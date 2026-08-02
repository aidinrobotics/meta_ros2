using UnityEngine;
#if USE_ROS_TCP
using Unity.Robotics.ROSTCPConnector;
#endif

namespace VrTeleop
{
    /// <summary>
    /// ROS-TCP 발행 대상 IP 를 앱 안에서 변경 (재빌드 불필요).
    ///
    /// Quest 에서는 Unity TouchScreenKeyboard 가 실제로 안 뜨므로(미지원),
    /// 포인터/키보드 배선 없이 "컨트롤러로 옥텟을 편집"하는 방식을 쓴다.
    /// 화면에는 런타임 생성한 3D TextMesh 로 IP 를 띄운다(씬 배선 불필요).
    ///
    /// 조작 (모두 왼손 컨트롤러 — 오른손 영상 조작과 충돌 없음):
    ///   Menu(Start) : 편집기 열기 / (열려 있으면) 취소하고 닫기
    ///   왼손 스틱 ←/→ : 편집할 자리(옥텟) 선택
    ///   왼손 스틱 ↑/↓ : 선택한 옥텟 값 +/- (0~255, 홀드 시 반복)
    ///   왼손 트리거   : 확인 → 즉시 재연결 + 저장
    ///
    /// 입력값은 PlayerPrefs 에 저장 -> 앱 재실행 시에도 유지.
    /// 영상 수신(RtpH264Receiver)은 UDP Any 바인딩이라 IP 무관 — 여기서 바꾸는 건 업링크뿐.
    /// </summary>
    public class IpConfigController : MonoBehaviour
    {
        [Tooltip("저장된 값이 없을 때 사용할 기본 IP")]
        public string defaultIp = "192.168.2.31";

        [Header("표시")]
        [Tooltip("편집 패널이 눈앞에 뜨는 거리(m)")]
        public float displayDistance = 1.2f;
        [Tooltip("텍스트 크기 (characterSize). 더 줄이려면 0.006~0.01 권장")]
        public float textSize = 0.01f;

        [Header("영상 오버레이")]
        [Tooltip("편집 중 영상 오버레이를 숨겨 텍스트가 보이게 함(컴포지터 레이어가 앱 화면 위에 합성되므로 필요). " +
                 "비우면 씬에서 자동 검색")]
        public VideoOverlayController videoOverlay;

        [Header("입력 감도")]
        [Tooltip("스틱 인식 임계값")]
        public float stickThreshold = 0.6f;
        [Tooltip("값 변경 반복 시작까지 지연(초)")]
        public float repeatDelay = 0.35f;
        [Tooltip("반복 간격(초)")]
        public float repeatInterval = 0.12f;

        const string PrefKey = "ros_ip";

        public string CurrentIp { get; private set; }

#if USE_ROS_TCP
        ROSConnection _ros;
#endif

        readonly int[] _oct = new int[4];
        int _sel;
        bool _editing;

        TextMesh _text;
        Transform _panel;

        // 스틱 반복/디바운스 상태
        float _selCooldown;   // 자리 선택 쿨다운
        float _valTimer;      // 값 변경 반복 타이머
        int _valDir;          // 현재 홀드 중인 값 변경 방향(0=없음)

        void Start()
        {
#if USE_ROS_TCP
            _ros = ROSConnection.GetOrCreateInstance();
            // 연결은 이 스크립트가 소유 -> ROSConnection 이 스스로 연결(이중 스레드)하지 않게 끔.
            _ros.ConnectOnStart = false;
            CurrentIp = PlayerPrefs.GetString(PrefKey,
                string.IsNullOrEmpty(_ros.RosIPAddress) ? defaultIp : _ros.RosIPAddress);
            ApplyIp(CurrentIp);
#else
            CurrentIp = PlayerPrefs.GetString(PrefKey, defaultIp);
#endif
        }

        void Update()
        {
#if USE_META_XR
            // Menu 버튼: 열기 / 닫기(취소)
            if (OVRInput.GetDown(OVRInput.RawButton.Start))
            {
                if (_editing) CloseEditor();
                else OpenEditor();
            }

            if (!_editing) return;

            HandleEdit();

            // 왼손 트리거: 확인
            if (OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger))
                Confirm();
#endif
        }

#if USE_META_XR
        void HandleEdit()
        {
            Vector2 s = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);

            // 좌/우: 자리 선택 (쿨다운으로 한 칸씩)
            _selCooldown -= Time.deltaTime;
            if (Mathf.Abs(s.x) > stickThreshold && _selCooldown <= 0f)
            {
                _sel = Mathf.Clamp(_sel + (s.x > 0 ? 1 : -1), 0, 3);
                _selCooldown = 0.25f;
                Redraw();
            }
            else if (Mathf.Abs(s.x) <= stickThreshold * 0.5f)
            {
                _selCooldown = 0f; // 중립 복귀 시 즉시 다음 입력 허용
            }

