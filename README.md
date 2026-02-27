# Unity package for ROS2 publishing (Meta Quest + Unity)


## Requirements

- Meta Quest 3 (Developer Mode enabled)
- Unity Editor (tested on **Unity 6000.2.7f2**)
- Android Build Support (installed via Unity Hub)
- [ROS-TCP-Endpoint](https://github.com/Unity-Technologies/ROS-TCP-Endpoint)

---

## Step 1: Enable Developer Mode on Meta Quest

Follow the official Meta guide here: [Meta Quest - Enable Developer Mode](https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/)

1. Log in to the **[Meta Developer site](https://developer.oculus.com/manage/organizations/)** and create an organization if needed.
2. On your smartphone:
   - Install the **Meta Quest mobile app**
   - Go to **Devices > Developer Mode** and enable it
3. Connect the headset to your PC via USB and accept USB debugging permission.

---

## Step 2: Unity Setup for Meta Quest

> Unity must be installed with **Android Build Support**. This setup assumes a clean project using the **Universal Render Pipeline (URP)** or **3D (Core)** template.

### 2.1 Create Unity Project

- Open Unity Hub → `add project from the disk`
- Choose meta_ros2 package directory

### 2.2 Add Required Packages

Go to `Window > Package Manager` and install the following:

- **Meta XR Interaction SDK** 
- **XR Hands**
- **XR Interaction Toolkit**

### 2.3 Setup Host PC IP
- From Scene Hierarchy, click `RosManager`
- Set `default IP` as Host IP address

### 2.4 Build Settings

- `File > Build Profiles`
  - `Scene List`
  - Check `Scenes/XROSScene`

### 2.5 Build and Run

- Connect Meta Quest via USB
- Click **Build and Run**
- Save as `.apk` and deploy

---

### Tutorial

1. Launch ROS-TCP-Endpoint to get meta quest data

```bash
ros2 launch ros_tcp_endpoint endpoint.launch
```

2. Run installed meta_ros2 app in the VR Headset

