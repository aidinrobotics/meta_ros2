# meta_ros2 — RB-Y1 VR Teleoperation (Meta Quest 3)

Meta Quest 3 Unity app for **bidirectional, low-latency VR teleoperation** of the RB-Y1
robot. Two independent subsystems run in one app:

| Direction | Data | Transport | Why |
|-----------|------|-----------|-----|
| **Downlink** (streaming PC → Quest) | Stereo camera (SBS) | **RTP / H.264 over UDP** | high bandwidth, latency-sensitive, drop-tolerant |
| **Uplink** (Quest → control PC) | Head + hand pose | **ROS-TCP (TCP)** | small, high-rate (60 Hz), lossless, ordered |

> The two streams are deliberately kept on separate channels — do **not** bundle video (UDP)
> and pose (TCP) together.

```
[Streaming PC]                                   [Meta Quest 3 app]
 ZED SBS ─ GStreamer ─ x264 ─ RTP/H.264 ──UDP:5600──▶ RtpH264Receiver
                                                     └▶ VideoDecoder (MediaCodec)
                                                        └▶ OVROverlay (SBS → L/R eye)
 CenterEye + Hand tracking ─ ROSConnection ──TCP:10000──▶ ROS-TCP-Endpoint (control PC)
                                                          /vr/head_pose, /vr/{left,right}_hand
```

---

## Components

All runtime scripts live in [`Assets/Scripts/`](Assets/Scripts):

| Script | Role |
|--------|------|
| `RtpH264Receiver.cs` | UDP socket (port 5600), RTP depacketize (single NAL / STAP-A / **FU-A** reassembly per RFC 6184), collects SPS/PPS, emits Annex-B access units |
| `VideoDecoder.cs` | Drives `MediaCodec("video/avc")` via AndroidJNI (no `.aar`), `low-latency=1`, renders to the OVROverlay external surface |
| `VideoOverlayController.cs` | OVROverlay external surface, splits SBS into left `(0,0,0.5,1)` / right `(0.5,0,0.5,1)` eyes, convergence / eye-swap tuning |
| `VrTeleopPublisher.cs` | Publishes head + hand pose over ROS-TCP at 60 Hz, with Unity→ROS `FLU` coordinate conversion |

See [`Assets/Scripts/README_VrTeleop.md`](Assets/Scripts/README_VrTeleop.md) for detailed scene-wiring steps.

---

## Requirements

- **Meta Quest 3** (Developer Mode enabled)
- **Unity 6000.3.x** with Android Build Support (IL2CPP + ARM64, min SDK 32)
- **Meta XR Core SDK** (`com.meta.xr.sdk.core`) — OVRCameraRig, OVROverlay, OVRSkeleton
- **ROS-TCP-Connector** (Unity) + **ROS-TCP-Endpoint** (control PC, ROS 2 Humble)
- Streaming PC with **GStreamer 1.x** (`x264enc`, `rtph264pay`, `h264parse`, `udpsink`)

---

## Unity setup

### 1. Packages
- Meta XR Core SDK (Unity Registry / Meta All-in-One SDK)
- ROS-TCP-Connector:
  `https://github.com/Unity-Technologies/ROS-TCP-Connector.git?path=/com.unity.robotics.ros-tcp-connector`

> `geometry_msgs` / `std_msgs` are **bundled** with ROS-TCP-Connector — do **not** re-generate
> them (duplicate types cause CS0029). Use *Generate ROS Messages* only for custom messages.

### 2. Scripting Define Symbols
`Player Settings → Android → Scripting Define Symbols`:
```
USE_META_XR;USE_ROS_TCP
```
(SDK-specific code is guarded by these; the project still compiles before the packages are installed.)

### 3. Scene (`Assets/Scenes/SampleScene.unity`)
- **OVRCameraRig** (OVRManager: Hand Tracking = *Controllers And Hands*, Quest 3)
- **VideoLayer** (child of `CenterEyeAnchor`): `OVROverlay` + `RtpH264Receiver` + `VideoDecoder` + `VideoOverlayController`; quad scaled **16:9**
- **RosBridge**: `ROSConnection` + `VrTeleopPublisher` (head = CenterEyeAnchor, hands = OVRSkeleton)

### 4. ROS connection
`Robotics → ROS Settings`: ROS IP = control-PC IP, port `10000`, protocol **ROS2**.

### 5. Passthrough (see-through background)
- OVRManager: Passthrough Support = *Supported*, **Enable Passthrough**
- Add **OVRPassthroughLayer** (Placement = *Underlay*)
- CenterEye Camera → `Environment → Background Type = Solid Color`, color alpha **0**

### 6. Build
`Internet Access = Require` (needed for the UDP socket), then **Build And Run**.

---

## Video downlink — streaming PC (GStreamer)

The app receives **raw RTP / H.264 over UDP** on port **5600** (payload type 96). The streaming
PC publishes with a GStreamer pipeline whose tail must be `rtph264pay ! udpsink`.

