using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class StartButton : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button _button;
    [SerializeField] private TextMeshPro _text;

    [Header("ROS Topics")]
    public string remoteSyncTopic = "remote_sync";
    public string remoteStartTopic = "remote_start";

    private ROSConnection ros;

    // 현재 상태 변수
    private bool remoteSync = false;
    private bool remoteStart = false;

    // 메시지 객체 (재사용)
    private BoolMsg syncMsg = new BoolMsg();
    private BoolMsg startMsg = new BoolMsg();
    private bool _isToggle = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();

        // 퍼블리셔 등록
        ros.RegisterPublisher<BoolMsg>(remoteSyncTopic, 10);
        ros.RegisterPublisher<BoolMsg>(remoteStartTopic, 10);

        // 버튼이 있다면 클릭 이벤트도 연결
        if (_button != null)
            _button.onClick.AddListener(OnPointerClick);

        // 초기 텍스트
        if (_text != null)
            _text.text = "Idle";
    }

    void Update()
    {
        // remoteSync / remoteStart 현재 상태를 주기적으로 발행
        syncMsg.data = remoteSync;
        startMsg.data = remoteStart;

        ros.Publish(remoteSyncTopic, syncMsg);
        ros.Publish(remoteStartTopic, startMsg);
    }

    // 손이 버튼 위에 들어올 때 (Poke/Ray hover)
    public void OnPointerEnter()
    {
        if (_text) _text.text = "Enter";
    }

    // 손이 버튼 영역을 벗어날 때
    public void OnPointerExit()
    {
        if (_text) _text.text = "Exit";
        remoteSync = false;
    }

    // 버튼을 누르는 순간
    public void OnPointerDown()
    {
        if (_text) _text.text = "Down";
        remoteSync = true;
    }

    // 버튼에서 손을 뗄 때
    public void OnPointerUp()
    {
        if (_text) _text.text = "Up";
        remoteSync = false;
    }

    // 클릭 완료 (Down→Up)
    public void OnPointerClick()
    {
        if (_text) _text.text = "Click";
    }

    // 토글용 UI(Toggle 컴포넌트에 연결)
    public void OnToggle(bool value)
    {
        _isToggle = !_isToggle;
        if (_text) _text.text = "Toggle " + _isToggle;
        remoteStart = _isToggle;
    }
}
