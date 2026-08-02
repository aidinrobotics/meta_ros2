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

        [Header("영상 패널 거리 (A/B 버튼)")]
        [Tooltip("A(가까이)/B(멀리) 홀드 시 초당 이동 거리(m)")]
        public float distanceSpeed = 1.5f;
        public float minDistance = 0.5f;
        public float maxDistance = 10f;

        [Header("영상 패널 위치 (오른손 엄지스틱)")]
        [Tooltip("오른손 엄지스틱으로 패널을 상하/좌우 이동. 초당 이동 거리(m)")]
        public float panSpeed = 1.0f;
        [Tooltip("데드존 미만 입력은 무시 (스틱 드리프트 방지)")]
        public float stickDeadzone = 0.15f;
        [Tooltip("중심에서 벗어날 수 있는 최대 좌우/상하 거리(m)")]
        public float maxPanX = 3f;
        public float maxPanY = 3f;

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

        // 엄지스틱 클릭 시 되돌릴 초기 패널 위치 (x/y 팬 + z 거리)
        Vector3 _homePos;
        bool _homeSet;

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

            // 왼손 X/Y: convergence −/+ (홀드 연속)
            // RawButton 으로 물리 버튼 지정 — Button.One/Two 는 A와 X/Y 가 공유돼 크로스토크 발생
            float dir = 0f;
            if (OVRInput.Get(OVRInput.RawButton.X)) dir -= 1f;
            if (OVRInput.Get(OVRInput.RawButton.Y)) dir += 1f;
            if (dir != 0f)
                convergence = Mathf.Clamp(convergence + dir * tuneSpeed * Time.deltaTime, -0.3f, 0.3f);

            // 오른손 A/B: 영상 패널 거리(Z) 가까이/멀리 (홀드 연속)
            float dz = 0f;
            if (OVRInput.Get(OVRInput.RawButton.A)) dz -= 1f; // A: 가까이
            if (OVRInput.Get(OVRInput.RawButton.B)) dz += 1f; // B: 멀리
            if (dz != 0f)
            {
                var p = transform.localPosition;
                p.z = Mathf.Clamp(p.z + dz * distanceSpeed * Time.deltaTime, minDistance, maxDistance);
                transform.localPosition = p;
                if (Time.frameCount % 15 == 0) Debug.Log($"[Overlay] distance={p.z:F2}m");
            }

            // 최초 진입 시 현재 위치를 홈(초기 위치)으로 기억
            if (!_homeSet) { _homePos = transform.localPosition; _homeSet = true; }

            // 오른손 엄지스틱 클릭: 패널 위치(x/y 팬 + z 거리)를 초기값으로 리셋
            if (OVRInput.GetDown(OVRInput.RawButton.RThumbstick))
            {
                transform.localPosition = _homePos;
                Debug.Log($"[Overlay] pos reset -> ({_homePos.x:F2},{_homePos.y:F2},{_homePos.z:F2})");
            }

            // 오른손 엄지스틱: 영상 패널 좌우(X)/상하(Y) 이동
            // RTouch 를 명시해야 확실히 오른손 스틱을 읽는다 (Secondary 만 쓰면 활성 컨트롤러 매핑에 따라 0 이 나올 수 있음)
            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
            if (Time.frameCount % 30 == 0) Debug.Log($"[Overlay] Rstick=({stick.x:F2},{stick.y:F2})");
            if (stick.magnitude > stickDeadzone)
            {
                var p = transform.localPosition;
                p.x = Mathf.Clamp(p.x + stick.x * panSpeed * Time.deltaTime, -maxPanX, maxPanX);
                p.y = Mathf.Clamp(p.y + stick.y * panSpeed * Time.deltaTime, -maxPanY, maxPanY);
                transform.localPosition = p;
                if (Time.frameCount % 15 == 0) Debug.Log($"[Overlay] pan=({p.x:F2},{p.y:F2})m");
            }
        }

        void OnSurfaceCreated()
        {
            decoder.SetSurface(_overlay.externalSurfaceObject);
            Debug.Log("[Overlay] external surface -> decoder");
        }

        // 영상 오버레이 표시/숨김 (외부 서피스 유지, 컴포지터 제출만 on/off).
        // IP 편집 등 앱 화면(3D 텍스트)을 앞에 보여야 할 때 사용.
        public void SetVideoHidden(bool h)
        {
            if (_overlay != null)
            {
                _overlay.hidden = h;
                Debug.Log($"[Overlay] hidden={h}");
            }
            else Debug.LogWarning("[Overlay] SetVideoHidden: _overlay null");

            // 상태바도 컴포지터 레이어라 앱 화면보다 위에 뜬다 -> 같이 숨긴다
            if (_stateDisplay == null) _stateDisplay = FindObjectOfType<RobotStateDisplay>();
            if (_stateDisplay != null) _stateDisplay.SetHidden(h);
        }

        RobotStateDisplay _stateDisplay;
#else
        void Awake()
        {
            Debug.LogWarning("[Overlay] Meta XR SDK 미설치(USE_META_XR 미정의). OVROverlay 배선 생략.");
        }

        public void SetVideoHidden(bool h) { /* no-op (USE_META_XR 미정의) */ }
#endif
    }
}
