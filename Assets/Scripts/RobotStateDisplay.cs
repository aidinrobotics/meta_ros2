using UnityEngine;
#if USE_ROS_TCP
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
#endif

namespace VrTeleop
{
    /// <summary>
    /// 로봇 상태(<c>std_msgs/String</c>, 기본 <c>/aidin_rby1_vive_teleop/state</c>)를
    /// 영상 캔버스 하단에 상태바로 띄운다. (IDLE / SESSION / TELEOP / HOMING / SHUTDOWN ...)
    /// 상태가 바뀔 때만 발행되는 토픽이라, 마지막으로 받은 상태를 계속 유지한다.
    ///
    /// 왜 그냥 TextMesh 를 안 쓰나:
    ///   영상은 OVROverlay(컴포지터 레이어)라 앱이 그린 3D 텍스트보다 항상 위에 합성된다.
    ///   (IpConfigController 가 편집 중 영상을 숨기는 이유가 이것)
    ///   그래서 상태바도 **별도의 OVROverlay 레이어**로 올리고, compositionDepth 를 영상보다
    ///   작게(= 더 앞) 줘서 영상 위에 보이게 한다.
    ///
    /// 파이프라인:
    ///   TextMesh(씬에서 멀리 떨어진 오프스크린 리그) → 전용 직교 카메라 → RenderTexture
    ///   → OVROverlay(Quad, 영상 패널의 자식) → 컴포지터
    ///   텍스트가 바뀔 때만 카메라를 Render() 하므로 상시 비용은 레이어 합성뿐이다.
    ///
    /// 배선: 영상 패널(VideoLayer, OVROverlay 가 붙은 오브젝트)에 이 컴포넌트를 추가하면 끝.
    ///       (다른 오브젝트에 두려면 videoPanel 에 VideoLayer Transform 을 지정)
    /// </summary>
    public class RobotStateDisplay : MonoBehaviour
    {
        [Header("ROS")]
        [Tooltip("구독할 상태 토픽 (std_msgs/String)")]
        public string stateTopic = "/aidin_rby1_vive_teleop/state";
        [Tooltip("상태 앞에 붙는 라벨. 비우면 상태 문자열만 표시")]
        public string label = "STATE";

        [Header("배치 (영상 패널 기준)")]
        [Tooltip("영상 패널(OVROverlay) Transform. 비우면 이 오브젝트")]
        public Transform videoPanel;
        [Tooltip("상태바 가로 = 패널 가로 x 이 비율")]
        [Range(0.05f, 1f)] public float widthRatio = 0.35f;
        [Tooltip("상태바 세로 = 패널 세로 x 이 비율")]
        [Range(0.02f, 0.5f)] public float heightRatio = 0.07f;
        [Tooltip("패널 아래 가장자리에서 위로 띄우는 양(패널 세로 비율). 음수면 패널 밖(아래)으로 나감")]
        public float bottomMargin = 0.015f;

        [Header("모양")]
        [Tooltip("상태바 배경색. 알파는 1 유지 권장 — 1 미만이면 이전 프레임이 완전히 덮이지 않아 " +
                 "글씨 잔상이 생길 수 있다(아래 BuildBackgroundQuad 참고)")]
        public Color barColor = Color.black;
        [Tooltip("기본 글자색 (colorByState 로 매칭되지 않는 상태)")]
        public Color textColor = Color.white;
        [Tooltip("상태별 글자색 사용 (SESSION·TELEOP=초록, HOMING=노랑, SHUTDOWN=빨강, IDLE=회색)")]
        public bool colorByState = true;
        [Tooltip("상태바 텍스처 가로 해상도(px). 세로는 상태바 비율에서 자동 계산")]
        public int texWidth = 2048;

        // 상태바 렌더 리그를 씬에서 멀리 떨어뜨려 메인 카메라(far clip ~1000m)에 안 잡히게 한다.
        // -> 전용 레이어를 잡아 컬링 마스크를 건드릴 필요가 없다.
        static readonly Vector3 RigOrigin = new Vector3(0f, -5000f, 0f);

        const string NoData = "NO DATA";

        // 상태 토픽은 상태가 "바뀔 때만" 발행된다 -> 수신 타임아웃으로 끊김을 판정하면 정상 동작 중에도
        // 표시가 사라진다. 마지막으로 받은 상태를 계속 띄우고, 한 번도 못 받았을 때만 NO DATA.
        string _state = "";
        string _shown;               // 현재 텍스처에 그려져 있는 문자열
        bool _dirty;
        int _pendingRenders;         // 텍스트 변경 후 다시 그릴 프레임 수(메시 갱신 1프레임 지연 대응)

        RenderTexture _rt;
        Camera _cam;
        TextMesh _text;
        MeshRenderer _textRenderer;
        Material _bgMat;
        Transform _rig;
        Transform _bar;
        float _viewAspect = 8f;      // 렌더 타깃 가로/세로 (= 카메라 뷰 가로, 세로는 1)

#if USE_META_XR
        OVROverlay _overlay;
#endif
#if USE_ROS_TCP
        ROSConnection _ros;
#endif

