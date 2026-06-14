# The Big Red Button Institute

## Morphospace / Manifold Relationship

- This repo is the Unity Quest example for the Rusty Morphospace Manifold lane.
- Start with `README.md` and
  `Documentation/Rusty-Morphospace-Manifold-Unity-Example.md` before changing
  Manifold DTOs, stream/command contract tests, runtime command flow, or
  validation docs.
- Keep Unity scene behavior, Unity packages, and Quest build settings local to
  this repo. Keep reusable command, stream, lease, authority, module, host, and
  clock contracts in the Manifold repo first.
- The old Rusty XR broker example is preserved on
  `codex/legacy-rusty-xr-broker-example`; do not reintroduce that adapter into
  this branch.

## Unity / Quest Workflow

- Use Unity `6000.3.16f1` or newer compatible Unity 6 builds. The proven local
  editor path is `S:\Work\tools\Unity\Editors\6000.3.16f1\Editor\Unity.exe`.
- Use the repo build entry point `TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk`.
- Run a plain batchmode import / compile pass before the APK build.
- Write Unity logs to repo-local files under `Temp\` or `Builds\Android\` and verify the log result instead of trusting the first shell return.
- Proven batchmode route:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Invoke-UnityBatch.ps1 `
  -UnityPath 'S:\Work\tools\Unity\Editors\6000.3.16f1\Editor\Unity.exe' `
  -ProjectPath (Get-Location).Path `
  -LogFile 'Temp\unity-import.log'

powershell -ExecutionPolicy Bypass -File .\Tools\Invoke-UnityBatch.ps1 `
  -UnityPath 'S:\Work\tools\Unity\Editors\6000.3.16f1\Editor\Unity.exe' `
  -ProjectPath (Get-Location).Path `
  -LogFile 'Temp\unity-quest-apk-build.log' `
  -ExecuteMethod 'TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk' `
  -BackgroundWaitSeconds 900
```

The APK is written to `Builds\Android\TheBigRedButtonInstitute.apk`.
- For Manifold contract tests, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Run-ManifoldEditModeTests.ps1
```

## Quest Screenshot Inspection Workflow

- In immersive Quest sessions, direct `adb shell screencap` can return zero-byte files and `screenrecord` can fail with `INVALID_LAYER_STACK`.
- Prefer pulling headset screenshots from `/sdcard/Oculus/Screenshots`.
- List the newest screenshots with:

```powershell
adb shell ls -lt /sdcard/Oculus/Screenshots
```

- Pull the newest image into the repo for inspection with:

```powershell
adb pull /sdcard/Oculus/Screenshots/<latest-file>.jpg Builds/Android/latest-headset-shot.jpg
```

## Big Red Button Scene Note

- The animated red cap renderer is `Big Red Button/RootNode/button` and is a `SkinnedMeshRenderer`.
- The passive base renderer is `Big Red Button/RootNode/stand1`.
