# New Agent Integration Brief

This brief introduces the two public repositories and the direct data paths that
can drive the Big Red Button experience without relying on the Rusty XR broker.
It is written for a new machine or CI environment, so verify local tools,
device availability, and Unity installation paths before running commands.

## Public repositories

### The Big Red Button Institute

Repository: https://github.com/MesmerPrism/the-big-red-button-institute

Role:

- Unity Quest project for the Big Red Button experience.
- Owns the Unity scene, Quest APK build, button interaction, direct Polar route,
  direct LSL route, direct OSC route, diagnostics HUD, and questionnaire caller
  bridge.
- Current questionnaire-panel work lives on branch
  `codex/brb-questionnaire-panel-bridge` unless it has already been merged.

Important repo paths:

- `ProjectSettings/ProjectVersion.txt` declares the Unity editor version.
- `AGENTS.md` describes repo-local build rules and Unity version policy.
- `Documentation/Quest-Polar-Workflow.md` describes the Quest/Polar scene setup.
- `Documentation/Rusty-XR-Project-Integration.md` describes the public boundary
  between this Unity project and Rusty XR work.
- `Documentation/Rusty-XR-Broker-Unity-Compatibility.md` describes broker
  compatibility, but the direct Polar and direct LSL paths below do not require
  that broker.
- `Assets/Scenes/SampleScene.unity` is the Quest scene.
- `Assets/Editor/QuestVrApkBuilder.cs` contains the batchmode APK build entry
  point.
- `Assets/Scripts/Questionnaire/QuestQuestionnairePanelLauncher.cs` is the Unity
  questionnaire launch wrapper.
- `Assets/Plugins/Android/QuestionnairePanelBridge.java` is the Android bridge
  that launches the standalone questionnaire panel.
- `Assets/Scripts/Biofeedback/PolarH10RuntimeManager.cs` owns the direct Polar
  runtime graph.
- `Assets/Scripts/Biofeedback/PolarHeartbeatButtonDriver.cs` maps Polar
  heartbeat samples to button blink pulses.
- `Assets/Scripts/Diagnostics/BigRedButtonDirectLslDriveReceiver.cs` maps LSL
  samples to button presses.
- `Assets/Scripts/BigRedButtonBlinkController.cs` owns the button blink visual.
- `Assets/Scripts/VR/QuestVrInputManager.cs` is the convergence point for
  runtime-triggered presses and blinks.

### Quest Questionnaire Panel

Repository: https://github.com/MesmerPrism/quest-questionnaire-panel

Role:

- Native Android/Quest 2D questionnaire panel app.
- Includes a minimal native caller tester.
- Owns the `quest.questionnaire.v1` contract documentation and validation tests.

Important repo paths:

- `contract/intents.md` is the contract source of truth.
- `docs/handoff-contract.md` explains the caller/callee handoff.
- `docs/validation-matrix.md` lists validation expectations.
- `app/` contains the panel app package
  `io.github.mesmerprism.questquestionnaire.panel`.
- `examples/native-caller/` contains the native caller tester package
  `io.github.mesmerprism.questquestionnaire.nativecaller`.

## How the repositories connect

The Unity app is the caller. The native panel app is the callee.

The product communication pattern is:

1. The foreground Unity app launches the panel with an explicit Android intent.
2. Request metadata is passed in `extras/request_json`.
3. The Unity app provides a caller-owned `content://` result URI and grants write
   access for that URI.
4. The Unity app provides a one-shot immutable broadcast `PendingIntent` for
   completion.
5. The panel writes answers only to the caller-owned result URI.
6. The panel sends the completion `PendingIntent`.
7. The Unity app receives the callback and reads its own result URI.

The Quest package identities currently used by this flow are:

- Unity caller app: `org.thebigredbuttoninstitute.app`
- Questionnaire panel app:
  `io.github.mesmerprism.questquestionnaire.panel`

Do not replace this product path with ADB relaunches, public shared storage,
MediaStore, `file://`, package killing, `force-stop`, overlays,
`QUERY_ALL_PACKAGES`, or `SYSTEM_ALERT_WINDOW`.

ADB is acceptable for installing APKs and validation launches during development.
It is not the product communication mechanism.

## Build orientation

On a new machine, do not assume local paths from another developer. Clone the
public repositories wherever the environment expects source checkouts.

Panel repo Gradle checks:

```powershell
.\gradlew.bat :app:assembleDebug
.\gradlew.bat :examples:native-caller:assembleDebug
.\gradlew.bat :app:testDebugUnitTest
.\gradlew.bat :examples:native-caller:testDebugUnitTest
```

On non-Windows shells, use `./gradlew` instead of `.\gradlew.bat`.

Unity repo build orientation:

- Use the Unity version declared in `ProjectSettings/ProjectVersion.txt`, or a
  compatible newer Unity 6 editor permitted by `AGENTS.md`.
- Configure Android/Quest support in that Unity installation.
- Use this batchmode entry point for Quest APK builds:
  `TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk`.
- Write Unity logs under repo-local `Temp/` or `Builds/Android/` paths and read
  them after each build.
