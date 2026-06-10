# The Big Red Button Institute

Unity 6 / URP / Meta Quest example centered on a large red button, a VR HUD,
and stream-driven input diagnostics.

This repository is the public Unity example for the
[Rusty XR](https://github.com/MesmerPrism/Rusty-XR) broker and companion-app
workflow. It provides a small Quest scene where direct Unity-owned inputs and
Rusty XR broker-routed inputs drive the same visible button, making latency and
stream-quality comparisons easy to inspect on-headset.

## Current state

- Quest / OpenXR runtime is set up in `Assets/Scenes/SampleScene.unity`.
- The big red button is imported into the scene and can be centered in front of
  the viewer from VR.
- The HUD supports multiple pages, controller-driven terminal commands, input
  status, broker status, and Polar connection / permission status.
- A small world-space counter above the button shows accepted button presses
  without opening the HUD.
- Direct Unity OSC and direct Unity Polar/BLE input can drive the button press
  animation.
- The Rusty XR Quest broker sidecar can drive the same button press routine
  through subscribed broker stream events.
- The diagnostic route table now records direct Unity Polar HR/RR and PMD frame
  receipt separately from broker-routed `bio:polar_hr_rr`, `bio:polar_acc`,
  `bio:polar_ecg`, and `bio:breath` stream events.
- The broker adapter can also subscribe to synthetic `eye.screen.gaze_point`
  events and map normalized screen gaze onto an in-scene marker without a
  device-specific eye tracker SDK.
- The broker edit-mode tests consume replay-record-shaped synthetic wave and
  screen-gaze fixture payloads, so the adapter can be checked without a
  headset or live broker.
- The Rusty XR Companion CLI can send deterministic OSC, broker, LSL, and
  Polar-shaped diagnostic streams and write JSON/CSV/Markdown/PDF reports.
- Android APK builds are supported from `Tools > Big Red Button > Build Quest APK`.

## Role in Rusty XR

Use this repo when you want a complete Unity-side Quest target for comparing:

- direct Unity OSC ingestion
- direct Unity LSL ingestion
- direct Unity Polar-compatible BLE ingestion
- direct Unity Polar-compatible PMD frame receipt
- broker-routed WebSocket stream events
- broker-routed Polar HR/RR, PMD, and breath assessment streams
- broker-routed synthetic screen gaze events
- broker-side OSC and LSL forwarding driven by the companion tools

The matching Rusty XR components live in:

- [Rusty XR](https://github.com/MesmerPrism/Rusty-XR), which owns the public
  broker app source, schemas, Rust contracts, and Rust examples.
- [Rusty XR Companion Apps](https://github.com/MesmerPrism/Rusty-XR-Companion-Apps),
  which owns the Windows CLI/app used for Quest install, launch, stream
  generation, and diagnostics output.

Direct Unity LSL reception resolves the companion `HRV_Biofeedback / HRV`
test stream and drives the same button path used by the broker-managed LSL
route, so both LSL routes can be compared in one scene.

The bidirectional repository boundary is documented in
`Documentation/Rusty-XR-Project-Integration.md`. The Rusty XR counterpart is
`docs/UNITY_EXAMPLE_INTEGRATION.md` in the Rusty XR repository.

## Validation

Run the Unity edit-mode broker tests:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Run-BrokerEditModeTests.ps1
```

Build the Quest APK from Unity or batch mode through:

```text
TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk
```

With the Rusty XR Quest broker installed and a headset on the same LAN, the
companion can compare direct Unity OSC against broker-routed OSC/WebSocket:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker compare --quest-host <quest-lan-ip> --serial <adb-serial> --count 16 --interval-ms 250 --out .\artifacts\broker-compare --json
```

## Docs

- `THIRD_PARTY_NOTICES.md`
- `Documentation/Quest-Polar-Workflow.md`
- `Documentation/Public-Broker-Latency-Example-Roadmap.md`
- `Documentation/Rusty-XR-Project-Integration.md`
- `Documentation/Rusty-XR-Broker-Unity-Compatibility.md`
