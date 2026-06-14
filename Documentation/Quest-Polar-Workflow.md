# Quest And Polar Workflow

This project uses a Quest-first runtime layout with an in-scene button,
controller-driven HUD, direct Polar H10-compatible BLE support, direct LSL,
direct OSC, and Manifold-facing contract DTOs.

## Scene Layout

`Assets/Scenes/SampleScene.unity` contains these key objects:

- `OVRCameraRig`
- `Big Red Button`
- `VR Runtime`
- `VR Runtime/VR Overlay HUD`
- `VR Runtime/Button Press Counter Canvas`
- `VR Runtime/Biofeedback Connection Hub`
- `VR Runtime/Polar H10 Breathing Source`

The BLE / Polar graph is scene-authored and serialized, not rebuilt from
scratch on every connect attempt.

## Why The Scene-Authored Polar Graph Matters

The Polar and BLE adapters depend on native / Java callbacks that target Unity
objects by name and lifecycle timing:

- `BleAdapter` renames its GameObject to `BleAdapter` in `Awake()`.
- `PolarPmdAdapter` renames its GameObject to `PolarPmdAdapter` and detaches it
  from the scene hierarchy in `Awake()`.

The stable graph avoids duplicate runtime objects after those adapters rename
or detach themselves.

The fix is:

- keep a stable `Biofeedback Connection Hub`;
- keep a stable `Polar H10 Breathing Source`;
- preserve serialized references on `PolarH10RuntimeManager`;
- let the runtime manager configure the existing graph.

## Permission Model

Manifest entries alone are not enough for Quest / Android BLE access.

This project keeps permission handling, BLE readiness, Polar discovery, and
app-level status coordination separate:

- `BluetoothPermissionsBootstrap` owns runtime permission requests.
- `BleCentral` only probes runtime readiness.
- `PolarUnifiedModule` handles Polar connection / scan behavior.
- `PolarH10RuntimeManager` coordinates app-level startup and HUD status.

For Quest / Android 12+ BLE work, request:

- `android.permission.BLUETOOTH_SCAN`
- `android.permission.BLUETOOTH_CONNECT`
- `android.permission.ACCESS_FINE_LOCATION`

Location permission is still requested because some BLE stacks still behave as
if scan access is location-gated.

## HUD And Commands

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
- `questionnaire_open`
- `center_button`
- `press_button`
- `blink_button`
- `stop_blink`
- `toggle_hud`
- `status`

## Button Behavior

- The imported button lives in the scene as a normal imported asset.
- `QuestVrButtonPressCounterCanvas` shows the accepted press count above the
  physical button for headset checks where the HUD is hidden.
- `PolarHeartbeatButtonDriver` listens to processed heartbeat samples.
- Accepted Polar heartbeat pulses call
  `QuestVrInputManager.TriggerButtonBlinkFromRuntime()`, so heartbeat feedback
  blinks the button without counting as a button press.
- Direct LSL and direct OSC can drive
  `QuestVrInputManager.TriggerButtonPressFromRuntime()`, so accepted threshold
  crossings play the press animation and increment the visible counter.
- `BigRedButtonDirectPolarDiagnosticReceiver` records direct Unity Polar HR/RR
  notifications and decoded PMD ACC/ECG frames in the diagnostic route table.
- `BigRedButtonDirectLslDriveReceiver` records app-owned LSL threshold input.
- `BigRedButtonDirectOscDriveReceiver` records app-owned OSC threshold input.

Direct OSC defaults:

```text
listen port: 9001
drive address: /brb/manifold/drive/button
ack address: /brb/manifold/drive/ack
```

## Build Workflow

Use:

- `Tools > Big Red Button > Build Quest APK`

This runs `QuestVrApkBuilder`, reinstalls the Quest scene layout, configures
the Android build target, and writes the APK to:

- `Builds/Android/TheBigRedButtonInstitute.apk`

## Useful Verification Commands

On a connected headset, these commands are useful when debugging BLE:

```powershell
adb shell dumpsys package org.thebigredbuttoninstitute.app
adb logcat -d
adb shell am start -W -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -c com.oculus.intent.category.VR
```

`dumpsys package` helps distinguish missing permissions from connection startup
problems.

## Current Implementation Files

- `Assets/Scripts/Biofeedback/PolarH10RuntimeManager.cs`
- `Assets/Scripts/Biofeedback/PolarHeartbeatButtonDriver.cs`
- `Assets/Scripts/Diagnostics/BigRedButtonDirectPolarDiagnosticReceiver.cs`
- `Assets/Scripts/Diagnostics/BigRedButtonDirectLslDriveReceiver.cs`
- `Assets/Scripts/Diagnostics/BigRedButtonDirectOscDriveReceiver.cs`
- `Assets/Scripts/Diagnostics/BigRedButtonDriveSignal.cs`
- `Assets/Scripts/Morphospace/Manifold/ManifoldProtocol.cs`
- `Assets/Scripts/VR/QuestVrInputManager.cs`
- `Assets/Scripts/VR/QuestVrButtonPressCounterCanvas.cs`
- `Assets/Scripts/VR/QuestVrOverlayHud.cs`
- `Assets/Editor/PolarH10SceneInstaller.cs`
- `Assets/Editor/QuestVrSceneInstaller.cs`
- `Assets/Editor/QuestVrApkBuilder.cs`
