# The Big Red Button Institute

## Rusty XR Relationship

- This repo is the public Unity comparison target for the Rusty XR broker and
  companion workflows.
- Start with `README.md`,
  `Documentation/Rusty-XR-Project-Integration.md`, and
  `Documentation/Rusty-XR-Broker-Unity-Compatibility.md` before changing the
  broker adapter, replay fixtures, stream parsing, or validation docs.
- Keep Unity scene behavior, Unity packages, and Quest build settings local to
  this repo. Keep Rusty XR broker schemas and reusable Rust contracts in the
  Rusty XR repo first.

## Unity / Quest Workflow

- Use Unity `6000.3.8f1` or newer compatible Unity 6 builds.
- Use the repo build entry point `TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk`.
- Run a plain batchmode import / compile pass before the APK build.
- Write Unity logs to repo-local files under `Temp\` or `Builds\Android\` and verify the log result instead of trusting the first shell return.

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
