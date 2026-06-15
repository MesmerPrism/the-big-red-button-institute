# Quest Runtime Layout Config

The Quest player can load button layout values from editable JSON files under
Unity's `Application.persistentDataPath`.

On Quest, this is expected to resolve to:

```text
/sdcard/Android/data/org.thebigredbuttoninstitute.app/files
```

At runtime the app seeds this file if it is missing:

```text
runtime_config/brb_layout_defaults.json
```

The optional active override is:

```text
runtime_config/brb_layout_runtime.json
```

The app loads defaults first, then applies the runtime override if present.
This lets the installed APK keep a user-editable default and a separate active
test override.

Example JSON:

```json
{
  "schema": "brb.runtime_layout.v1",
  "counter_canvas_local_y_m": 0.0315,
  "button_height_m": 0.36,
  "button_distance_from_head_m": 0.48,
  "button_vertical_offset_from_head_m": -0.32,
  "minimum_button_world_y_m": 0.54,
  "use_absolute_button_world_y": false,
  "absolute_button_world_y_m": 1.33,
  "place_button_on_startup": true,
  "keep_button_in_front_of_head": true
}
```

Useful ADB commands:

```powershell
$adb = 'S:\Work\tools\Android\windows-sdk\platform-tools\adb.exe'
$pkg = 'org.thebigredbuttoninstitute.app'
$root = "/sdcard/Android/data/$pkg/files"

& $adb shell mkdir -p "$root/runtime_config"
& $adb pull "$root/runtime_config/brb_layout_defaults.json" .\brb_layout_defaults.json
& $adb push .\brb_layout_runtime.json "$root/runtime_config/brb_layout_runtime.json"
& $adb shell am start -W -n "$pkg/com.unity3d.player.UnityPlayerGameActivity" `
  -a android.intent.action.MAIN `
  -c android.intent.category.LAUNCHER `
  -c com.oculus.intent.category.VR `
  --es brb.runtimeCommand reload_layout
```

Use `layout_status` to print the active values and paths:

```powershell
& $adb shell am start -W -n "$pkg/com.unity3d.player.UnityPlayerGameActivity" `
  -a android.intent.action.MAIN `
  -c android.intent.category.LAUNCHER `
  -c com.oculus.intent.category.VR `
  --es brb.runtimeCommand layout_status
```
