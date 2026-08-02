# VR Teleop — Quest 앱 스크립트 배선 가이드

`vr_teleop_pipeline_guide.md` Part 2 구현. 영상 다운링크(UDP/RTP/H.264 → MediaCodec →
OVROverlay SBS)와 포즈 업링크(ROS-TCP)를 한 앱에서 독립 구동한다.

## 1. 필수 패키지
- **Meta XR Core SDK** (`com.meta.xr.sdk.core`) — OVROverlay / OVRCameraRig / OVRSkeleton
- **ROS-TCP-Connector**
  `https://github.com/Unity-Technologies/ROS-TCP-Connector.git?path=/com.unity.robotics.ros-tcp-connector`
  설치 후 `Robotics → Generate ROS Messages` 로 geometry_msgs 생성.

## 2. Scripting Define Symbols
Player Settings > Other Settings > Scripting Define Symbols 에 추가:
```
USE_META_XR;USE_ROS_TCP
```
(패키지 설치 전에도 스크립트가 컴파일되도록 Android 특정/SDK 특정 코드는 심볼로 가드됨.)

## 3. 빌드 설정
- Platform: **Android**, IL2CPP + **ARM64**, Minimum API **32+**
- AndroidManifest: `android.permission.INTERNET`
- XR Plug-in Management: Meta/Oculus 로더 활성

## 4. 씬 배선
1. `SampleScene` 에 **OVRCameraRig** 추가.
2. 빈 GameObject **VideoLayer** 생성 후 컴포넌트 추가:
   - `OVROverlay`
   - `RtpH264Receiver`  (port=5600, Jetson `--port` 와 일치)
   - `VideoDecoder`     (width/height = SBS 해상도. `--scale` 적용 시 맞추기)
   - `VideoOverlayController` (decoder 필드에 위 VideoDecoder 연결)
   - `RobotStateDisplay` (로봇 상태 `std_msgs/String` 을 캔버스 하단 상태바로 표시.
     상태바(TextMesh→RenderTexture→OVROverlay)는 런타임 생성되므로 별도 배선 없음)
3. 빈 GameObject **RosBridge** 생성 후:
   - `ROSConnection` (Inspector 에서 제어 PC IP / 포트 설정. 앱 내 변경도 가능 — `IpConfigController`)
   - `VrTeleopPublisher`
     - head = OVRCameraRig 의 CenterEyeAnchor
     - leftHand / rightHand = OVRSkeleton (Hand tracking)
     - hideHandMesh = ON 이면 화면의 3D 손 메시를 숨김(관절 발행은 유지)
   - `IpConfigController` (왼손 Menu 버튼으로 발행 IP 를 앱 안에서 변경)

## 5. 데이터 흐름
```
RtpH264Receiver ──Frames(ConcurrentQueue)──> VideoDecoder ──render──> OVROverlay Surface
                                                   ▲
VideoOverlayController: externalSurfaceObject ─────┘ (SBS 좌/우 눈 분리)

VrTeleopPublisher ──ROS-TCP(TCP)──> 제어 PC ROS-TCP-Endpoint  (/vr/hmd_pose, /vr/{left,right}_hand_pose)

로봇 ──/aidin_rby1_vive_teleop/state(String)──> RobotStateDisplay
      └▶ TextMesh(오프스크린) ─Camera─> RenderTexture ─> OVROverlay(Quad, compositionDepth = 영상−1)
```

> 상태바가 영상 위에 보이는 이유: 영상은 컴포지터 레이어(OVROverlay)라 앱이 그린 3D 텍스트를
> 항상 덮는다. 그래서 상태바도 레이어로 올리고 `compositionDepth` 를 영상보다 **작게**(= 더 앞) 준다.
>
> 상태바 RenderTexture 주의점(글씨 잔상 원인 2가지, 둘 다 처리됨):
> - **MSAA 금지** — OVROverlay 는 모바일에서 RT → 스왑체인을 `CopyTexture` 로 그대로 복사한다
>   (`PopulateLayer` 의 `bypassBlit`). 멀티샘플 RT 는 이 복사가 성립하지 않아 이전 프레임이 남는다.
> - **clear 를 믿지 말 것** — URP 에서 수동 `Camera.Render()` 는 중간 타깃을 재사용할 수 있다.
>   배경을 불투명 Quad 로 **그려서** 덮는다(`barColor` 알파는 1 유지).

## 6. 테스트 순서
1. 데스크톱에서 Jetson 파이프라인을 Quest IP 로 송출
   (`zed_sbs_rtsp_node.py --host <QUEST_IP> --port 5600`).
2. Quest 앱에서 SBS 영상이 양안에 뜨는지 확인 (로그 `[Decoder] MediaCodec started`).
3. 제어 PC 에서 `ros2 topic echo /vr/head_pose` 로 포즈 수신 확인.

## 남은 TODO (device 검증 필요)
- `VideoDecoder.Configure()` 의 `new AndroidJavaObject(_surfacePtr)` — 외부 서피스 ptr 수명/래핑은
  실기기에서 확인. 문제 시 `AndroidJNI.NewLocalRef` 로 감싸 전달.
- 손 관절 매핑: Quest 스켈레톤 → dex-retargeting 입력 규격(관절 수/순서) 매핑 테이블 확정.
- 지연/유실 튜닝: `RtpH264Receiver.maxQueued`, `VideoDecoder` 출력 폴링 주기.
- 좌표 변환 `.To<FLU>()` 실제 로봇 기준 프레임과 일치 여부(clutch/프레임 고정 포함).
