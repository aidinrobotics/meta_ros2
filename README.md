# meta_ros2 — RB-Y1 VR 원격조작 (Meta Quest 3)

RB-Y1 로봇의 **양방향 저지연 VR 원격조작**을 위한 Meta Quest 3 Unity 앱. 한 앱 안에서
두 서브시스템이 독립적으로 동작한다.

| 방향 | 데이터 | 전송 | 이유 |
|------|--------|------|------|
| **다운링크** (스트리밍 PC → Quest) | 스테레오 카메라(SBS) | **RTP / H.264 over UDP** | 대역폭 큼, 지연 민감, 유실 허용 |
| **업링크** (Quest → 제어 PC) | 헤드셋 + 손 포즈 | **ROS-TCP (TCP)** | 작음, 고빈도(60Hz), 무손실·순서 보장 |

> 두 스트림은 **의도적으로 채널을 분리**한다 — 영상(UDP)과 포즈(TCP)를 한 채널에 묶지 않는다.

```
[스트리밍 PC]                                    [Meta Quest 3 앱]
 ZED SBS ─ GStreamer ─ x264 ─ RTP/H.264 ──UDP:5600──▶ RtpH264Receiver
                                                     └▶ VideoDecoder (MediaCodec)
                                                        └▶ OVROverlay (SBS → 좌/우 눈)
 CenterEye + 손 트래킹 ─ ROSConnection ──TCP:10000──▶ ROS-TCP-Endpoint (제어 PC)
                                                       /vr/hmd_pose, /vr/{left,right}_hand_pose
```

---

## 구성 요소

런타임 스크립트는 모두 [`Assets/Scripts/`](Assets/Scripts) 에 있다:

| 스크립트 | 역할 |
|----------|------|
| `RtpH264Receiver.cs` | UDP 소켓(5600), RTP 디페이로타이징(단일 NAL / STAP-A / **FU-A** 재조립, RFC 6184), **SPS에서 해상도 자동 파싱**, RTP 시퀀스 갭 기반 **패킷 손실 처리**(wait-for-IDR / feed-through 토글), 초당 진단 통계 |
| `VideoDecoder.cs` | `MediaCodec("video/avc")` 를 AndroidJNI 로 직접 구동(`.aar` 불필요), `low-latency=1`, OVROverlay 외부 서피스로 렌더 |
| `VideoOverlayController.cs` | OVROverlay 외부 서피스를 **파싱된 해상도로 생성**, SBS 를 좌 `(0,0,0.5,1)` / 우 `(0.5,0,0.5,1)` 눈으로 분리, 컨트롤러 버튼·**오른손 스틱**으로 컨버전스·패널 거리·**패널 위치(팬)** 실시간 튜닝 |
| `VrTeleopPublisher.cs` | 헤드셋+손 포즈를 ROS-TCP 로 60Hz 발행, Unity→ROS `FLU` 좌표 변환, **인식된 3D 손 메시 숨김 옵션**(`hideHandMesh`, 관절 발행은 유지) |
| `IpConfigController.cs` | **앱 내에서 ROS 발행 IP 변경**(재빌드 불필요). 왼손 Menu 버튼 → 컨트롤러로 옥텟 편집, `PlayerPrefs` 저장(재실행 유지) |
| `RobotStateDisplay.cs` | 로봇 상태(`/aidin_rby1_vive_teleop/state`, `std_msgs/String`)를 **영상 캔버스 하단 상태바**로 표시. 영상 위에 보이도록 별도 OVROverlay 레이어(`compositionDepth` 영상보다 −1)로 합성, 상태별 색상. 토픽이 상태 변화 시에만 발행되므로 **마지막 수신 상태를 계속 유지**(첫 수신 전에만 `NO DATA`) |

씬 배선 상세는 [`Assets/Scripts/README_VrTeleop.md`](Assets/Scripts/README_VrTeleop.md) 참고.

---

## 요구 사항

- **Meta Quest 3** (개발자 모드 활성화)
- **Unity 6000.3.x** + Android Build Support (IL2CPP + ARM64, min SDK 32)
- **Meta XR Core SDK** (`com.meta.xr.sdk.core`) — OVRCameraRig, OVROverlay, OVRSkeleton
- **ROS-TCP-Connector** (Unity) + **ROS-TCP-Endpoint** (제어 PC, ROS 2 Humble)
- 스트리밍 PC 에 **GStreamer 1.x** (`x264enc`, `rtph264pay`, `h264parse`, `udpsink`)

