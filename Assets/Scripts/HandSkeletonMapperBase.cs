using UnityEngine;
using UnityEngine.XR.Hands;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

public abstract class HandSkeletonMapperBase : MonoBehaviour
{
    [Header("ROS Topics")]
    public string leftJointTopic  = "left_hand/joint_states";
    public string rightJointTopic = "right_hand/joint_states";

    protected ROSConnection ros;

    protected JointStateMsg leftMsg  = new JointStateMsg();
    protected JointStateMsg rightMsg = new JointStateMsg();
    bool _registered = false;

    protected virtual void Awake()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<JointStateMsg>(leftJointTopic);
        ros.RegisterPublisher<JointStateMsg>(rightJointTopic);
    }

    /// <summary>
    /// 매 프레임 호출. 맵퍼가 내부에서 JointState를 구성해 발행한다.
    /// </summary>
    public abstract void Publish(XRHand leftHand, Transform leftAnchor,
                                 XRHand rightHand, Transform rightAnchor);
}
