using UnityEngine;
#if USE_ROS_TCP
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry; // .To<FLU>() 좌표 변환
using RosMessageTypes.Geometry;
#endif

namespace VrTeleop
{
    /// <summary>
    /// 헤드셋/손 포즈를 ROS-TCP(TCP)로 발행 (업링크).
    ///  - /vr/head_pose : PoseStamped
    ///  - /vr/left_hand, /vr/right_hand : PoseArray (관절 포즈)
    ///
    /// 필수: Unity(왼손,Y-up) -> ROS(오른손,Z-up) 변환 .To<FLU>().
    ///       빼먹으면 로봇이 거울/직교로 움직인다(가이드 1번 버그).
    ///
    /// 패키지 설치 후 Scripting Define Symbols 에 USE_ROS_TCP, USE_META_XR 추가.
    /// </summary>
    public class VrTeleopPublisher : MonoBehaviour
    {
        [Tooltip("CenterEyeAnchor")]
        public Transform head;

        [Tooltip("발행 주기(Hz)")]
        public float rateHz = 60f;

        [Tooltip("포즈 header.frame_id (로봇쪽 기준 프레임)")]
        public string frameId = "vr_origin";

#if USE_META_XR
        public OVRSkeleton leftHand;
        public OVRSkeleton rightHand;
#endif

        float _period, _acc;

#if USE_ROS_TCP
        ROSConnection _ros;

        void Start()
        {
            _period = 1f / rateHz;
            _ros = ROSConnection.GetOrCreateInstance();
            _ros.RegisterPublisher<PoseStampedMsg>("/vr/hmd_pose");
            _ros.RegisterPublisher<PoseArrayMsg>("/vr/left_hand_pose");
            _ros.RegisterPublisher<PoseArrayMsg>("/vr/right_hand_pose");
        }

        void Update()
        {
            _acc += Time.deltaTime;
            if (_acc < _period) return;
            _acc -= _period; // 0으로 리셋하면 오버슈트를 버려 실효 주기가 절반이 됨

            if (head != null)
            {
                var h = new PoseStampedMsg
                {
                    header = new RosMessageTypes.Std.HeaderMsg { frame_id = frameId },
                    pose = new PoseMsg
                    {
                        position = head.position.To<FLU>(),
                        orientation = head.rotation.To<FLU>()
                    }
                };
                _ros.Publish("/vr/hmd_pose", h);
            }

#if USE_META_XR
            if (leftHand != null) _ros.Publish("/vr/left_hand_pose", HandToPoseArray(leftHand));
            if (rightHand != null) _ros.Publish("/vr/right_hand_pose", HandToPoseArray(rightHand));
#endif
        }

#if USE_META_XR
        // 주의: Quest 스켈레톤(~24~26 관절)과 dex-retargeting 입력(Manus 25)의 관절 정의가
        // 다르므로, 여기서 필요한 관절만 골라 순서를 맞춘다(매핑 테이블은 로봇쪽 규격에 맞춰 확정).
        PoseArrayMsg HandToPoseArray(OVRSkeleton skel)
        {
            var bones = skel.Bones;
            var poses = new PoseMsg[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                var t = bones[i].Transform;
                poses[i] = new PoseMsg
                {
                    position = t.position.To<FLU>(),
                    orientation = t.rotation.To<FLU>()
                };
            }
            return new PoseArrayMsg
            {
                header = new RosMessageTypes.Std.HeaderMsg { frame_id = frameId },
                poses = poses
            };
        }
#endif
#else
        void Start()
        {
            Debug.LogWarning("[VrTeleop] ROS-TCP-Connector 미설치(USE_ROS_TCP 미정의). 발행 비활성.");
        }
#endif
    }
}