- The repo builder convention outputs Quest APK artifacts under
  `Builds/Android/`.

The Unity project is configured so the Quest player can use hands and
controllers. Do not regress this to controller-only input when editing player
settings.

## Minimal questionnaire validation path

Prerequisites:

- Build and install the panel APK on the Quest.
- Build and install the Unity APK on the same Quest.
- Confirm both packages use the identities listed above.
- Confirm the Unity branch includes the questionnaire bridge if the work has
  not been merged to `main`.

CLI launch examples:

```powershell
# Initial language/demographics/prior-experience panel sequence.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger initial

# Post-condition questionnaire after condition 1.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger post_condition_1

# Post-condition questionnaire after condition 2.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger post_condition_2

# Final end-confirmation / extra-presses sequence.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger final

# Backwards-compatible initial launch.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --ez brb.questionnaireOpen true
```

For debug smoke tests, add this boolean to any launch above:

```powershell
  --ez brb.questionnaireDebugAutoSubmit true
```

The debug auto-submit path is for validation only. For real interaction, launch
without `brb.questionnaireDebugAutoSubmit`.

For agentic UI-flow checks, pass a debug command script through the Unity
launcher to the panel. The script sets the same panel state that visible
Compose controls set, then uses the same Next/Submit handlers that write the
caller-owned result URI.

```powershell
# Complete the initial panel sequence through scripted UI-equivalent actions.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger initial `
  --es brb.questionnaireCommandScript "language:en-US,next,participant_code:QA001,age:30,next,prior:no,next" `
  --ei brb.questionnaireCommandIntervalMs 500

# Exercise the final non-10 branch into the extra-presses prompt.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger final `
  --es brb.questionnaireCommandScript "final:1,next,submit" `
  --ei brb.questionnaireCommandIntervalMs 700

# Exercise the final 10 branch, which skips the extra-presses prompt.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger final `
  --es brb.questionnaireCommandScript "final:10,next,submit" `
  --ei brb.questionnaireCommandIntervalMs 700
```

Separate script commands with commas for ADB shell calls; semicolons and
newlines are also accepted when the shell transport preserves them. Useful
panel command tokens include `language:<en-US|ja-JP>`,
`participant_code:<value>`, `age:<0-100>`, `prior:<yes|no>`,
`presence_slider:<0-100>`, `redness_vas:<0-100>`, `redness_likert:<1-7>`,
`ipq_all:<0-6>`, `lost_opportunity_ack`, `final:<1-10>`, `next`, `back`,
`replay_audio`, `submit`, and `cancel`.

Unity runtime command extras are separate from panel commands and drive the 3D
scene directly:

```powershell
# Play the imported press animation once and increment the world-space counter.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.runtimeCommand press_button

# Press three times with a visible gap between count increments.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.runtimeCommand press_button `
  --ei brb.runtimeCommandRepeat 3 `
  --ei brb.runtimeCommandIntervalMs 700

# Show the heartbeat-style continuous blink for six seconds.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.runtimeCommand "blink_button:6"

# Chain scene actions in one launch-extra script.
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.runtimeCommandScript "center_button,blink_button:6,press_button,status" `
  --ei brb.runtimeCommandIntervalMs 700
```

The runtime commands call `QuestVrInputManager` methods shared with HUD and
controller paths: `press_button` uses `TriggerButtonPressFromRuntime()` and
increments the visible counter; `blink_button:<seconds>` uses
`BigRedButtonBlinkController.SetBlinking(true)` and auto-stops after the
duration.

Inside Unity, future button or timer triggers should call the same wrapper used
by the CLI path:

```csharp
QuestQuestionnairePanelLauncher.LaunchInitialStudyQuestionnairesFromTrigger(
    "button",
    debugAutoSubmit: false);
QuestQuestionnairePanelLauncher.LaunchPostConditionQuestionnairesFromTrigger(
    conditionNumber: 1,
    triggerName: "condition_1_complete",
    debugAutoSubmit: false);
QuestQuestionnairePanelLauncher.LaunchFinalQuestionnairesFromTrigger(
    "final_session_complete",
    debugAutoSubmit: false);
