using UnityEngine;

namespace VrTeleop
{
    /// <summary>
    /// OVROverlay 를 외부 서피스 모드로 만들고, 만들어진 Surface 를 VideoDecoder 에 넘긴다.
    /// SBS 한 장을 좌(0,0,0.5,1)·우(0.5,0,0.5,1) 로 쪼개 각 눈에 넣는다.
    ///
    /// 씬 배선: 이 컴포넌트 + OVROverlay + VideoDecoder + RtpH264Receiver 를
    /// 같은 GameObject(또는 참조 연결)에 둔다.
    ///
    /// OVROverlay 는 Meta XR Core SDK 에 포함. 미설치 시 아래 심볼로 컴파일에서 제외된다.
    /// (Meta SDK 설치 후 Player Settings > Scripting Define Symbols 에 USE_META_XR 추가)
    /// </summary>
    public class VideoOverlayController : MonoBehaviour
    {
        public VideoDecoder decoder;

        [Header("스테레오 튜닝")]
        [Tooltip("좌우 눈 영상의 수평 시차(컨버전스). +면 두 영상이 바깥으로 벌어짐. 편한 값 찾아 고정")]
        [Range(-0.3f, 0.3f)] public float convergence = 0.122f;

        [Tooltip("좌/우 눈이 뒤바뀐 것 같으면(깊이가 반대로 느껴지면) 체크")]
        public bool swapEyes = false;

        [Tooltip("X(-)/Y(+) convergence(왼손), A(가까이)/B(멀리) 패널 거리(오른손). 좌우 스왑은 인스펙터에서")]
        public bool enableButtonTuning = true;
        public float tuneSpeed = 0.2f;

        [Header("영상 패널 거리 (X/Y 버튼)")]
        [Tooltip("X(가까이)/Y(멀리) 홀드 시 초당 이동 거리(m)")]
        public float distanceSpeed = 1.5f;
        public float minDistance = 0.5f;
        public float maxDistance = 10f;

#if USE_META_XR
        OVROverlay _overlay;
        RtpH264Receiver _rx;
        bool _surfaceInit;

        void Awake()
        {
            _overlay = GetComponent<OVROverlay>();
            if (_overlay == null) _overlay = gameObject.AddComponent<OVROverlay>();
            _rx = GetComponent<RtpH264Receiver>();

            _overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
            _overlay.isExternalSurface = true;
            _overlay.overrideTextureRectMatrix = true;
            // 실제 해상도(SPS)가 확정될 때까지 external surface 생성을 보류한다.
            // (external surface 는 생성 전에 크기를 정해야 하므로 SPS 파싱 후 켠다)
            _overlay.enabled = false;
        }

        // SPS 로 해상도가 확정되면 그 크기로 surface 를 생성 (영상 크기에 유동 대응)
        void InitSurface()
        {
            int w = _rx != null ? _rx.VideoWidth : 0;
            int h = _rx != null ? _rx.VideoHeight : 0;
            if (w <= 0 || h <= 0)
            {
                // SPS 는 왔지만 파싱 실패 -> VideoDecoder 인스펙터 기본값으로 폴백
                if (_rx == null || !_rx.HasParameterSets || decoder == null) return; // 아직 SPS 안 옴 -> 대기
                w = decoder.width; h = decoder.height;
            }

            _overlay.externalSurfaceWidth = w;
            _overlay.externalSurfaceHeight = h;
            if (decoder != null) { decoder.width = w; decoder.height = h; }

            _overlay.externalSurfaceObjectCreated += OnSurfaceCreated;
            _overlay.enabled = true;   // OnEnable -> external surface 생성
            ApplyRects();
            _lastC = convergence;
            _lastSwap = swapEyes;
            _surfaceInit = true;
            Debug.Log($"[Overlay] video {w}x{h} -> external surface");
        }

        // SBS 좌/우 절반을 각 눈에 매핑 + convergence 만큼 dest 를 좌우로 밀어 수평 시차 조정
        void ApplyRects()
        {
            var srcL = new Rect(0f, 0f, 0.5f, 1f);
            var srcR = new Rect(0.5f, 0f, 0.5f, 1f);
            if (swapEyes) { var t = srcL; srcL = srcR; srcR = t; }

            float c = convergence;
            var destL = new Rect(-c, 0f, 1f, 1f); // 왼눈 영상: 왼쪽으로
            var destR = new Rect(c, 0f, 1f, 1f);  // 오른눈 영상: 오른쪽으로 (반대 방향 = 수평 시차)

            _overlay.SetSrcDestRects(srcL, srcR, destL, destR);
            _overlay.UpdateTextureRectMatrix();
        }

        float _lastC = float.NaN;
        bool _lastSwap;

        void Update()
        {
            if (!_surfaceInit) { InitSurface(); return; }

            HandleInput();

            // 값이 바뀌었을 때만 rect 재적용 (버튼/Inspector 변경 모두 여기서 반영)
            if (convergence != _lastC || swapEyes != _lastSwap)
            {
                bool swapChanged = swapEyes != _lastSwap;
                _lastC = convergence;
                _lastSwap = swapEyes;
                ApplyRects();
                if (swapChanged || Time.frameCount % 15 == 0)
                    Debug.Log($"[Overlay] convergence={convergence:F3} swapEyes={swapEyes}");
            }
        }

        void HandleInput()
        {
            if (!enableButtonTuning) return;

            // X(Three)/Y(Four): convergence −/+ (왼손, 홀드 연속)
            float dir = 0f;
            if (OVRInput.Get(OVRInput.Button.Three)) dir -= 1f;
            if (OVRInput.Get(OVRInput.Button.Four)) dir += 1f;
            if (dir != 0f)
                convergence = Mathf.Clamp(convergence + dir * tuneSpeed * Time.deltaTime, -0.3f, 0.3f);

            // A(One)/B(Two): 영상 패널 거리(Z) 가까이/멀리 (오른손, 홀드 연속)
            float dz = 0f;
            if (OVRInput.Get(OVRInput.Button.One)) dz -= 1f; // A: 가까이
            if (OVRInput.Get(OVRInput.Button.Two)) dz += 1f; // B: 멀리
            if (dz != 0f)
            {
                var p = transform.localPosition;
                p.z = Mathf.Clamp(p.z + dz * distanceSpeed * Time.deltaTime, minDistance, maxDistance);
                transform.localPosition = p;
                if (Time.frameCount % 15 == 0) Debug.Log($"[Overlay] distance={p.z:F2}m");
            }
        }

        void OnSurfaceCreated()
        {
            decoder.SetSurface(_overlay.externalSurfaceObject);
            Debug.Log("[Overlay] external surface -> decoder");
        }
#else
        void Awake()
        {
            Debug.LogWarning("[Overlay] Meta XR SDK 미설치(USE_META_XR 미정의). OVROverlay 배선 생략.");
        }
#endif
    }
}