        void Start()
        {
            if (videoPanel == null) videoPanel = transform;

            BuildTextRig();
            BuildBar();

            _state = "";
            SetText(NoData, Color.gray);

#if USE_ROS_TCP
            _ros = ROSConnection.GetOrCreateInstance();
            _ros.Subscribe<StringMsg>(stateTopic, OnState);
            Debug.Log("[State] subscribe " + stateTopic + " (std_msgs/String)");
#else
            SetText("NO ROS", Color.gray);
            Debug.LogWarning("[State] USE_ROS_TCP 미정의 — 상태 구독 비활성");
#endif
        }

#if USE_ROS_TCP
        // ROSConnection 은 수신 메시지를 Update 에서 처리 -> 이 콜백은 메인 스레드다.
        void OnState(StringMsg msg)
        {
            _state = msg.data ?? "";
            Refresh();
        }
#endif

        void LateUpdate()
        {
            PlaceBar();

            if (_dirty) { _dirty = false; _pendingRenders = 2; }
            if (_pendingRenders > 0)
            {
                FitText();
                Redraw();
                _pendingRenders--;
            }
        }

        void Refresh()
        {
            if (string.IsNullOrEmpty(_state))   // 첫 수신 전
            {
                SetText(NoData, Color.gray);
                return;
            }

            string s = _state.Trim();
            string body = string.IsNullOrEmpty(label) ? s : label + ": " + s;
            SetText(body, colorByState ? StateColor(s) : textColor);
        }

        Color StateColor(string s)
        {
            switch (s.ToUpperInvariant())
            {
                case "SESSION":
                case "TELEOP": return new Color(0.40f, 1f, 0.45f);
                case "HOMING": return new Color(1f, 0.82f, 0.25f);
                case "SHUTDOWN": return new Color(1f, 0.35f, 0.30f);
                case "IDLE": return new Color(0.80f, 0.80f, 0.80f);
                default: return textColor;
            }
        }

        void SetText(string s, Color c)
        {
            if (_text == null) return;
            if (_shown == s && _text.color == c) return;
            _shown = s;
            _text.text = s;
            _text.color = c;
            _dirty = true;
        }

        /// <summary>상태바 숨김/표시 (IP 편집 등 앱 화면을 앞에 보여야 할 때).</summary>
        public void SetHidden(bool h)
        {
#if USE_META_XR
            if (_overlay != null) _overlay.hidden = h;
#else
            if (_bar != null) _bar.gameObject.SetActive(!h);
#endif
        }

        // ── 오프스크린 텍스트 리그 (TextMesh -> 직교 카메라 -> RenderTexture) ──────────────

        void BuildTextRig()
        {
            int texHeight = Mathf.Clamp(Mathf.RoundToInt(texWidth / Mathf.Max(0.01f, BarAspect())), 32, 1024);

            // MSAA 금지: OVROverlay 는 모바일에서 RenderTexture -> 스왑체인을 CommandBuffer.CopyTexture 로
            // 그대로 복사한다(PopulateLayer 의 bypassBlit). 멀티샘플 RT 는 이 복사가 성립하지 않아
            // 예전 프레임이 그대로 남는다(글씨 잔상). 계단현상은 폰트를 크게 구워서 보완한다.
            _rt = new RenderTexture(Mathf.Max(64, texWidth), texHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "StateBarRT",
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _rt.Create();
            _viewAspect = (float)_rt.width / _rt.height;

            // 첫 Render() 전에 오버레이가 복사해 가도 쓰레기 픽셀이 안 보이게 초기화
            var prevRt = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(true, true, barColor);
            RenderTexture.active = prevRt;

            var rigGo = new GameObject("RobotStateTextRig");
            _rig = rigGo.transform;
            _rig.position = RigOrigin;

            var textGo = new GameObject("StateText");
            textGo.transform.SetParent(_rig, false);
            _text = textGo.AddComponent<TextMesh>();
            // LegacyRuntime.ttf 는 한글 글리프가 없다 -> 표시 문자열은 ASCII 로 유지
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _textRenderer = textGo.GetComponent<MeshRenderer>();
            _textRenderer.sharedMaterial = _text.font.material;
            _textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _textRenderer.receiveShadows = false;
            _text.fontSize = 160;              // 아틀라스를 크게 구운 뒤 축소 -> MSAA 없이도 선명
            _text.characterSize = 0.1f;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = textColor;

            BuildBackgroundQuad();

            var camGo = new GameObject("StateBarCamera");
            camGo.transform.SetParent(_rig, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -1f);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 0.5f;      // 화면 세로 = 1 (월드 단위)
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 10f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = barColor;
            _cam.targetTexture = _rt;
            _cam.useOcclusionCulling = false;
            _cam.allowHDR = false;
            _cam.enabled = false;              // 텍스트가 바뀔 때만 수동 Render()
        }

        // 배경을 "지우기(clear)" 가 아니라 "그리기" 로 깔아, 렌더 타깃에 남아 있던 이전 프레임을
        // 무조건 덮어쓴다. URP 는 수동 Camera.Render() 시 중간 타깃을 재사용할 수 있어
        // clear 만 믿으면 잔상이 남는다.
        //   - 폰트 머티리얼과 같은 셰이더(GUI/Text Shader: Cull Off / ZTest Always / 알파블렌드)를 쓴다.
        //     URP 빌드에 확실히 포함돼 있는 셰이더는 이것뿐(Shader.Find 로 찾은 셰이더는 스트립될 수 있음).
        //   - renderQueue 2900 (글자 3000 보다 앞) + 카메라에서 더 먼 z — 두 기준 모두 "배경 먼저".
        //   - 알파 1 일 때만 완전히 덮인다 -> barColor 는 불투명 권장(반투명은 clear 동작에 의존).
        void BuildBackgroundQuad()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "StateBarBackground";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_rig, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.05f);   // 글자보다 카메라에서 멀리
            quad.transform.localScale = new Vector3(_viewAspect * 1.2f, 1.2f, 1f); // 뷰보다 넉넉히

