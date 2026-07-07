using System;
using System.Threading;
using UnityEngine;

namespace VrTeleop
{
    /// <summary>
    /// AndroidJNI(AndroidJavaObject)로 MediaCodec("video/avc")를 직접 구동한다. (.aar 불필요)
    ///  - RtpH264Receiver 에서 SPS/PPS + access unit 을 받아 디코딩
    ///  - 출력 Surface 는 OVROverlay.externalSurfaceObject (외부 서피스)
    ///  - KEY_LOW_LATENCY=1 로 프레임 버퍼링 최소화
    ///
    /// 디코드 루프는 백그라운드 스레드(입력 큐 push + 출력 렌더)에서 돈다.
    /// 에디터에서는 Android API 가 없으므로 전부 no-op 로 컴파일된다.
    /// </summary>
    [RequireComponent(typeof(RtpH264Receiver))]
    public class VideoDecoder : MonoBehaviour
    {
        public int width = 2560;   // SBS 폭 (--scale 적용 시 맞춰 조정)
        public int height = 720;

        RtpH264Receiver _rx;
        IntPtr _surfacePtr = IntPtr.Zero;
        volatile bool _surfaceReady;
        Thread _thread;
        volatile bool _running;

        void Awake() => _rx = GetComponent<RtpH264Receiver>();

        /// <summary>OVROverlay.externalSurfaceObjectCreated 콜백에서 호출.</summary>
        public void SetSurface(IntPtr surface)
        {
            _surfacePtr = surface;
            _surfaceReady = surface != IntPtr.Zero;
        }

        void OnEnable()
        {
            _running = true;
            _thread = new Thread(DecodeLoop) { IsBackground = true, Name = "H264Decode" };
            _thread.Start();
        }

        void OnDisable()
        {
            _running = false;
            try { _thread?.Join(300); } catch { }
            _thread = null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void DecodeLoop()
        {
            AndroidJNI.AttachCurrentThread();
            AndroidJavaObject codec = null;
            AndroidJavaObject bufInfo = null;
            try
            {
                // SPS/PPS + surface 가 모두 준비될 때까지 대기
                while (_running && (!_rx.HasParameterSets || !_surfaceReady))
                    Thread.Sleep(10);
                if (!_running) return;

                codec = Configure();
                bufInfo = new AndroidJavaObject("android.media.MediaCodec$BufferInfo");
                Debug.Log("[Decoder] MediaCodec started");

                while (_running)
                {
                    // ---- 입력: 수신 큐 -> MediaCodec ----
                    if (_rx.Frames.TryDequeue(out var au))
                    {
                        int inIdx = codec.Call<int>("dequeueInputBuffer", 10000L);
                        if (inIdx >= 0)
                        {
                            var inBuf = codec.Call<AndroidJavaObject>("getInputBuffer", inIdx);
                            inBuf.Call<AndroidJavaObject>("clear");
                            inBuf.Call<AndroidJavaObject>("put", au.data); // byte[] -> jbyteArray
                            codec.Call("queueInputBuffer", inIdx, 0, au.data.Length, au.ptsUs, 0);
                        }
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }

                    // ---- 출력: 디코드된 프레임을 Surface 로 렌더 ----
                    int outIdx = codec.Call<int>("dequeueOutputBuffer", bufInfo, 0L);
                    while (outIdx >= 0)
                    {
                        codec.Call("releaseOutputBuffer", outIdx, true); // render=true
                        outIdx = codec.Call<int>("dequeueOutputBuffer", bufInfo, 0L);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Decoder] {e}");
            }
            finally
            {
                try { codec?.Call("stop"); } catch { }
                try { codec?.Call("release"); } catch { }
                AndroidJNI.DetachCurrentThread();
            }
        }

        AndroidJavaObject Configure()
        {
            using (var codecClass = new AndroidJavaClass("android.media.MediaCodec"))
            using (var fmtClass = new AndroidJavaClass("android.media.MediaFormat"))
            using (var bbClass = new AndroidJavaClass("java.nio.ByteBuffer"))
            {
                var codec = codecClass.CallStatic<AndroidJavaObject>("createDecoderByType", "video/avc");
                var fmt = fmtClass.CallStatic<AndroidJavaObject>("createVideoFormat", "video/avc", width, height);

                var sps = bbClass.CallStatic<AndroidJavaObject>("wrap", _rx.Sps);
                var pps = bbClass.CallStatic<AndroidJavaObject>("wrap", _rx.Pps);
                fmt.Call("setByteBuffer", "csd-0", sps);
                fmt.Call("setByteBuffer", "csd-1", pps);
                fmt.Call("setInteger", "low-latency", 1); // KEY_LOW_LATENCY (Android 11+/XR2 Gen2)

                // externalSurfaceObject(IntPtr) 를 AndroidJavaObject 로 감싸 configure 에 전달
                using (var surface = new AndroidJavaObject(_surfacePtr))
                {
                    codec.Call("configure", fmt, surface, null, 0);
                }
                codec.Call("start");
                return codec;
            }
        }
#else
        void DecodeLoop()
        {
            // 에디터/비-Android: 파이프라인 배선 확인용 no-op
            while (_running)
            {
                if (_rx.Frames.TryDequeue(out _)) { }
                Thread.Sleep(5);
            }
        }
#endif
    }
}
