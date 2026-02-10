using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry; // PoseStamped

public class HandPosePublisher : MonoBehaviour
{
    public Transform handAnchor;
    ROSConnection ros;
    public string topicName = "hand_pose";
    public string frameId = "world";
    public float publishHz = 60f;
    float publishInterval;
    float nextPublishTime;
    private PoseStampedMsg handPoseMsg;
    

    void Start()
    {
        if (handAnchor == null)
        {
            // 자동으로 Camera.main 사용 (MainCamera 태그 필요)
            handAnchor = Camera.main?.transform;
        }
        publishInterval = 1.0f / publishHz;

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseStampedMsg>(topicName);

        handPoseMsg = new PoseStampedMsg();
        
    }

    // Update is called once per frame
    void Update()
    {
        if (handAnchor == null ) return;
        if (Time.time < nextPublishTime) return;
        nextPublishTime = Time.time + publishInterval;

        // 현재 회전값 (Quaternion → Euler)
        Vector3 euler = handAnchor.rotation.eulerAngles;

        // Unity 좌표계에서 eulerAngles는 (x = pitch, y = yaw, z = roll)
        float pitch = euler.x;
        float yaw   = euler.y;
        float roll  = euler.z;


        // Unity -> ROS(FLU) 좌표/자세 변환
        Vector3<FLU> posFLU = handAnchor.position.To<FLU>();
        Quaternion<FLU> rotFLU = handAnchor.rotation.To<FLU>();

        
        handPoseMsg.header.frame_id = frameId;
        handPoseMsg.pose.position.x = posFLU.x;
        handPoseMsg.pose.position.y = posFLU.y;
        handPoseMsg.pose.position.z = posFLU.z;

        handPoseMsg.pose.orientation.x = rotFLU.x;
        handPoseMsg.pose.orientation.y = rotFLU.y;
        handPoseMsg.pose.orientation.z = rotFLU.z;
        handPoseMsg.pose.orientation.w = rotFLU.w;
        ros.Publish(topicName, handPoseMsg);
        
    }
}