### Publish (streaming PC → Quest)

Send a **side-by-side (SBS) stereo** H.264 stream to the headset. Replace `<QUEST_IP>` with the
Quest's LAN IP and the source with your camera (`videotestsrc` shown for a quick test):

```bash
gst-launch-1.0 -v \
  videotestsrc is-live=true ! video/x-raw,width=2560,height=720,framerate=30/1 ! \
  videoconvert ! video/x-raw,format=I420 ! \
  x264enc tune=zerolatency speed-preset=ultrafast bitrate=15000 key-int-max=30 bframes=0 ! \
  video/x-h264,profile=baseline ! h264parse config-interval=1 ! \
  rtph264pay pt=96 config-interval=1 ! \
  udpsink host=<QUEST_IP> port=5600 sync=false
```

Key points that must match the receiver:
- **`rtph264pay pt=96 config-interval=1`** — payload 96, and SPS/PPS re-sent every keyframe so a
  late-joining decoder initializes quickly.
- **`profile=baseline`, `bframes=0`, `tune=zerolatency`** — no reorder delay.
- Frame is **SBS**: left half → left eye, right half → right eye (handled by `VideoOverlayController`).
  Default expected size is **2560×720** (per-eye 1280×720); update `VideoDecoder.width/height` if you change it.

### Receive (Unity app)
`RtpH264Receiver` (UDP:5600) → `VideoDecoder` (MediaCodec) → `OVROverlay`. Nothing to configure
beyond the port. Logs on success:
```
[Rtp] listening udp:5600
[Overlay] external surface -> decoder
[Decoder] MediaCodec started
```

### Verify on a PC before deploying to the Quest
Point the sender's `host=` at a desktop and decode there:
```bash
gst-launch-1.0 udpsrc port=5600 \
  ! application/x-rtp,media=video,encoding-name=H264,payload=96 \
  ! rtph264depay ! h264parse ! avdec_h264 ! autovideosink sync=false
```
(Needs `gstreamer1.0-libav` for `avdec_h264` and `gstreamer1.0-plugins-bad` for `h264parse`.)

### RTSP relay (optional, higher quality / GPU offload)
For a desktop-GPU pipeline (NVENC H.265, higher resolution), the streaming PC can publish to an
**RTSP server** (e.g. MediaMTX) with GStreamer/ffmpeg:
```bash
# example: re-encode an incoming SBS stream to RTSP via NVENC
ffmpeg -i - -c:v hevc_nvenc -preset p1 -tune ll -f rtsp rtsp://127.0.0.1:8554/zed
```
Note: the current in-app receiver consumes **raw RTP/UDP**, not RTSP. Using an RTSP relay requires
either an RTSP→RTP GStreamer bridge feeding UDP:5600, or adapting `RtpH264Receiver` to perform the
RTSP handshake. Prefer the direct RTP/UDP path above unless you specifically need the GPU offload.

---

## Pose uplink — control PC (ROS 2)

### Topics published by the app
| Topic | Type | Contents |
|-------|------|----------|
| `/vr/head_pose` | `geometry_msgs/PoseStamped` | headset position + orientation |
| `/vr/left_hand` | `geometry_msgs/PoseArray` | left-hand joint poses |
| `/vr/right_hand` | `geometry_msgs/PoseArray` | right-hand joint poses |

Poses are published at ~60 Hz with `.To<FLU>()` Unity→ROS conversion and `frame_id = vr_origin`.

### Run the endpoint on the control PC
```bash
ros2 run ros_tcp_endpoint default_server_endpoint --ros-args -p ROS_IP:=0.0.0.0
# verify
ros2 topic hz /vr/head_pose
ros2 topic echo /vr/head_pose --once
```

---

## In-headset controls (video tuning)

| Input | Action |
|-------|--------|
| **A** (hold) | decrease stereo convergence |
| **B** (hold) | increase stereo convergence |
| **X** | toggle left/right eye swap (if depth looks inverted) |

Find a comfortable value in the log (`[Overlay] convergence=...`), then set it as the default on
`VideoOverlayController.convergence` and uncheck *Enable Button Tuning*.

---

## Known issue — Meta XR SDK compile error (auto-fixed)

Meta XR Core SDK **v203.0.0** ships `RuntimeOptimizer/Core/RuntimeOptimizerPlugin.cs` with `#define`
directives placed after `using` statements → **CS1032**, which blocks all compilation. Because
`Library/PackageCache/` is git-ignored and regenerated, it recurs on every clone / Library wipe.

[`Assets/Editor/MetaSdkFix/`](Assets/Editor/MetaSdkFix) contains an isolated Editor assembly
(`MetaSdkFix.Editor`, no references) that compiles even when the Meta assembly fails, detects the
broken file on editor load, moves the `#define` block above the `using`s, and triggers recompilation.
It is a no-op once the file is correct — **just open the project in Unity and let it recompile once.**
