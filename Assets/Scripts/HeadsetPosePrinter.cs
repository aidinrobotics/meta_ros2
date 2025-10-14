using UnityEngine;
using TMPro; 
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry; // PoseStamped

public class HeadsetPosePrinter : MonoBehaviour
{
    // OVR Camera Rig의 CenterEyeAnchor를 참조
    public Transform centerEyeAnchor;
    public TextMeshPro textMesh;       // 3D TextMeshPro 오브젝트
    ROSConnection ros;
    public string topicName = "hmd_pose";
    public string frameId = "vr_origin";
    public float publishHz = 30f;
    float publishInterval;
    float nextPublishTime;
    private PoseStampedMsg headsetPoseMsg;

    void Start()
    {
        if (centerEyeAnchor == null)
        {
            // 자동으로 Camera.main 사용 (MainCamera 태그 필요)
            centerEyeAnchor = Camera.main?.transform;
        }
        publishInterval = 1.0f / publishHz;

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseStampedMsg>(topicName);

        headsetPoseMsg = new PoseStampedMsg();
    }

    void Update()
    {
        if (centerEyeAnchor == null || textMesh == null) return;
        if (Time.time < nextPublishTime) return;
        nextPublishTime = Time.time + publishInterval;

        // 현재 회전값 (Quaternion → Euler)
        Vector3 euler = centerEyeAnchor.rotation.eulerAngles;

        // Unity 좌표계에서 eulerAngles는 (x = pitch, y = yaw, z = roll)
        float pitch = euler.x;
        float yaw   = euler.y;
        float roll  = euler.z;

        textMesh.text = $"Roll: {roll:F1}\nPitch: {pitch:F1}\nYaw: {yaw:F1}";

        // Unity -> ROS(FLU) 좌표/자세 변환
        Vector3<FLU> posFLU = centerEyeAnchor.position.To<FLU>();
        Quaternion<FLU> rotFLU = centerEyeAnchor.rotation.To<FLU>();

        
        headsetPoseMsg.header.frame_id = frameId;
        headsetPoseMsg.pose.position.x = posFLU.x;
        headsetPoseMsg.pose.position.y = posFLU.y;
        headsetPoseMsg.pose.position.z = posFLU.z;

        headsetPoseMsg.pose.orientation.x = rotFLU.x;
        headsetPoseMsg.pose.orientation.y = rotFLU.y;
        headsetPoseMsg.pose.orientation.z = rotFLU.z;
        headsetPoseMsg.pose.orientation.w = rotFLU.w;
        ros.Publish(topicName, headsetPoseMsg);
    }
}
