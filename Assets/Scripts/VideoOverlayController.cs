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

        [Tooltip("A(-)/B(+) 버튼 홀드로 convergence 조절, X 버튼으로 좌우 스왑")]
        public bool enableButtonTuning = true;
        public float tuneSpeed = 0.2f;

#if USE_META_XR
        OVROverlay _overlay;

        void Awake()
        {
            _overlay = GetComponent<OVROverlay>();
            if (_overlay == null) _overlay = gameObject.AddComponent<OVROverlay>();

            _overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
            _overlay.isExternalSurface = true;
            _overlay.externalSurfaceWidth = decoder.width;   // SBS 폭
            _overlay.externalSurfaceHeight = decoder.height;

            _overlay.externalSurfaceObjectCreated += OnSurfaceCreated;

            _overlay.overrideTextureRectMatrix = true;
            ApplyRects();
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

            // A(One): convergence 감소 / B(Two): 증가 — 홀드하면 연속 조절
            float dir = 0f;
            if (OVRInput.Get(OVRInput.Button.One)) dir -= 1f;
            if (OVRInput.Get(OVRInput.Button.Two)) dir += 1f;
            if (dir != 0f)
                convergence = Mathf.Clamp(convergence + dir * tuneSpeed * Time.deltaTime, -0.3f, 0.3f);

            // X(Three): 좌/우 눈 스왑 토글
            if (OVRInput.GetDown(OVRInput.Button.Three))
                swapEyes = !swapEyes;
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