```

Use another trigger label such as `"timer"` when the source is a timed event.
The important property is that all triggers share the same Android contract
implementation.

The split-app study scene keeps the Rusty XR broker and broker screen-gaze
visualizer off by default. The broker adapter source remains available for
future comparison work, but this branch should not require a broker sidecar or
show the `eye.screen.gaze_point` marker during normal questionnaire validation.

## Direct Polar route without Rusty XR broker

The Unity project can talk to Polar directly. The Rusty XR broker is not needed
for this route.

Primary classes:

- `PolarH10RuntimeManager`
- `PolarUnifiedModule`
- `PolarPmdAdapter`
- `PolarHeartbeatButtonDriver`
- `QuestVrInputManager`
- `BigRedButtonBlinkController`

Runtime flow:

1. `PolarH10RuntimeManager` owns the direct Polar graph in the Unity scene.
2. `PolarUnifiedModule` receives Polar heart-rate and PMD data. On Android, PMD
   transport is handled through the native bridge behind `PolarPmdAdapter`.
3. Heartbeat state is surfaced through `PolarH10RuntimeManager.HeartbeatSampleUpdated`.
4. `PolarHeartbeatButtonDriver` filters usable heartbeat samples.
5. Accepted heartbeat pulses call
   `QuestVrInputManager.TriggerButtonBlinkFromRuntime()`.
6. `QuestVrInputManager` calls `BigRedButtonBlinkController.PulseOnce()`.

This path blinks the button from heartbeats. It does not need the broker, and it
does not need LSL.

Quest/Android details to verify on the target machine and headset:

- The APK manifest includes Bluetooth and location permissions needed for BLE.
- Runtime permission prompts must be accepted on-device.
- The HUD command path includes `polar_permissions`, `polar_connect`,
  `polar_scan`, and `polar_clear_saved_device`.
- Polar H10 sensor identity, pairing state, and battery state are local
  environment facts. Do not commit them.
- If comparing against broker-based Polar routes, avoid having both direct Unity
  PMD and a broker process try to own the same live PMD stream at the same time.

The direct Polar diagnostic receiver,
`BigRedButtonDirectPolarDiagnosticReceiver`, subscribes to Polar HR/RR/PMD
events and records route evidence for diagnostics. The heartbeat-to-blink path
is `PolarHeartbeatButtonDriver`.

## Direct LSL route without Rusty XR broker

The Unity project can receive LSL directly. The Rusty XR broker is not needed
for this route.

Native LSL library locations in the Unity repo:

- `Assets/Plugins/LSL/Windows/x64/lsl.dll`
- `Assets/Plugins/Android/arm64-v8a/liblsl.so`

Primary class:

- `BigRedButtonDirectLslDriveReceiver`

Default receiver configuration:

- Stream name: `HRV_Biofeedback`
- Stream type: `HRV`
- Channel index: `0`
- Input mapping: normalized float in the `0..1` range
- Trigger threshold: `0.5`
- Default trigger behavior: rising-edge pulse
- Minimum trigger interval: `0.25` seconds

Runtime flow:

1. Any compatible LSL outlet publishes a float stream matching the configured
   name or type.
2. `BigRedButtonDirectLslDriveReceiver` resolves the stream and pulls samples on
   a worker thread.
3. The selected channel is normalized and compared against the trigger
   threshold.
4. Accepted threshold crossings call
   `QuestVrInputManager.TriggerButtonPressFromRuntime()`.
5. `QuestVrInputManager` plays the button press animation and increments the
   press counter.

This path drives button presses from LSL. It does not need the broker, and it
does not need a Polar sensor unless the chosen LSL outlet is itself fed by
Polar data.

Network details to verify on the target machine and headset:

- LSL discovery usually depends on local network and multicast behavior.
- Keep the headset and LSL source on a network that permits discovery.
- Android multicast/network permissions and Wi-Fi policy may matter.
- Do not assume the LSL source is the old companion app. Any outlet with the
  expected stream name/type and float channel can be used.

## Button blink and press convergence

There are two important runtime entry points:

- `QuestVrInputManager.TriggerButtonBlinkFromRuntime()` blinks the button
  without counting it as a user press.
- `QuestVrInputManager.TriggerButtonPressFromRuntime()` plays the press
  animation, updates diagnostics, and increments the press counter.

Direct Polar heartbeat uses the blink path.

Direct LSL uses the press path by default.

Blink routes converge on `BigRedButtonBlinkController.PulseOnce()` for the
visual button pulse. Button-press routes keep animation/counting separate by
default, so a press no longer creates a blink unless a caller explicitly asks
for the blink route.

## Validation checklist for a new environment

1. Clone both public repositories.
2. In the panel repo, build the panel and native caller tester with Gradle.
3. In the Unity repo, open the project with the declared Unity version or a
   compatible Unity 6 editor.
4. Build the Quest APK with
   `TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk`.
5. Install both APKs on the same Quest.
6. Launch the Unity app and verify hands/controllers input works.
7. Trigger the questionnaire panel with the debug CLI path, then repeat without
   debug auto-submit for real foreground interaction.
8. For direct Polar, accept BLE permissions, connect or scan from the HUD, and
   verify heartbeat-driven button blinks.
9. For direct LSL, start a known compatible LSL outlet, verify stream discovery,
   and verify threshold-driven button presses.
10. Save logs under repo-local ignored paths such as `Temp/` or
    `Builds/Android/`, then summarize results without committing device serials,
    participant data, screenshots, APKs, or local paths.

## What not to assume

- Do not assume Windows drive letters, Unity editor install locations, Android
  SDK paths, headset serials, Wi-Fi names, or Polar device IDs.
- Do not assume the Rusty XR broker is present.
- Do not assume direct Polar and direct LSL are both active in every test.
- Do not assume LSL is produced by a specific companion app.
- Do not assume questionnaire-panel bridge code is on `main` until the branch
  state is checked.
- Do not commit private validation artifacts to either public repo.
