using UnityEngine;
using UnityEditor;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;
using Unity.Robotics.ROSTCPConnector;
using TMPro;


public class RosWrenchSubscriber : MonoBehaviour
{
    ROSConnection _ros;
    public string topicName;

    public TextMeshPro textMesh;

    public Transform leftHand;
    public Transform rightHand;
    public ArrowGenerator arrowPrefab;
    private ArrowGenerator arrowInstance;
    private float scaleFactor = 10f;

    private Vector3 force;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _ros = ROSConnection.GetOrCreateInstance();
        _ros.Subscribe<WrenchStampedMsg>(topicName, OnWrench);

        arrowInstance = Instantiate(arrowPrefab, leftHand.position, Quaternion.identity, leftHand);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnWrench(WrenchStampedMsg msg)
    {
        textMesh.text = msg.wrench.force.x.ToString() + "\n" + msg.wrench.force.y.ToString() + "\n" + msg.wrench.force.z.ToString();
        force = new Vector3((float)msg.wrench.force.x, (float)msg.wrench.force.y, (float)msg.wrench.force.z);

        arrowInstance.gameObject.SetActive(true);
        arrowInstance.transform.position = leftHand.position;
        arrowInstance.transform.rotation = Quaternion.LookRotation(force.normalized, Vector3.up);
        float length = force.magnitude * scaleFactor;
        arrowInstance.stemLength = length * scaleFactor;
    
    }
}
