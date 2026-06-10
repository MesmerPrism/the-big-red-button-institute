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
- The broker adapter accepts both the current Quest broker app's top-level
  `stream_event` fields and the newer public Rusty XR contract shape with a
  nested stream sample header.
- The broker adapter also consumes synthetic `eye.screen.gaze_point` events and
  can visualize normalized screen gaze in the Unity scene, keeping real
  eye-tracker providers outside this example.
- A comparison-route controller records per-route sample counts, accepted
  button pulses, sequence gaps, duplicate/out-of-order packets, and available
  latency timestamps.
- A direct Unity OSC receiver listens on UDP `9001` for
  `/rusty-xr/drive/radius` so the same companion OSC utility can exercise a
  direct app-owned route and the broker-routed OSC route on different ports.
  When the companion includes a host send timestamp and reply port, Unity sends
  `/rusty-xr/drive/ack` acknowledgements with receive/send timestamps for
  round-trip and clock-alignment diagnostics.
- A direct Unity LSL receiver resolves the companion `HRV_Biofeedback / HRV`
  one-channel normalized float stream, pulls samples on a worker thread, and
  drives the same button routine used by direct OSC and broker-routed streams.
- Direct Unity Polar diagnostics subscribe to the existing `PolarUnifiedModule`
  events and record standard HR/RR notifications separately from decoded PMD
  ACC/ECG frame receipt.
- The broker client subscribes to `bio:polar_hr_rr`, `bio:polar_acc`,
  `bio:polar_ecg`, and `bio:breath`; a broker bio receiver records Gargoyle
  stream timing and counts without taking ownership of Unity's direct BLE graph.

## Comparison Goal

The public example should make each transport path explicit:

| Path | Status | Purpose |
| --- | --- | --- |
| Manual hand/controller press | Present | Baseline interaction and visual counter validation. |
| Direct Unity BLE/Polar | Present | App-owned sensor ingestion and button drive. |
| Direct Unity Polar PMD | Present | App-owned PMD ACC/ECG frame receipt diagnostics. |
| Direct Unity LSL | Present | App-owned LSL stream path for latency comparison. |
| Direct Unity OSC | Present with ack timing | App-owned OSC path for creative-tool and diagnostics comparison. |
| Broker WebSocket drive | Present | Broker-mediated stream drive into the Unity app. |
| Broker Polar/Breath WebSocket | Present | Gargoyle-managed Polar HR/RR, PMD, and breath-assessment diagnostics. |
| Broker synthetic eye stream | Present | Provider-neutral Unity consumer for `eye.screen.gaze_point` samples. |
| Broker LSL/OSC forwarding | Present and validated with the companion diagnostics | Sidecar-mediated latency and stream diagnostics. |

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

Current no-hardware OSC comparison:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker compare --quest-host <quest-lan-ip> --serial <serial> --count 16 --interval-ms 250 --out .\artifacts\broker-compare --json
```

The direct Unity OSC route uses port `9001`. The broker OSC ingress profile uses
port `9000`, then publishes a broker `stream_event` to subscribed WebSocket
clients. The Unity Android manifest declares network socket permissions so this
direct LAN OSC route works on Quest builds.

The direct route has been validated with companion-generated acknowledgements:
Unity echoes host send time, Unity receive time, Unity acknowledgement send time,
value, sequence, and accepted-pulse state. The companion report uses the
four-timestamp exchange to estimate target-minus-host clock offset and writes
JSON, Markdown, and CSV artifacts. The broker route is implemented in the same
runner and has been validated on headset through the Rusty XR broker service,
with the companion recording direct Unity OSC acknowledgements and broker
stream-event counts in one comparison bundle.

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
- Keep the repository private until the public branch contains the full
  comparison example, public-safe docs, and validation evidence on `main`.
