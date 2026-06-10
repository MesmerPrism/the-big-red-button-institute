# Third-Party Notices

This repository includes third-party assets used by the Unity example scene.

## Big Red Button Model

- File: `Assets/Models/BigRedButton.glb`
- Title: Big Red Button
- Creator: Charlie
- Source: https://poly.pizza/m/lTyNXwkDgX
- License: Creative Commons Attribution 3.0 Unported (CC BY 3.0)
- License URL: https://creativecommons.org/licenses/by/3.0/

The Unity project uses the model as the visible button in the Quest example
scene. The repository adds Unity scene setup, materials, colliders, runtime
press behavior, and diagnostics around the model.

## Lab Streaming Layer liblsl

- Files:
  - `Assets/Plugins/LSL/Windows/x64/lsl.dll`
  - `Assets/Plugins/Android/arm64-v8a/liblsl.so`
- Project: Lab Streaming Layer core library (`liblsl`)
- Source: https://github.com/sccn/liblsl
- License: MIT; liblsl also uses Boost Software License-covered code.

The Unity project uses these native libraries for the optional direct Unity
LSL diagnostic receiver. The BRB receiver resolves the companion test stream
by name/type and consumes a single normalized float channel for transport
latency comparison against broker-managed LSL routing.
