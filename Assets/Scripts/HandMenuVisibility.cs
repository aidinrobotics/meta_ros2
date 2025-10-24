using UnityEngine;
using UnityEngine.UI; // GraphicRaycaster (uGUI)
#if UNITY_XR_INTERACTION_TOOLKIT
using UnityEngine.XR.Interaction.Toolkit.UI; // TrackedDeviceGraphicRaycaster
#endif

[RequireComponent(typeof(CanvasGroup))]
public class HandMenuVisibility : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;                // 생략 시 Camera.main
    public CanvasGroup canvasGroup;            // 페이드용
    public GraphicRaycaster uGuiRaycaster;     // (uGUI 사용 시)
#if UNITY_XR_INTERACTION_TOOLKIT
    public TrackedDeviceGraphicRaycaster xrRaycaster; // (XR Canvas면)
#endif

    [Header("Gating (angle & distance)")]
    [Tooltip("보이기 시작 각도(도). 메뉴의 +Z(앞)과 카메라 방향이 이 값보다 작을 때 보임")]
    [Range(0f, 180f)] public float showAngleDeg = 50f;
    [Tooltip("숨기기 각도(도). 히스테리시스용(깜빡임 방지)")]
    [Range(0f, 180f)] public float hideAngleDeg = 60f;
    [Tooltip("이 거리보다 가까우면 숨김(너무 가까우면 보기 불편)")]
    public float minDistance = 0.08f;          // 8cm
    [Tooltip("이 거리보다 멀면 숨김")]
    public float maxDistance = 0.8f;           // 80cm

    [Header("Visuals")]
    [Tooltip("보이기/숨기기 전환 속도")]
    public float fadeSpeed = 12f;
    [Tooltip("메뉴가 항상 카메라를 바라보도록 회전(빌보드)할지")]
    public bool billboardToCamera = true;

    bool _visibleTarget;   // 목표 상태(히스테리시스 적용)
    float _cosShow, _cosHide;

    void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (!targetCamera && Camera.main) targetCamera = Camera.main;
#if UNITY_XR_INTERACTION_TOOLKIT
        xrRaycaster = GetComponent<TrackedDeviceGraphicRaycaster>();
#endif
        uGuiRaycaster = GetComponent<GraphicRaycaster>();
    }

    void Awake()
    {
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        if (!targetCamera) targetCamera = Camera.main;
        // 각도 임계값 미리 코사인으로
        _cosShow = Mathf.Cos(showAngleDeg  * Mathf.Deg2Rad);
        _cosHide = Mathf.Cos(hideAngleDeg  * Mathf.Deg2Rad);
    }

    void Update()
    {
        if (!targetCamera) return;

        // 메뉴(+Z)와 카메라 방향의 내적
        Vector3 toCam = (targetCamera.transform.position - transform.position).normalized;
        float dot = Vector3.Dot(-transform.forward, toCam); // 1: 정면, -1: 등짐

        float dist = Vector3.Distance(targetCamera.transform.position, transform.position);

        // 히스테리시스가 적용된 표시 상태 판정
        if (_visibleTarget)
        {
            // 보이는 상태였으면 hide 조건이 충족될 때까지 유지
            if (dot < _cosHide || dist < minDistance || dist > maxDistance)
                _visibleTarget = false;
        }
        else
        {
            // 숨김 상태였으면 show 조건이 충족될 때만 전환
            if (dot >= _cosShow && dist >= minDistance && dist <= maxDistance)
                _visibleTarget = true;
        }

        // 필요 시 빌보드 회전 (손바닥 로컬 회전을 고정하고 싶으면 false)
        if (billboardToCamera)
        {
            // 팔/손바닥의 업벡터와 최대한 유지하며 카메라를 바라보게
            var up = transform.up;
            var lookPos = targetCamera.transform.position;
            transform.rotation = Quaternion.LookRotation((lookPos - transform.position).normalized, up);
        }

        // 부드러운 페이드
        float targetAlpha = _visibleTarget ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
        canvasGroup.interactable = _visibleTarget;
        canvasGroup.blocksRaycasts = _visibleTarget;

        // XR/UGUI 레이캐스터도 함께 껐다 켜서, 안 보일 때는 터치/포인터 차단
        if (uGuiRaycaster) uGuiRaycaster.enabled = _visibleTarget;
#if UNITY_XR_INTERACTION_TOOLKIT
        if (xrRaycaster) xrRaycaster.enabled = _visibleTarget;
#endif
    }

    // 런타임에 각도 임계 변경 시 호출
    public void SetAngleThresholds(float showDeg, float hideDeg)
    {
        showAngleDeg = showDeg;
        hideAngleDeg = hideDeg;
        _cosShow = Mathf.Cos(showAngleDeg * Mathf.Deg2Rad);
        _cosHide = Mathf.Cos(hideAngleDeg * Mathf.Deg2Rad);
    }
}
