using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosMessageTypes.Std;
using RosMessageTypes.Sensor;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine.UI;
using System.Threading.Tasks;
using TMPro;
using UnityEngine.Experimental.Rendering;

/// <summary>
///
/// </summary>
public class RosImageSubscriber : MonoBehaviour
{
    ROSConnection _ros;
    public TextMeshPro textMesh;   
    
    public ComputeShader debayer;
    public Material material;

    public RawImage rawImage;

    private RenderTexture _texture2D;
    protected Transform _Img;
    protected Image _icon;
    public enum DebayerMode
    {
        RGGB,
        BGGR,
        GBRG,
        GRBG,
        None = -1,
    }
    public DebayerMode debayerType = DebayerMode.GRBG;
    public string topicName = "/camera/camera/color/image_raw/compressed";

    void Start()
    {
        
        _ros = ROSConnection.GetOrCreateInstance();

        if (topicName.EndsWith("compressed"))
        {
            _ros.Subscribe<CompressedImageMsg>(topicName, OnCompressed);
        }
        else
        {
            _ros.Subscribe<ImageMsg>(topicName, OnImage);
        }

    }

    private void Update()
    {
       
    }

    private void OnDestroy()
    {
        if (topicName != null)
            _ros.Unsubscribe(topicName);
    }

    void OnCompressed(CompressedImageMsg msg)
    {

        try
        {   
            if (textMesh != null)
                textMesh.text = msg.header.stamp.sec.ToString() + "." + msg.header.stamp.nanosec.ToString();

            Texture2D _input = new Texture2D(2, 2);
            ImageConversion.LoadImage(_input, msg.data);
            _input.Apply();
            
            if (material != null)
            {

                SetupTex(_input.width, _input.height);

                if (debayerType == DebayerMode.None)
                {
                    RenderTexture.active = _texture2D;
                    Graphics.Blit(_input, _texture2D);
                    RenderTexture.active = null;
                    return;
                }

                // debayer the image using compute shader
                if (debayer != null)
                {
                    debayer.SetInt("mode", (int)debayerType);
                    debayer.SetTexture(0, "Input", _input);
                    debayer.SetTexture(0, "Result", _texture2D);
                    debayer.Dispatch(0, _input.width / 2, _input.height / 2, 1);

                }
            }
            Destroy(_input);

        }
        catch (System.Exception e)
        {
            Debug.LogError(e);
        }
    }

    void OnImage(ImageMsg msg)
    {
        SetupTex((int)msg.width, (int)msg.height);

        try
        {
            // _texture2D.LoadRawTextureData(msg.data);
            // _texture2D.Apply();
        }
        catch (System.Exception e)
        {
            Debug.LogError(e);
        }
        Resize();
    }
    protected virtual void Resize()
    {
        if (_texture2D == null) return;
        float aspectRatio = (float)_texture2D.width / (float)_texture2D.height;

        float width = _Img.transform.localScale.x;
        float height = width / aspectRatio;

        _Img.localScale = new Vector3(width, 1, height);
    }

    protected virtual void SetupTex(int width = 2, int height = 2)
    {
        if (_texture2D == null)
        {
            _texture2D = new RenderTexture(width, height, 0, GraphicsFormat.R8G8B8A8_UNorm);
            _texture2D.enableRandomWrite = true;
            _texture2D.Create();
            material.SetTexture("_BaseMap", _texture2D);
            rawImage.texture = _texture2D;
        }
    }

    /// <summary>
    /// For debugging, render the current image to a file
    /// </summary>
    public void Render()
    {
        // Save the _uiImage rendertexture to a file
        RenderTexture.active = _texture2D;
        Texture2D tex = new Texture2D(_texture2D.width, _texture2D.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, _texture2D.width, _texture2D.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        byte[] bytes = tex.EncodeToPNG();

        string filename = topicName.Replace("/", "_");
        System.IO.File.WriteAllBytes(Application.dataPath + "/../" + filename + ".png", bytes);
    }
}