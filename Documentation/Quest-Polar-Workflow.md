# Quest And Polar Workflow

This project uses a Quest-first runtime layout with an in-scene button,
controller-driven HUD, and a Polar H10-compatible BLE connection stack.

## Scene layout

`Assets/Scenes/SampleScene.unity` contains these key objects:

- `OVRCameraRig`
- `Big Red Button`
- `VR Runtime`
- `VR Runtime/VR Overlay HUD`
- `VR Runtime/Button Press Counter Canvas`
- `VR Runtime/Biofeedback Connection Hub`
- `VR Runtime/Polar H10 Breathing Source`

The important detail is that the BLE / Polar graph is now scene-authored and
serialized, not rebuilt from scratch on every connect attempt.

## Why the scene-authored Polar graph matters

The Polar and BLE adapters depend on native / Java callbacks that target Unity
objects by name and lifecycle timing:

- `BleAdapter` renames its GameObject to `BleAdapter` in `Awake()`
- `PolarPmdAdapter` renames its GameObject to `PolarPmdAdapter` and detaches it
  from the scene hierarchy in `Awake()`

When the project tried to rebuild the whole BLE / Polar graph dynamically, it
could create duplicate runtime objects after those adapters renamed or detached
themselves. That made the connection flow unreliable.

The fix is:

- keep a stable `Biofeedback Connection Hub`
- keep a stable `Polar H10 Breathing Source`
- preserve serialized references on `PolarH10RuntimeManager`
- let the runtime manager configure the existing graph instead of inventing a
  new one

## Permission model

Manifest entries alone are not enough for Quest / Android BLE access.

This project now follows a split that keeps permission handling, BLE readiness,
Polar discovery, and app-level status coordination separate:

- `BluetoothPermissionsBootstrap` owns runtime permission requests
- `BleCentral` only probes runtime readiness
- `PolarUnifiedModule` handles Polar connection / scan behavior
- `PolarH10RuntimeManager` coordinates app-level startup and HUD status

For Quest / Android 12+ BLE work, request:

- `android.permission.BLUETOOTH_SCAN`
- `android.permission.BLUETOOTH_CONNECT`
- `android.permission.ACCESS_FINE_LOCATION`

Even on Android 12+ / 14, location permission is still requested because some
BLE stacks still behave as if scan access is location-gated.

## HUD and commands

The HUD is controlled from `QuestVrInputManager` and `QuestVrOverlayHud`.

Current pages:

- `Dashboard`
- `Permissions`
- `Signals`
- `Terminal`
- `Input`

Current useful terminal commands:

- `polar_permissions`
- `polar_connect`
- `polar_scan`
- `polar_clear_saved_device`
- `broker_status`
- `broker_connect`
- `broker_subscribe`
- `broker_polar_hr_start`
- `broker_polar_pmd_start`
- `broker_polar_stop`
- `broker_drive_button`
- `broker_open_ui`
- `broker_close_ui`
- `center_button`
- `press_button`
- `toggle_hud`
- `status`

## Button behavior

- The imported button lives in the scene as a normal imported asset.
- `QuestVrButtonPressCounterCanvas` shows the accepted press count above the
  physical button for headset checks where the HUD is hidden.
- `PolarHeartbeatButtonDriver` listens to processed heartbeat samples.
- When Polar tracking is connected and confidence is high enough, accepted beat
  events trigger the button press animation.
- Broker drive pulses use the same button press method, so the visible counter,
  HUD press count, and button animation remain comparable across direct and
  broker-routed paths.
- `BigRedButtonDirectPolarDiagnosticReceiver` records direct Unity Polar HR/RR
  notifications and decoded PMD ACC/ECG frames in the diagnostic route table.
- `RustyXrBrokerBioSignalReceiver` records Gargoyle `bio:polar_hr_rr`,
  `bio:polar_acc`, `bio:polar_ecg`, and `bio:breath` stream events without
  owning Unity's direct BLE connection.

Treat Polar PMD as single-owner during live sensor tests. The direct Unity PMD
path and the broker-owned PMD path are both instrumented for comparison, but
they should not both attempt to own the same H10 PMD stream unless that is the
specific contention test.

## Build workflow

Use:

- `Tools > Big Red Button > Build Quest APK`

This runs `QuestVrApkBuilder`, reinstalls the Quest scene layout, configures the
Android build target, and writes the APK to:

- `Builds/Android/TheBigRedButtonInstitute.apk`

## Useful verification commands

On a connected headset, these commands are useful when debugging BLE:

```powershell
adb shell dumpsys package org.thebigredbuttoninstitute.app
adb logcat -d
adb shell am start -W -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -c com.oculus.intent.category.VR
```

`dumpsys package` is especially useful to distinguish:

- permissions are granted, but connection startup is broken
- permissions are missing, so scan / connect never had a chance

## Current implementation files

- `Assets/Scripts/Biofeedback/PolarH10RuntimeManager.cs`
- `Assets/Scripts/Biofeedback/PolarHeartbeatButtonDriver.cs`
- `Assets/Scripts/Diagnostics/BigRedButtonDirectPolarDiagnosticReceiver.cs`
- `Assets/Scripts/RustyXrBroker/RustyXrBrokerBioSignalReceiver.cs`
- `Assets/Scripts/VR/QuestVrInputManager.cs`
- `Assets/Scripts/VR/QuestVrButtonPressCounterCanvas.cs`
- `Assets/Scripts/VR/QuestVrOverlayHud.cs`
- `Assets/Scripts/RustyXrBroker/`
- `Assets/Editor/PolarH10SceneInstaller.cs`
- `Assets/Editor/QuestVrSceneInstaller.cs`
- `Assets/Editor/QuestVrApkBuilder.cs`