            _bgMat = new Material(_text.font.material.shader);
            _bgMat.name = "StateBarBackground";
            _bgMat.mainTexture = Texture2D.whiteTexture;
            _bgMat.color = barColor;
            _bgMat.renderQueue = 2900;

            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = _bgMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // 텍스처를 지우고 다시 그린다.
        // GL.Clear + 카메라 clear + 배경 Quad — 삼중으로 이전 프레임을 지운다.
        // (파이프라인/플랫폼에 따라 셋 중 하나만 동작해도 잔상이 남지 않도록)
        void Redraw()
        {
            if (_cam == null || _rt == null) return;

            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(true, true, barColor);
            RenderTexture.active = prev;

            _cam.Render();
        }

        // 텍스트가 상태바 안에 꽉 차되 넘치지 않도록 스케일을 맞춘다(문자열 길이 무관).
        void FitText()
        {
            if (_text == null || _textRenderer == null) return;

            _text.transform.localScale = Vector3.one;
            Vector3 size = _textRenderer.bounds.size;
            if (size.x < 1e-4f || size.y < 1e-4f) return;

            float maxW = _viewAspect * 0.94f;   // 카메라 뷰 가로 = aspect, 세로 = 1
            float maxH = 0.62f;
            float k = Mathf.Min(maxW / size.x, maxH / size.y);
            _text.transform.localScale = new Vector3(k, k, k);
        }

        // ── 상태바 레이어 ────────────────────────────────────────────────────────────

        float BarAspect()
        {
            Vector3 p = (videoPanel != null ? videoPanel : transform).lossyScale;
            float w = Mathf.Abs(p.x) * widthRatio;
            float h = Mathf.Abs(p.y) * heightRatio;
            return (h > 1e-4f) ? w / h : 8f;
        }

        void BuildBar()
        {
            var go = new GameObject("StateBar");
            go.SetActive(false);                       // 텍스처를 넣은 뒤 켜야 스왑체인이 제대로 잡힌다
            _bar = go.transform;
            _bar.SetParent(videoPanel, false);
            PlaceBar();

#if USE_META_XR
            _overlay = go.AddComponent<OVROverlay>();
            _overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
            _overlay.currentOverlayType = OVROverlay.OverlayType.Overlay;
            _overlay.noDepthBufferTesting = true;
            _overlay.isDynamic = true;                 // RenderTexture 내용이 바뀌므로 매 프레임 복사
            _overlay.textures[0] = _rt;

            // 영상 레이어보다 작은 compositionDepth = 더 앞에 합성 (영상 위에 상태바)
            var videoOverlay = videoPanel != null ? videoPanel.GetComponent<OVROverlay>() : null;
            _overlay.compositionDepth = (videoOverlay != null ? videoOverlay.compositionDepth : 0) - 1;
#else
            // 에디터/SDK 미설치 확인용 폴백 — 평범한 Quad 에 같은 RenderTexture 를 띄운다
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "StateBarQuad";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_bar, false);
            var mat = new Material(Shader.Find("Unlit/Transparent"));
            mat.mainTexture = _rt;
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
#endif
            go.SetActive(true);
        }

        // 패널을 팬/줌(오른손 스틱, A/B)해도 상태바가 따라가도록 매 프레임 위치를 맞춘다.
        // (패널의 자식이라 위치·회전은 자동, 여기서는 패널 로컬 기준 비율만 반영)
        void PlaceBar()
        {
            if (_bar == null) return;
            _bar.localScale = new Vector3(widthRatio, heightRatio, 1f);
            _bar.localPosition = new Vector3(0f, -0.5f + heightRatio * 0.5f + bottomMargin, -0.004f);
            _bar.localRotation = Quaternion.identity;
        }

        void OnDestroy()
        {
#if USE_ROS_TCP
            // 종료 중에는 GetOrCreateInstance() 로 새 오브젝트가 생기지 않게 캐시된 참조만 쓴다
            if (_ros != null) _ros.Unsubscribe(stateTopic);
#endif
            if (_cam != null) _cam.targetTexture = null;
            if (_rig != null) Destroy(_rig.gameObject);
            if (_bgMat != null) Destroy(_bgMat);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