            // 상/하: 값 변경 (첫 입력 즉시 + 홀드 반복)
            int dir = (s.y > stickThreshold) ? 1 : (s.y < -stickThreshold ? -1 : 0);
            if (dir != _valDir)
            {
                _valDir = dir;
                _valTimer = repeatDelay;
                if (dir != 0) StepValue(dir); // 방향 진입 즉시 1회
            }
            else if (dir != 0)
            {
                _valTimer -= Time.deltaTime;
                if (_valTimer <= 0f) { StepValue(dir); _valTimer = repeatInterval; }
            }
        }

        void StepValue(int dir)
        {
            _oct[_sel] = Mathf.Clamp(_oct[_sel] + dir, 0, 255);
            Redraw();
        }
#endif

        void OpenEditor()
        {
            ParseOctets(CurrentIp);
            _sel = 0;
            _valDir = 0;
            _selCooldown = 0f;
            EnsurePanel();
            _panel.gameObject.SetActive(true);
            ShowVideo(false); // 영상 오버레이가 텍스트를 가리므로 편집 중 숨김
            _editing = true;
            Redraw();
            Debug.Log("[IpConfig] editor opened (current=" + CurrentIp + ")");
        }

        void CloseEditor()
        {
            _editing = false;
            if (_panel != null) _panel.gameObject.SetActive(false);
            ShowVideo(true);
            Debug.Log("[IpConfig] editor closed (no change)");
        }

        void Confirm()
        {
            string ip = _oct[0] + "." + _oct[1] + "." + _oct[2] + "." + _oct[3];
            PlayerPrefs.SetString(PrefKey, ip);
            PlayerPrefs.Save();
            ApplyIp(ip);
            _editing = false;
            if (_panel != null) _panel.gameObject.SetActive(false);
            ShowVideo(true);
        }

        // 영상 오버레이 표시/숨김 (외부 서피스는 유지, 합성만 on/off)
        void ShowVideo(bool show)
        {
            if (videoOverlay == null) videoOverlay = FindObjectOfType<VideoOverlayController>();
            if (videoOverlay != null) videoOverlay.SetVideoHidden(!show);
            else Debug.LogWarning("[IpConfig] VideoOverlayController 못 찾음 — 영상 숨김 불가");
        }

        void ApplyIp(string ip)
        {
            CurrentIp = ip;
#if USE_ROS_TCP
            _ros.Disconnect();                 // 기존 연결 스레드 종료
            _ros.Connect(ip, _ros.RosPort);    // 새 IP 로 재연결 (포트 유지)
            Debug.Log("[IpConfig] ROS IP -> " + ip + ":" + _ros.RosPort);
#else
            Debug.LogWarning("[IpConfig] USE_ROS_TCP 미정의 — IP 저장만 됨: " + ip);
#endif
        }

        void ParseOctets(string ip)
        {
            var parts = (ip ?? "").Split('.');
            for (int i = 0; i < 4; i++)
                _oct[i] = (i < parts.Length && int.TryParse(parts[i], out int v)) ? Mathf.Clamp(v, 0, 255) : 0;
        }

        // 눈앞에 head-lock 되는 TextMesh 패널을 런타임 생성 (씬 배선 불필요)
        void EnsurePanel()
        {
            if (_panel != null) return;

            var cam = Camera.main != null ? Camera.main.transform : transform;

            var go = new GameObject("IpEditorPanel");
            _panel = go.transform;
            _panel.SetParent(cam, false);
            _panel.localPosition = new Vector3(0f, 0f, displayDistance);
            _panel.localRotation = Quaternion.identity;

            _text = go.AddComponent<TextMesh>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.GetComponent<MeshRenderer>().sharedMaterial = _text.font.material;
            _text.fontSize = 90;
            _text.characterSize = textSize;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = Color.white;
        }

        void Redraw()
        {
            if (_text == null) return;
            // LegacyRuntime.ttf 는 한글 글리프가 없어 ASCII 로만 표기(깨짐 방지)
            var sb = new System.Text.StringBuilder();
            sb.Append("ROS IP CONFIG\n\n");
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) sb.Append(" . ");
                sb.Append(i == _sel ? "[" + _oct[i] + "]" : _oct[i].ToString());
            }
            sb.Append("\n\nStick L/R: digit   U/D: value\nTrigger: OK    Menu: Cancel");
            _text.text = sb.ToString();
        }
    }
}