---

## Unity 설정

### 1. 패키지
- Meta XR Core SDK (Unity Registry / Meta All-in-One SDK)
- ROS-TCP-Connector:
  `https://github.com/Unity-Technologies/ROS-TCP-Connector.git?path=/com.unity.robotics.ros-tcp-connector`

> `geometry_msgs` / `std_msgs` 는 ROS-TCP-Connector 에 **기본 내장**돼 있다 — 다시 생성하지 말 것
> (타입 중복 → CS0029). *Generate ROS Messages* 는 커스텀 메시지에만 사용한다.

### 2. Scripting Define Symbols
`Player Settings → Android → Scripting Define Symbols`:
```
USE_META_XR;USE_ROS_TCP
```
(SDK 의존 코드는 이 심볼로 가드돼 있어, 패키지 설치 전에도 프로젝트가 컴파일된다.)

### 3. 씬 (`Assets/Scenes/SampleScene.unity`)
- **OVRCameraRig** (OVRManager: Hand Tracking = *Controllers And Hands*, Quest 3)
- **VideoLayer** (`CenterEyeAnchor` 자식): `OVROverlay` + `RtpH264Receiver` + `VideoDecoder` + `VideoOverlayController` + `RobotStateDisplay`; Quad 스케일 **16:9**(1.7778:1), 거리 `localPosition.z`(기본 3m). 해상도는 SPS에서 자동 감지되어 서피스 크기가 자동 정렬됨
  - `RobotStateDisplay`: 상태바를 영상 패널의 자식으로 런타임 생성(추가 배선 불필요) — `State Topic`(기본 `/aidin_rby1_vive_teleop/state`), `Width/Height Ratio`(패널 대비 크기, 기본 0.35 / 0.07), `Bottom Margin`(하단 여백, 음수면 패널 밖 아래)
- **RosBridge**: `ROSConnection` + `VrTeleopPublisher` + `IpConfigController`
  - `VrTeleopPublisher`: head = CenterEyeAnchor, hands = OVRSkeleton, `Hide Hand Mesh`(기본 ON)
  - `IpConfigController`: `Video Overlay` 는 비워두면 자동 검색(씬에 OVROverlay 여러 개면 VideoLayer 직접 지정), `Text Size`(기본 0.01)·`Display Distance`(기본 1.2m) 로 편집 패널 크기 조정

