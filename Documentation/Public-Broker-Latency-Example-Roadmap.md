# Public Broker Latency Example Roadmap

This project is being shaped into a public Quest/Unity example for comparing
direct app-owned data ingestion against the same data routed through a general
purpose broker sidecar.

## Current Runtime Shape

- The scene owns a physical Big Red Button model with press-surface and base
  colliders.
- Hand and controller-driven hand interaction can press the button through
  generated mesh colliders.
- A world-space press counter sits above the button so headset validation can
  confirm accepted presses without opening the diagnostic HUD.
- The diagnostic HUD remains available for permissions, signals, broker status,
  terminal commands, and input inspection.
- The Polar H10/BLE graph is scene-authored and drives button presses through
  the same runtime press path used by manual and broker events.
- The broker client can connect to the Quest broker sidecar, subscribe to drive
  streams, open the 2D broker console, and request that the broker console
  returns to the background.

## Comparison Goal

The public example should make each transport path explicit:

| Path | Status | Purpose |
| --- | --- | --- |
| Manual hand/controller press | Present | Baseline interaction and visual counter validation. |
| Direct Unity BLE/Polar | Present | App-owned sensor ingestion and button drive. |
| Direct Unity LSL | Planned | App-owned LSL stream path for latency comparison. |
| Direct Unity OSC | Planned | App-owned OSC path for creative-tool and diagnostics comparison. |
| Broker WebSocket drive | Present | Broker-mediated stream drive into the Unity app. |
| Broker LSL/OSC forwarding | Broker-side present | Sidecar-mediated latency and stream diagnostics. |

All accepted input paths should converge on the same button press routine so
latency counters, animation, visual feedback, and logs stay comparable.

## Validation Loop

Use the companion/broker tooling to send deterministic test streams, then verify
the Unity side through:

- the visible press counter above the button
- HUD `Press count`
- Unity logcat messages
- broker status and stream counters
- companion-side stream send/latency logs

ADB keyboard input is useful for app command smoke tests. Physical controller
testing is still required for Meta controller bindings because Android
synthetic gamepad keys do not fully emulate the OVRInput/OpenXR controller
path.

## Publicization Checklist

- Keep package names, signing material, generated APKs, and machine-local paths
  out of public docs.
- Keep adapter dependencies explicit and optional where possible.
- Add tests around broker protocol parsing, Unity-side stream routing, and
  press-counter behavior before widening the public API.
- Provide one complete Unity validation route and one complete Rust/Rusty XR
  route that can both consume the same broker test stream.
