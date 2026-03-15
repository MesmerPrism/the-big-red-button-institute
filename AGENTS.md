# The Big Red Button Institute

## Repo-local Unity / Quest workflow

- Use Unity `6000.3.8f1` from `C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe`.
- Use the repo build entry point `TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk`.
- Run a plain batchmode import / compile pass before the APK build.
- Write Unity logs to repo-local files under `Temp\` or `Builds\Android\` and verify the log result instead of trusting the first shell return.

## Quest screenshot inspection workflow

- In immersive Quest sessions on this machine, direct `adb shell screencap` can return zero-byte files and `screenrecord` can fail with `INVALID_LAYER_STACK`.
- Prefer pulling real headset screenshots from `/sdcard/Oculus/Screenshots`.
- List the newest screenshots with:

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.2.7f2\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
& $adb shell ls -lt /sdcard/Oculus/Screenshots
```

- Pull the newest image into the repo for inspection with:

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.2.7f2\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
& $adb pull '/sdcard/Oculus/Screenshots/<latest-file>.jpg' 'C:\Users\tillh\source\repos\The Big Red Button Institute\Builds\Android\latest-headset-shot.jpg'
```

## Big Red Button scene note

- The animated red cap renderer is `Big Red Button/RootNode/button` and is a `SkinnedMeshRenderer`.
- The passive base renderer is `Big Red Button/RootNode/stand1`.
