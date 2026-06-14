# The Big Red Button Institute

Unity 6 / URP / Meta Quest example centered on a large red button, a VR HUD,
split-app questionnaire validation, and Rusty Morphospace Manifold contracts.

This branch is the active Unity example for showing how a Quest app can keep
scene behavior local while treating Morphospace Manifold as the authority for
typed commands, stream descriptors, leases, acknowledgements, and safe
rejections. The previous Rusty XR broker example is parked on
`codex/legacy-rusty-xr-broker-example`.

## Current State

- Quest / OpenXR runtime is set up in `Assets/Scenes/SampleScene.unity`.
- The big red button can be centered in front of the viewer from VR.
- The HUD supports dashboard, permissions, signals, terminal, and input pages.
- A world-space counter above the button shows accepted button presses without
  opening the HUD.
- Runtime commands can center the button, play the imported press animation,
  increment the counter, and run a timed heartbeat-style blink.
- The standalone Quest questionnaire panel can be launched for initial,
  post-condition, and final sequences through the Android bridge.
- Direct Unity OSC, LSL, and Polar/BLE diagnostics remain available for
  app-owned input checks.
- `Assets/Scripts/Morphospace/Manifold/` contains Unity DTOs and builders for
  Manifold command and stream-subscription contracts.
- Android APK builds are supported from `Tools > Big Red Button > Build Quest APK`.

## Role In Morphospace

Use this repo when you want a Unity-side Quest target for:

- checking Manifold command envelope, acknowledgement, rejection, stream
  registry, and stream subscription JSON shapes from Unity;
- driving visible Quest scene behavior from local Unity inputs while keeping
  command authority outside the scene;
- testing direct Unity OSC, LSL, and Polar/BLE ingestion as app-owned routes;
- integrating the Quest Questionnaire Panel as a separate native Android panel;
- proving the same button press and blink paths can be reached from controllers,
  runtime command extras, Polar heartbeat, LSL, OSC, and questionnaire flow.

The matching Manifold source lives in the Rusty Morphospace family, with the
local active Manifold repo normally checked out at:

```text
S:\Work\repos\active\rusty-manifold
```

Manifold owns source-of-truth contracts and authority rules. This Unity project
owns scene meaning, visible button behavior, Quest build settings, and the
Unity-side adapter DTOs that consume those contracts.

## Validation

Run the Unity edit-mode Manifold tests:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Run-ManifoldEditModeTests.ps1
```

Build the Quest APK from Unity or batch mode through:

```text
TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk
```

Proven local batchmode command:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Invoke-UnityBatch.ps1 `
  -UnityPath 'S:\Work\tools\Unity\Editors\6000.3.16f1\Editor\Unity.exe' `
  -ProjectPath (Get-Location).Path `
  -LogFile 'Temp\unity-quest-apk-build.log' `
  -ExecuteMethod 'TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk' `
  -BackgroundWaitSeconds 900
```

The APK output is `Builds\Android\TheBigRedButtonInstitute.apk`.

Useful local checks before commit:

```powershell
git diff --check
```

## Docs

- `Documentation/Rusty-Morphospace-Manifold-Unity-Example.md`
- `Documentation/New-Agent-Integration-Brief.md`
- `Documentation/Quest-Polar-Workflow.md`
- `Documentation/Quest-Questionnaire-Panel-Integration.md`
- `THIRD_PARTY_NOTICES.md`

## License

This project is distributed under the MIT License. The BRB study audio assets
were generated with ElevenLabs for this project; confirm the generating account
and subscription terms before publishing release APKs or asset bundles.
