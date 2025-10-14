using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Tf2;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using TMPro;

public class TFPublisher : MonoBehaviour
{
    
    public Transform centerEyeAnchor;
    public Transform leftHandAnchor;
    public Transform rightHandAnchor;
    public string headsetFrame = "hmd";
    public string handFrameLeft = "hand_left";
    public string handFrameRight = "hand_right";
    public string tfTopic = "tf";
    public string frameId = "world";
    public float publishHz = 30f;
    public TextMeshPro textMesh;       // 3D TextMeshPro 오브젝트
    float publishInterval;
    float nextPublishTime;
    private TFMessageMsg tfMessageMsg;
    private ROSConnection ros;
    
    private PoseStampedMsg headsetPoseMsg;
    private PoseStampedMsg leftHandMsg;
    private PoseStampedMsg rightHandMsg;

    private HeaderMsg headsetHeader;
    private HeaderMsg odomHeader;

    private string rootFrame = "vr_origin";
    private int _decimator = 1;
    private int _frameCounter = 0;
    
    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();

        ros.RegisterPublisher<TFMessageMsg>(tfTopic);

        headsetPoseMsg = new PoseStampedMsg();
        leftHandMsg = new PoseStampedMsg();
        rightHandMsg = new PoseStampedMsg();

        headsetHeader = new HeaderMsg();
        headsetHeader.frame_id = headsetFrame;

        odomHeader = new HeaderMsg();
        odomHeader.frame_id = rootFrame;

        tfMessageMsg = new TFMessageMsg(); 
        
        if (textMesh != null)
            textMesh.text = "";

        if (centerEyeAnchor == null)
        {
            // 자동으로 Camera.main 사용 (MainCamera 태그 필요)
            centerEyeAnchor = Camera.main?.transform;
        }
    }

    // Update is called once per frame
    void Update()
    {
        // For better performance we can choose to only publish every nth frame
        _frameCounter++;
        if (_frameCounter % _decimator != 0) return;
        _frameCounter = 0;
                
        Vector3<FLU> headsetPos = centerEyeAnchor.position.To<FLU>();
        Quaternion<FLU> headsetRot = centerEyeAnchor.rotation.To<FLU>();
        Vector3<FLU> leftHandPos = leftHandAnchor.position.To<FLU>();
        Quaternion<FLU> leftHandRot = leftHandAnchor.rotation.To<FLU>();
        Vector3<FLU> rightHandPos = rightHandAnchor.position.To<FLU>();
        Quaternion<FLU> rightHandRot = rightHandAnchor.rotation.To<FLU>();

        tfMessageMsg.transforms = new TransformStampedMsg[3];        

        // Publish the headset from origin
        tfMessageMsg.transforms[0] = new TransformStampedMsg();
        tfMessageMsg.transforms[0].header = odomHeader;
        tfMessageMsg.transforms[0].child_frame_id = headsetFrame;
        tfMessageMsg.transforms[0].transform = new TransformMsg();
        tfMessageMsg.transforms[0].transform.translation = headsetPos;
        tfMessageMsg.transforms[0].transform.rotation = headsetRot;

        // Publish the left hand to unity center
        tfMessageMsg.transforms[1] = new TransformStampedMsg();
        tfMessageMsg.transforms[1].header = odomHeader;
        tfMessageMsg.transforms[1].child_frame_id = handFrameLeft;
        tfMessageMsg.transforms[1].transform = new TransformMsg();
        tfMessageMsg.transforms[1].transform.translation = leftHandPos;
        tfMessageMsg.transforms[1].transform.rotation = leftHandRot;

        // Publish the right hand to unity center
        tfMessageMsg.transforms[2] = new TransformStampedMsg();
        tfMessageMsg.transforms[2].header = odomHeader;
        tfMessageMsg.transforms[2].child_frame_id = handFrameRight;
        tfMessageMsg.transforms[2].transform = new TransformMsg();
        tfMessageMsg.transforms[2].transform.translation = rightHandPos;
        tfMessageMsg.transforms[2].transform.rotation = rightHandRot;

        // Unity defaults to a quaternion with all 0s if the headset/hands arent detected, if this happens we set the identity quaternion
        for(int i = 0; i < tfMessageMsg.transforms.Length; i++)
            if (tfMessageMsg.transforms[i].transform.rotation.From<FLU>().Equals(default))
                tfMessageMsg.transforms[i].transform.rotation.w = 1;

        ros.Publish(tfTopic, tfMessageMsg);
    }
}