### 4. ROS 연결
`Robotics → ROS Settings`: ROS IP = 제어 PC IP, 포트 `10000`, 프로토콜 **ROS2**.
IP 는 **앱 안에서도 변경 가능**(왼손 Menu 버튼 → IP 편집기, 아래 [인헤드셋 컨트롤](#인헤드셋-컨트롤) 참고). 인앱 변경값이 저장돼 있으면 씬 설정보다 우선한다.

### 5. 패스스루 (실공간 배경)
- OVRManager: Passthrough Support = *Supported*, **Enable Passthrough**
- **OVRPassthroughLayer** 추가 (Placement = *Underlay*)
- CenterEye Camera → `Environment → Background Type = Solid Color`, 색상 알파 **0**

### 6. 빌드
`Internet Access = Require` (UDP 소켓에 필요) 후 **Build And Run**.

---

## 영상 다운링크 — 스트리밍 PC (GStreamer)

앱은 포트 **5600** 에서 **raw RTP / H.264 over UDP** (payload type 96) 를 수신한다. 스트리밍 PC 는
파이프라인 끝이 `rtph264pay ! udpsink` 인 GStreamer 로 발행한다.

### 발행 (스트리밍 PC → Quest)

**side-by-side(SBS) 스테레오** H.264 스트림을 헤드셋으로 전송한다. `<QUEST_IP>` 를 Quest 의 LAN IP 로,
소스를 실제 카메라로 바꾼다 (아래는 빠른 테스트용 `videotestsrc`):

```bash
gst-launch-1.0 -v \
  videotestsrc is-live=true ! video/x-raw,width=2560,height=720,framerate=30/1 ! \
  videoconvert ! video/x-raw,format=I420 ! \
  x264enc tune=zerolatency speed-preset=ultrafast bitrate=12000 key-int-max=6 bframes=0 ! \
  video/x-h264,profile=baseline ! h264parse config-interval=1 ! \
  rtph264pay pt=96 config-interval=1 mtu=1200 ! \
  udpsink host=<QUEST_IP> port=5600 sync=false
```

> Jetson(ZED SBS) 발행 노드는 [`zed_sbs_rtsp_node.py`](zed_sbs_rtsp_node.py) 참고.
> `--bitrate --gop --preset --intra-refresh --sliced-threads` 인자로 튜닝한다.
> 예: `--bitrate 12 --gop 6 --preset ultrafast` (Orin Nano SW x264 기준).

수신부와 반드시 맞춰야 할 부분:
- **`rtph264pay pt=96 config-interval=1`** — payload 96, SPS/PPS 를 주기적으로 재전송하여 늦게 접속한
  디코더도 빠르게 초기화된다.
- **`mtu=1200`** — RTP 패킷을 UDP 1개에 맞춰 IP 단편화 제거 → 패킷 손실 증폭 방지.
- **`profile=baseline`, `bframes=0`, `tune=zerolatency`** — 프레임 재정렬 지연 제거.
- **`key-int-max`(GOP)** — 짧을수록(예 6) 손실 후 복구가 빠름. `intra-refresh` 는 IDR을 없애
  MediaCodec이 출력 못 하는 경우가 있어 **비권장**.
- 프레임은 **SBS**: 좌측 절반 → 왼눈, 우측 절반 → 오른눈 (`VideoOverlayController` 가 처리).
  해상도는 **SPS에서 자동 감지**되므로 2560×720·3840×1080 등 무엇이든 앱이 알아서 맞춘다
  (`VideoDecoder.width/height` 는 파싱 실패 시 폴백 기본값).

### 수신 (Unity 앱)
`RtpH264Receiver` (UDP:5600) → `VideoDecoder` (MediaCodec) → `OVROverlay`. 포트 외 별도 설정 없음.
성공 시 로그:
```
[Rtp] listening udp:5600
[Overlay] video 2560x720 -> external surface
[Overlay] external surface -> decoder
[Decoder] MediaCodec started
```

**패킷 손실 처리** — `RtpH264Receiver.waitForIdrOnLoss`:
- **ON**(기본): 손실 시 다음 IDR까지 스킵 → 깨짐 없음, 대신 끊김. GOP 짧을수록 끊김↓.
- **OFF**: 손실 프레임도 그대로 공급 → 30fps 부드러움, 손실 순간만 약간 깨짐. teleop 권장.
- 초당 진단 로그(`adb logcat -s Unity | grep "Rtp\] 1s"`): `delivered=fps loss=N waitIDR=N queue=N`.
  `loss` 가 잦으면 WiFi 손실이 근본 원인 → 유선 백홀 + 전용 5GHz/WiFi6 AP 로 해결.

### Quest 배포 전 PC 에서 검증
발행부의 `host=` 를 데스크톱으로 두고 그곳에서 디코딩:
```bash
gst-launch-1.0 udpsrc port=5600 \
  ! application/x-rtp,media=video,encoding-name=H264,payload=96 \
  ! rtph264depay ! h264parse ! avdec_h264 ! autovideosink sync=false
```
(`avdec_h264` 는 `gstreamer1.0-libav`, `h264parse` 는 `gstreamer1.0-plugins-bad` 필요.)

### RTSP 릴레이 (선택, 고화질 / GPU 오프로드)
데스크톱 GPU 파이프라인(NVENC H.265, 고해상도)을 쓰려면 스트리밍 PC 가 **RTSP 서버**(예: MediaMTX)로
GStreamer/ffmpeg 를 통해 발행할 수 있다:
```bash
# 예: 들어온 SBS 스트림을 NVENC 로 재인코딩해 RTSP 로 발행
ffmpeg -i - -c:v hevc_nvenc -preset p1 -tune ll -f rtsp rtsp://127.0.0.1:8554/zed
```
주의: 현재 앱 수신부는 **raw RTP/UDP** 를 받으며 RTSP 는 받지 않는다. RTSP 릴레이를 쓰려면
(a) RTSP→RTP 를 UDP:5600 으로 흘려주는 GStreamer 브리지를 두거나, (b) `RtpH264Receiver` 를 RTSP
핸드셰이크 지원으로 개조해야 한다. GPU 오프로드가 꼭 필요한 경우가 아니면 위의 직접 RTP/UDP 경로를 권장.

---

## 포즈 업링크 — 제어 PC (ROS 2)

### 앱이 발행하는 토픽
| 토픽 | 타입 | 내용 |
|------|------|------|
| `/vr/hmd_pose` | `geometry_msgs/PoseStamped` | 헤드셋 위치 + 자세 |
| `/vr/left_hand_pose` | `geometry_msgs/PoseArray` | 왼손 관절 포즈 |
| `/vr/right_hand_pose` | `geometry_msgs/PoseArray` | 오른손 관절 포즈 |

포즈는 약 60Hz 로 발행되며 `.To<FLU>()` Unity→ROS 변환과 `frame_id = vr_origin` 이 적용된다.

### 제어 PC 에서 엔드포인트 실행
```bash
ros2 run ros_tcp_endpoint default_server_endpoint --ros-args -p ROS_IP:=0.0.0.0
# 검증
ros2 topic hz /vr/hmd_pose
ros2 topic echo /vr/hmd_pose --once
```

---

## 인헤드셋 컨트롤

### 영상 튜닝
| 입력 | 손 | 동작 |
|------|-----|------|
| **X** (홀드) | 왼손 | 스테레오 컨버전스 감소 |
| **Y** (홀드) | 왼손 | 스테레오 컨버전스 증가 |
| **A** (홀드) | 오른손 | 영상 패널 가까이 (`localPosition.z↓`) |
| **B** (홀드) | 오른손 | 영상 패널 멀리 (`localPosition.z↑`) |
| **엄지스틱** ↕↔ | 오른손 | 영상 패널 상하/좌우 이동 (팬) |
| **엄지스틱 클릭** | 오른손 | 패널 위치 초기화 (x/y 팬 + z 거리, 시작 위치로) |
| 좌/우 눈 스왑 | — | 인스펙터 `Swap Eyes` (깊이가 반대로 느껴질 때) |

로그(`[Overlay] convergence=...` / `distance=...` / `pan=...`)에서 편한 값을 찾은 뒤,
`VideoOverlayController.convergence` 와 VideoLayer `localPosition` 에 고정하고
*Enable Button Tuning* 을 해제한다.

### IP 설정 (`IpConfigController`)
ROS 발행 대상 IP 를 **재빌드 없이** 앱 안에서 변경한다. (영상 수신은 UDP `Any` 바인딩이라 무관 — 바뀌는 건 업링크뿐.)

| 입력 | 손 | 동작 |
|------|-----|------|
| **Menu** | 왼손 | IP 편집기 열기 / (열려 있으면) 취소하고 닫기 |
| **엄지스틱** ←/→ | 왼손 | 편집할 자리(옥텟) 선택 (`[ ]` 표시) |
| **엄지스틱** ↑/↓ | 왼손 | 선택한 옥텟 값 +/- (0~255, 홀드 시 반복) |
| **트리거** | 왼손 | 확인 → 즉시 재연결 + `PlayerPrefs` 저장 |

- 편집기가 뜨면 영상 오버레이가 잠시 숨겨진다(컴포지터 레이어가 앱 화면 위에 합성되므로). 확인/취소 시 복원.
- 저장값은 앱 재실행 후에도 유지되며 씬의 ROS Settings 보다 우선한다.
- Quest 는 Unity `TouchScreenKeyboard` 를 지원하지 않아 시스템 키보드 대신 이 컨트롤러 방식으로 입력한다.

---

## 알려진 이슈 — Meta XR SDK 컴파일 에러 (자동 수정됨)

Meta XR Core SDK **v203.0.0** 은 `RuntimeOptimizer/Core/RuntimeOptimizerPlugin.cs` 에서 `#define`
지시문을 `using` 뒤에 두고 shipping 해 **CS1032** 로 전체 컴파일을 막는다. `Library/PackageCache/` 는
gitignore + 재임포트마다 원본으로 덮여써서 클론 / Library 삭제 때마다 재발한다.

[`Assets/Editor/MetaSdkFix/`](Assets/Editor/MetaSdkFix) 에 참조가 비어 있는 독립 Editor 어셈블리
(`MetaSdkFix.Editor`)가 있어, Meta 어셈블리가 깨져도 별도로 컴파일된다. 에디터 로드 시 깨진 파일을 감지해
`#define` 블록을 `using` 위로 옮기고 재컴파일을 요청한다. 이미 정상이면 no-op 이므로 —
**프로젝트를 Unity 에서 한 번 열어 재컴파일되게 두면 된다.**
