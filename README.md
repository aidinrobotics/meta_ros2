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

### 2.3 Build Settings

- `File > Build Profiles`
  - `Scene List`
  - Check "Scenes/XROSScene"

### 2.4 Build and Run

- Connect Meta Quest via USB
- Click **Build and Run**
- Save as `.apk` and deploy

---

### Example: Right Arm

```python
right_builder = (
    rby.CartesianImpedanceControlCommandBuilder()
    .set_joint_stiffness([80, 80, 80, 80, 80, 80, 40])         # 7 Joints (Joint 0–5: 80 Nm/rad, Joint 6: 40 Nm/rad)
    .set_joint_torque_limit([30]*7)             # Torque clamped to ±30 Nm
    .add_joint_limit("right_arm_3", -2.6, -0.5) # Prevent overstretch / singularity
    .add_target("base", "link_right_arm_6", right_T, 2, np.pi*2, 20, np.pi*80)
          # move "link_right_arm_6" link to `right_T` with respect to "base"
          # (linear velocity limit: 2 m/s,
          #  angular velocity limit: 2π rad/s,
          #  linear acceleration limit: 20 m/s²,
          #  angular acceleration limit: 80π rad/s²)
)
```

---

### Composition

```python
ctrl_builder = (
    rby.BodyComponentBasedCommandBuilder()
    .set_torso_command(torso_builder)
    .set_right_arm_command(right_builder)
    .set_left_arm_command(left_builder)
)
```

> Tip: When `--whole_body` is used, the torso, right arm, and left arm targets are added into a **single CartesianImpedanceControlCommandBuilder**, instead of splitting by body part.

This `ctrl_builder` is passed into the robot control stream:

```python
stream.send_command(
    rby.RobotCommandBuilder().set_command(
        rby.ComponentBasedCommandBuilder()
        .set_body_command(ctrl_builder)
        ...
    )
)
```
