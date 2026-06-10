# Rusty XR Project Integration

Status: public relationship note for this Unity example and the Rusty XR
repository family.

## Role

The Big Red Button Institute is the public Unity comparison target for Rusty XR
broker and companion workflows. It keeps a small, inspectable Quest scene where
direct Unity inputs and Rusty XR broker-routed inputs drive the same visible
button behavior.

Use this project when the question is:

- can Unity consume the current public Rusty XR broker stream shape?
- can Unity consume Rusty XR replay-record-shaped fixtures without a headset?
- can broker-routed OSC or synthetic stream events be compared against direct
  Unity input paths?
- can a visible Quest scene make latency and routing differences easy to
  inspect?

## Ownership Boundary

This Unity project owns:

- scene meaning and visible button behavior
- Unity packages, project settings, and Quest build settings
- Unity edit-mode tests for the adapter
- direct Unity OSC/BLE input paths
- local Unity diagnostics and scene-facing UX

Rusty XR owns:

- public Rust contracts and schemas
- broker and companion-facing stream/replay shapes
- public broker, Quest, and companion examples
- catalog validation and device/operator diagnostics
- public source boundaries for Quest camera, tracking, media, and launch tools

Neither side should silently redefine the other side's contract. If a broker
schema changes, update Rusty XR first, then update this Unity adapter and its
edit-mode tests.

## Expected Sibling Layout

When both repositories are checked out together for validation, keep them as
siblings:

```text
<workspace>\Rusty-XR
<workspace>\the-big-red-button-institute
```

Repository docs and scripts should still use repository-relative paths and
public GitHub links. Do not commit local drive letters, headset serials, local
IP addresses, generated diagnostics, screenshots, or APK outputs.

## Validation Pair

Rusty XR side:

```powershell
python tools\schema\check_quest_app_catalog.py tools\schema\fixtures\quest-app-catalog.example.json
cargo test -p rusty-xr-broker-model -p rusty-xr-osc --features serde
```

Unity side:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Run-BrokerEditModeTests.ps1
```

Live Quest comparisons should launch the Rusty XR broker example through the
companion workflow, then use this Unity scene as the visible target.

## Counterpart Docs

- Rusty XR:
  <https://github.com/MesmerPrism/Rusty-XR>
- Rusty XR Unity example integration:
  <https://github.com/MesmerPrism/Rusty-XR/blob/main/docs/UNITY_EXAMPLE_INTEGRATION.md>
- Rusty XR Unity broker adapter contract:
  <https://github.com/MesmerPrism/Rusty-XR/blob/main/docs/UNITY_BROKER_ADAPTER_CONTRACT.md>
- Rusty XR companion apps:
  <https://github.com/MesmerPrism/Rusty-XR-Companion-Apps>
