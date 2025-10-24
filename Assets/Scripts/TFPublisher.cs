using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Sensor;
using RosMessageTypes.Tf2;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using TMPro;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class TFPublisher : MonoBehaviour
{
    public Transform centerEyeAnchor;
    public Transform leftHandAnchor;
    public Transform rightHandAnchor;

    public string rootFrame = "vr_world";
    public string headsetFrame = "hmd";
    public string handFrameLeft = "hand_left";
    public string handFrameRight = "hand_right";
    public string tfTopic = "tf";
    public string tfHandsTopic = "tf_hands";
    HeaderMsg odomHeader, leftHeader, rightHeader;

    public float publishHz = 30f;
    public TextMeshPro textMesh;

    float publishInterval;
    float nextPublishTime;

    private ROSConnection ros;


    private int _decimator = 1;
    private int _frameCounter = 0;

    // XR Hands
    XRHandSubsystem _handSubsystem;
    public HandSkeletonMapperBase mapper;  // ← 인스펙터에서 SkeletonMapper 드롭

    // 재사용 버퍼
    private readonly List<TransformStampedMsg> _tfBuffer = new List<TransformStampedMsg>(128);
    XRHandJointID[] _ids;
    TransformStampedMsg[] _tfArray;         // 고정 길이
    TFMessageMsg _tfMsg;                    // 재사용

    string[] _leftChild, _rightChild;       // 미리 만든 child_frame_id
    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<TFMessageMsg>(tfTopic);
        ros.RegisterPublisher<TFMessageMsg>(tfHandsTopic);
        
        odomHeader  = new HeaderMsg { frame_id = rootFrame };
        leftHeader  = new HeaderMsg { frame_id = handFrameLeft  };
        rightHeader = new HeaderMsg { frame_id = handFrameRight };

        if (textMesh != null) textMesh.text = "";

        if (centerEyeAnchor == null)
            centerEyeAnchor = Camera.main?.transform;

        // XRHandSubsystem 가져오기
        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        _handSubsystem = loader?.GetLoadedSubsystem<XRHandSubsystem>();

        if (_handSubsystem == null)
            Debug.LogWarning("XRHandSubsystem not found. Check OpenXR + XR Hands settings.");
        
        if (mapper != null)
        {
            ros.RegisterPublisher<JointStateMsg>(mapper.leftJointTopic);
            ros.RegisterPublisher<JointStateMsg>(mapper.rightJointTopic);
        }

        publishInterval = (publishHz > 0f) ? (1f / publishHz) : 0f;

        // 관절 ID 목록 준비
        int JOINTS = XRHandJointID.EndMarker.ToIndex();
        _ids = new XRHandJointID[JOINTS];
        for (int i = 0; i < JOINTS; ++i) _ids[i] = XRHandJointIDUtility.FromIndex(i);

        // child_frame_id 미리 생성(문자열 GC 제거)
        _leftChild = new string[JOINTS];
        _rightChild = new string[JOINTS];
        for (int i = 0; i < JOINTS; ++i) {
            _leftChild[i]  = HandJointFrameName(handFrameLeft,  _ids[i]);
            _rightChild[i] = HandJointFrameName(handFrameRight, _ids[i]);
        }

        // TF 배열/메시지 선할당: 3(base) + (왼손 JOINTS) + (오른손 JOINTS)
        int N = 3 + JOINTS*2;
        _tfArray = new TransformStampedMsg[N];
        for (int i = 0; i < N; ++i) {
            _tfArray[i] = new TransformStampedMsg {
                header = odomHeader,                   // 기본값(필요 시 후에 덮어씀)
                child_frame_id = "",                   // 후에 덮어씀
                transform = new TransformMsg()         // 재사용
            };
        }
        // 베이스 3개 child_frame 고정
        _tfArray[0].child_frame_id = headsetFrame;
        _tfArray[1].child_frame_id = handFrameLeft;
        _tfArray[2].child_frame_id = handFrameRight;

        _tfMsg = new TFMessageMsg { transforms = _tfArray };

    }

    void OnEnable()
    {
        if (_handSubsystem != null)
            _handSubsystem.updatedHands += OnUpdatedHands;
    }

    void OnDisable()
    {
        if (_handSubsystem != null)
            _handSubsystem.updatedHands -= OnUpdatedHands;
    }

    // XR Hands가 갱신되는 타이밍에 관절 Pose를 최신 상태로 확보
    void OnUpdatedHands(XRHandSubsystem subsystem,
                        XRHandSubsystem.UpdateSuccessFlags flags,
                        XRHandSubsystem.UpdateType type) { /* no-op: Update()에서 읽음 */ }

    void Update()
    {
        _frameCounter++;
        if (_frameCounter % _decimator != 0) return;
        _frameCounter = 0;

        if (publishInterval > 0f && Time.time < nextPublishTime) return;
        nextPublishTime = Time.time + publishInterval;

        // _tfBuffer.Clear();

        // // 1) HMD / Hands 베이스 프레임(월드 기준) 발행
        // PublishBaseTFs();

        // // 2) 손가락 관절 TF (각 손 프레임 기준, 상대 Pose)
        // if (_handSubsystem != null)
        // {
        //     PublishHandJointsRelative(_handSubsystem.leftHand,  leftHandAnchor,  handFrameLeft);
        //     PublishHandJointsRelative(_handSubsystem.rightHand, rightHandAnchor, handFrameRight);
        // }

        // // 3) Publish
        // var tfMsg = new TFMessageMsg { transforms = _tfBuffer.ToArray() };
        // ros.Publish(tfTopic, tfMsg);
        // ros.Publish(tfTopic+"_hands", tfMsg);

        // // Optional: 맵퍼에 Hand 데이터 전달 → 맵퍼가 자체적으로 JointState 발행
        // // if (_handSubsystem != null && mapper != null)
        // {
        //     mapper.Publish(_handSubsystem.leftHand,  leftHandAnchor,
        //                    _handSubsystem.rightHand, rightHandAnchor);
        // }
        // --- 베이스 3개: 값만 갱신 ---
        SetTF(_tfArray[0], odomHeader, headsetFrame, centerEyeAnchor.position, centerEyeAnchor.rotation);
        SetTF(_tfArray[1], odomHeader, handFrameLeft,  leftHandAnchor.position,  leftHandAnchor.rotation);
        SetTF(_tfArray[2], odomHeader, handFrameRight, rightHandAnchor.position, rightHandAnchor.rotation);

        // --- 손 관절들: 값만 갱신 (고정 슬롯) ---
        int baseIdx = 3;
        // Left
        if (_handSubsystem != null && leftHandAnchor != null) {
            var hand = _handSubsystem.leftHand;
            for (int i = 0; i < _ids.Length; ++i) {
                var joint = hand.GetJoint(_ids[i]);
                var slot = _tfArray[baseIdx + i];
                slot.header = leftHeader;                          // 부모 hand_left
                slot.child_frame_id = _leftChild[i];               // 미리 만든 문자열
                if (joint.TryGetPose(out var pose)) {
                    var posRel = leftHandAnchor.InverseTransformPoint(pose.position);
                    var rotRel = Quaternion.Inverse(leftHandAnchor.rotation) * pose.rotation;
                    slot.transform.translation = posRel.To<FLU>();
                    slot.transform.rotation    = rotRel.To<FLU>();
                    if (slot.transform.rotation.From<FLU>().Equals(default)) slot.transform.rotation.w = 1;
                }
                // 추적불가면 이전값 유지(또는 여기서 identity로 초기화해도 됨)
            }
        }
        // Right
        if (_handSubsystem != null && rightHandAnchor != null) {
            var hand = _handSubsystem.rightHand;
            int off = baseIdx + _ids.Length;
            for (int i = 0; i < _ids.Length; ++i) {
                var joint = hand.GetJoint(_ids[i]);
                var slot = _tfArray[off + i];
                slot.header = rightHeader;                         // 부모 hand_right
                slot.child_frame_id = _rightChild[i];
                if (joint.TryGetPose(out var pose)) {
                    var posRel = rightHandAnchor.InverseTransformPoint(pose.position);
                    var rotRel = Quaternion.Inverse(rightHandAnchor.rotation) * pose.rotation;
                    slot.transform.translation = posRel.To<FLU>();
                    slot.transform.rotation    = rotRel.To<FLU>();
                    if (slot.transform.rotation.From<FLU>().Equals(default)) slot.transform.rotation.w = 1;
                }
            }
        }

        // --- Publish (객체 재사용/무할당) ---
        ros.Publish(tfTopic, _tfMsg);
        ros.Publish(tfHandsTopic, _tfMsg);
    }
    static void SetTF(TransformStampedMsg t, HeaderMsg h, string child, Vector3 pos, Quaternion rot)
    {
        t.header = h;
        // child_frame_id는 Start에서 고정했으니 생략 가능 (바뀌지 않으면 재할당 금지)
        t.transform.translation = pos.To<FLU>();
        t.transform.rotation    = rot.To<FLU>();
        if (t.transform.rotation.From<FLU>().Equals(default)) t.transform.rotation.w = 1;
    }

    void PublishBaseTFs()
    {
        // 안전 체크
        if (centerEyeAnchor == null || leftHandAnchor == null || rightHandAnchor == null)
            return;

        // HMD
        {
            var t = new TransformStampedMsg
            {
                header = odomHeader,
                child_frame_id = headsetFrame,
                transform = new TransformMsg
                {
                    translation = centerEyeAnchor.position.To<FLU>(),
                    rotation    = centerEyeAnchor.rotation.To<FLU>()
                }
            };
            // Unity가 rotation all-zero 찍는 경우 보정
            if (t.transform.rotation.From<FLU>().Equals(default)) t.transform.rotation.w = 1;
            _tfBuffer.Add(t);
        }

        // Left hand base
        {
            var t = new TransformStampedMsg
            {
                header = odomHeader,
                child_frame_id = handFrameLeft,
                transform = new TransformMsg
                {
                    translation = leftHandAnchor.position.To<FLU>(),
                    rotation    = leftHandAnchor.rotation.To<FLU>()
                }
            };
            if (t.transform.rotation.From<FLU>().Equals(default)) t.transform.rotation.w = 1;
            _tfBuffer.Add(t);
        }

        // Right hand base
        {
            var t = new TransformStampedMsg
            {
                header = odomHeader,
                child_frame_id = handFrameRight,
                transform = new TransformMsg
                {
                    translation = rightHandAnchor.position.To<FLU>(),
                    rotation    = rightHandAnchor.rotation.To<FLU>()
                }
            };
            if (t.transform.rotation.From<FLU>().Equals(default)) t.transform.rotation.w = 1;
            _tfBuffer.Add(t);
        }
    }

    void PublishHandJointsRelative(XRHand hand, Transform handAnchor, string handFrame)
    {
        if (handAnchor == null) return;
        if (!hand.isTracked)   return;

        // 모든 관절 순회
        int count = XRHandJointID.EndMarker.ToIndex();
        for (int i = 0; i < count; ++i)
        {
            var id = XRHandJointIDUtility.FromIndex(i);
            var joint = hand.GetJoint(id);

            if (!joint.TryGetPose(out var pose)) continue;

            // 월드 -> 손 프레임(handAnchor) 상대 변환
            Vector3 posRel = handAnchor.InverseTransformPoint(pose.position);
            Quaternion rotRel = Quaternion.Inverse(handAnchor.rotation) * pose.rotation;

            // frame 이름: hand_left/index_proximal 같은 형식으로 안전하게
            string child = HandJointFrameName(handFrame, id);

            var t = new TransformStampedMsg
            {
                header = new HeaderMsg { frame_id = handFrame }, // 부모 프레임: hand_left/right
                child_frame_id = child,
                transform = new TransformMsg
                {
                    translation = posRel.To<FLU>(),
                    rotation    = rotRel.To<FLU>()
                }
            };

            if (t.transform.rotation.From<FLU>().Equals(default)) t.transform.rotation.w = 1;
            _tfBuffer.Add(t);
        }
    }

    static string HandJointFrameName(string handFrame, XRHandJointID id)
    {
        // XRHandJointID를 ROS-friendly 소문자/언더스코어로 변환
        // 예: IndexProximal -> index_proximal
        string j = id.ToString();
        var sb = new System.Text.StringBuilder(j.Length + 16);
        for (int k = 0; k < j.Length; ++k)
        {
            char c = j[k];
            if (char.IsUpper(c) && k > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return $"{handFrame}/{sb}";
    }
}
