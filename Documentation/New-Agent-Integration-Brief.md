# New Agent Integration Brief

This brief introduces the active public project shape for a new machine or CI
environment. Verify local tools, Quest availability, and Unity installation
paths before running commands.

## Public Repositories

### The Big Red Button Institute

Repository: https://github.com/MesmerPrism/the-big-red-button-institute

Role:

- Unity Quest project for the Big Red Button experience.
- Owns the Unity scene, Quest APK build, button interaction, direct Polar route,
  direct LSL route, direct OSC route, diagnostics HUD, and questionnaire caller
  bridge.
- Contains Unity DTOs/tests for the active Rusty Morphospace Manifold contract
  slice.

Important repo paths:

- `ProjectSettings/ProjectVersion.txt` declares the Unity editor version.
- `AGENTS.md` describes repo-local build rules and Unity version policy.
- `Documentation/Rusty-Morphospace-Manifold-Unity-Example.md` describes the
  active Manifold boundary.
- `Documentation/Quest-Polar-Workflow.md` describes direct Polar, LSL, and OSC
  scene setup.
- `Assets/Scenes/SampleScene.unity` is the Quest scene.
- `Assets/Editor/QuestVrApkBuilder.cs` contains the batchmode APK build entry
  point.
- `Assets/Scripts/Morphospace/Manifold/ManifoldProtocol.cs` contains Unity
  Manifold DTOs and JSON builders.
- `Assets/Scripts/Questionnaire/QuestQuestionnairePanelLauncher.cs` is the
  Unity questionnaire launch wrapper.
- `Assets/Plugins/Android/QuestionnairePanelBridge.java` is the Android bridge
  that launches the standalone questionnaire panel.

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

### Rusty Morphospace Manifold

Local active repo, on this machine:

```text
S:\Work\repos\active\rusty-manifold
```

Role:

- Owns typed command, stream, lease, authority, module, host, clock, package,
  and audit contracts.
- Provides source-only fixtures and validation routes.
- Defines the authority boundary that Unity should request through, not
  redefine.

Useful Manifold checks:

```powershell
cargo fmt --all --check
cargo test --workspace
cargo run -p rusty-manifold-fixtures -- validate
cargo run -p rusty-manifold-schema -- export --check
```

## How The Repositories Connect

The Unity app is the questionnaire caller. The native panel app is the
questionnaire callee.

The product communication pattern is:

1. The foreground Unity app launches the panel with an explicit Android intent.
2. Request metadata is passed in `extras/request_json`.
3. The Unity app provides a caller-owned `content://` result URI and grants
   write access for that URI.
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

## Manifold Boundary

The BRB Unity project currently consumes a source-only Manifold slice:

- command envelopes;
- command acknowledgements and rejections;
- stream registry snapshots;
- stream manifests;
- stream subscription requests;
- scalar sample schema IDs;
- BRB button-drive stream identifiers.

Unity-side files:

- `Assets/Scripts/Morphospace/Manifold/ManifoldProtocol.cs`
- `Assets/Tests/EditMode/Morphospace/Manifold/ManifoldProtocolTests.cs`
- `Tools/Run-ManifoldEditModeTests.ps1`

The first BRB-specific stream identity is:

```text
stream.brb.button_drive
brb.manifold.sample.button_drive.v1
```

Direct OSC defaults:

```text
/brb/manifold/drive/button
/brb/manifold/drive/ack
```

## Build Orientation

Panel repo Gradle checks:

```powershell
.\gradlew.bat :app:assembleDebug
.\gradlew.bat :examples:native-caller:assembleDebug
.\gradlew.bat :app:testDebugUnitTest
.\gradlew.bat :examples:native-caller:testDebugUnitTest
```

Unity repo checks:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Run-ManifoldEditModeTests.ps1
```

Unity APK build entry point:

```text
TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk
```

The Unity project is configured so the Quest player can use hands and
controllers. Do not regress this to controller-only input when editing player
settings.

## Minimal Questionnaire Validation Path

Prerequisites:

- Build and install the panel APK on the Quest.
- Build and install the Unity APK on the same Quest.
- Confirm both packages use the identities listed above.

CLI launch examples:

```powershell
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger initial

adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger post_condition_1

adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.questionnaireTrigger final
```

For debug smoke tests, add this boolean:

```powershell
  --ez brb.questionnaireDebugAutoSubmit true
```

Unity runtime command extras drive the 3D scene directly:

```powershell
adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.runtimeCommand press_button

adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.runtimeCommand "blink_button:6"

adb shell am start -W `
  -n org.thebigredbuttoninstitute.app/com.unity3d.player.UnityPlayerGameActivity `
  --es brb.runtimeCommandScript "center_button,blink_button:6,press_button,status" `
  --ei brb.runtimeCommandIntervalMs 700
```

## What Not To Assume

- Do not assume Windows drive letters, Unity editor install locations, Android
  SDK paths, headset serials, Wi-Fi names, or Polar device IDs.
- Do not assume a live Manifold transport exists just because source-only
  Manifold DTOs/tests exist.
- Do not commit generated APKs, screenshots, logcat bundles, participant data,
  headset serials, signing keys, or local machine paths.
- The old broker example is history on a dedicated legacy branch, not an active
  adapter in this branch.
