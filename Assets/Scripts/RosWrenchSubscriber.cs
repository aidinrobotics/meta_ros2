using UnityEngine;
using UnityEditor;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;
using Unity.Robotics.ROSTCPConnector;
using TMPro;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class RosWrenchSubscriber : MonoBehaviour
{
    ROSConnection _ros;
    public string topicName;

    public TextMeshPro textMesh;

    public Transform leftHand;
    public Transform rightHand;

    private Vector3 force;
    public float scaleFactor = 2f;
    public float stemWidth = 0.5f;
    public float tipWidth = 2f;
    public float tipRatio = 0.2f;
    public int radialSegments = 12;
    public Shader vertexShader;
    public Color stemColor = Color.white;
    public Color tipColor = Color.red;
    
 
    [System.NonSerialized]
    public List<Vector3> verticesList;
    [System.NonSerialized]
    public List<int> trianglesList;
    Mesh mesh;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _ros = ROSConnection.GetOrCreateInstance();
        _ros.Subscribe<WrenchStampedMsg>(topicName, OnWrench);
        mesh = new Mesh();
        this.GetComponent<MeshFilter>().mesh = mesh;

    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // void OnWrench(WrenchStampedMsg msg)
    // {
    //     if (textMesh != null)
    //         textMesh.text = msg.wrench.force.x.ToString() + "\n" + msg.wrench.force.y.ToString() + "\n" + msg.wrench.force.z.ToString();
    //     force = new Vector3((float)msg.wrench.force.x, (float)msg.wrench.force.y, (float)msg.wrench.force.z);
    //     stemLength = force.magnitude;

    //     Vector3 forceDirection = force.normalized;
        
    //     // draw arrow
    //     verticesList = new List<Vector3>();
    //     trianglesList = new List<int>();

    //     //stem setup
    //     Vector3 stemOrigin = Vector3.zero;
    //     float stemHalfWidth = stemWidth/2f;
    //     //Stem points
    //     verticesList.Add(stemOrigin+(stemHalfWidth*Vector3.down));
    //     verticesList.Add(stemOrigin+(stemHalfWidth*Vector3.up));
    //     verticesList.Add(verticesList[0]+(stemLength*Vector3.right));
    //     verticesList.Add(verticesList[1]+(stemLength*Vector3.right));
 
    //     //Stem triangles
    //     trianglesList.Add(0);
    //     trianglesList.Add(1);
    //     trianglesList.Add(3);
 
    //     trianglesList.Add(0);
    //     trianglesList.Add(3);
    //     trianglesList.Add(2);
        
    //     //tip setup
    //     Vector3 tipOrigin = stemLength*Vector3.right;
    //     float tipHalfWidth = tipWidth/2;
 
    //     //tip points
    //     verticesList.Add(tipOrigin+(tipHalfWidth*Vector3.up));
    //     verticesList.Add(tipOrigin+(tipHalfWidth*Vector3.down));
    //     verticesList.Add(tipOrigin+(tipLength*Vector3.right));
 
    //     //tip triangle
    //     trianglesList.Add(4);
    //     trianglesList.Add(6);
    //     trianglesList.Add(5);
 
    //     //assign lists to mesh.
    //     mesh.vertices = verticesList.ToArray();
    //     mesh.triangles = trianglesList.ToArray();
    // }

    void OnWrench(WrenchStampedMsg msg)
    {
        if (textMesh != null)
            textMesh.text = $"{msg.wrench.force.x:F2}\n{msg.wrench.force.y:F2}\n{msg.wrench.force.z:F2}";

        // Force and direction
        Vector3 force = new Vector3((float)msg.wrench.force.x, (float)msg.wrench.force.y, (float)msg.wrench.force.z);
        float magnitude = force.magnitude * scaleFactor;

        Vector3 forceDir = force.normalized;

        // === Arrow parameter ===
        float stemRadius = stemWidth / 2f;
        float stemLength = magnitude * (1 - tipRatio);
        float tipLength = magnitude * tipRatio;
        float tipRadius = tipWidth / 2f;

        // === Create vertex lists ===
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();

        // Stem (orange)
        for (int i = 0; i < radialSegments; i++)
        {
            float angle = (float)i / radialSegments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * stemRadius;
            float y = Mathf.Sin(angle) * stemRadius;
            vertices.Add(new Vector3(0, y, x));
            vertices.Add(new Vector3(stemLength, y, x));
            colors.Add(stemColor);
            colors.Add(stemColor);
        }

        for (int i = 0; i < radialSegments; i++)
        {
            int i0 = i * 2;
            int i1 = (i * 2 + 2) % (radialSegments * 2);
            int i2 = i0 + 1;
            int i3 = (i1 + 1) % (radialSegments * 2);

            triangles.Add(i0); triangles.Add(i2); triangles.Add(i3);
            triangles.Add(i0); triangles.Add(i3); triangles.Add(i1);
        }

        // Tip (red)
        int tipBaseStart = vertices.Count;
        for (int i = 0; i < radialSegments; i++)
        {
            float angle = (float)i / radialSegments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * tipRadius;
            float y = Mathf.Sin(angle) * tipRadius;
            vertices.Add(new Vector3(stemLength, y, x));
            colors.Add(tipColor);
        }

        int apexIndex = vertices.Count;
        vertices.Add(new Vector3(stemLength + tipLength, 0, 0));
        colors.Add(tipColor);

        for (int i = 0; i < radialSegments; i++)
        {
            int i0 = tipBaseStart + i;
            int i1 = tipBaseStart + (i + 1) % radialSegments;
            triangles.Add(i0); triangles.Add(i1); triangles.Add(apexIndex);
        }

        // Apply rotation
        Quaternion rot = Quaternion.FromToRotation(Vector3.right, forceDir);
        for (int i = 0; i < vertices.Count; i++)
            vertices[i] = rot * vertices[i];

        // Assign to mesh
        Mesh mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);  // 🎨 정점 색상 적용
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Assign to filter/renderer
        GetComponent<MeshFilter>().mesh = mesh;

        var meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = gameObject.AddComponent<MeshRenderer>();

        // ✨ Vertex Color를 지원하는 셰이더 사용
        meshRenderer.material = new Material(vertexShader);
    }
}
