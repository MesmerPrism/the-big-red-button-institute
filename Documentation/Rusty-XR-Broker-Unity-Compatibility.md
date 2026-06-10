# Rusty XR Broker Unity Compatibility

Status: public implementation note for the canonical Unity example.

For the broader repository relationship and ownership boundary, see
`Documentation/Rusty-XR-Project-Integration.md`.

## Purpose

This project demonstrates that a Unity scene can consume Rusty XR broker-routed
stream events without owning the broker runtime. The scene keeps experiment and
button behavior local, while the broker owns stream routing, timing metadata,
drop visibility, replay shape, and sidecar diagnostics.

## Current Adapter Shape

Implemented Unity-side:

- WebSocket client connection to the Rusty XR Quest broker sidecar.
- `client_hello` schema advertisement while retaining the broker app's
  expected `type: "hello"` compatibility field.
- command envelopes for status, stream listing, subscribe/unsubscribe, and
  broker console open/close.
- stream-event parsing for the current broker app shape:
  - top-level `sequence_id`
  - top-level broker timestamps
  - OSC drive payload with `/rusty-xr/drive/radius`
- stream-event parsing for the public Rusty XR contract shape:
  - nested `header`
  - `sequence_number`
  - `payload_schema`
  - source and broker timestamps
  - drop/late counters
- replay-record parsing for the public Rusty XR JSONL fixture shape:
  - `rusty.xr.broker.replay_record.v1`
  - normalization into the same Unity stream-event object used by live broker
    messages
  - edit-mode coverage for synthetic wave and blink/dropout screen-gaze
    records
- synthetic screen-gaze stream parsing:
  - `eye.screen.gaze_point`
  - normalized `x/y`
  - sample validity, confidence, provider, and source-device metadata
- routing of compatible `value01` payloads into the same button-drive path used
  by direct Unity inputs.
- optional world-space marker visualization for broker-routed screen gaze.
- direct Unity OSC and LSL diagnostic receivers for comparing app-owned
  ingestion against broker-managed forwarding.
- broker bio stream reception for `bio:polar_hr_rr`, `bio:polar_acc`,
  `bio:polar_ecg`, and `bio:breath`, including timing/count diagnostics for
  Gargoyle-managed Polar sources.
- terminal commands for broker-side Polar source control:
  `broker_polar_hr_start`, `broker_polar_pmd_start`, and `broker_polar_stop`.

## Why Two Stream Shapes Are Accepted

The installed Quest broker app already publishes legacy top-level
`stream_event` fields. The Rusty XR public contract crate now defines a richer
`BrokerStreamEvent` with a nested `BrokerStreamSampleHeader`.

The Unity adapter normalizes both shapes into one runtime event object so the
example remains compatible with current broker APKs while also proving the
new public contract is consumable by Unity.

## What Is Not Implemented Here

- no Unity-owned broker runtime
- no native eye-tracker SDK
- no EDIA/UXF trial semantics
- no proprietary provider data forwarding

Direct OSC and direct LSL are intentionally diagnostic app-owned routes. The
broker adapter remains the reusable route for sidecar-managed stream events,
timing metadata, and stream identity.

Direct Polar BLE/PMD and broker-managed Polar BLE/PMD can both be visible in
the diagnostic table, but a single Polar H10 PMD stream should still be treated
as single-owner during live tests. Use one path as the PMD owner unless the
test deliberately uses separate sensors or a non-PMD HR/RR-only split.

The eye-stream path is intentionally synthetic/provider-neutral. It proves that
Unity can consume the public eye-data contract and visualize broker-routed gaze
without adding a Tobii, headset, or EDIA-specific provider to this project.

## Desktop Fixture Coverage

The edit-mode broker tests include fixture-equivalent replay-record JSON for:

- `synthetic:wave`
- `eye.screen.gaze_point`

Those records mirror the public Rusty XR replay fixture shape so Unity can
validate replay parsing without a headset, broker runtime, native tracker SDK,
or play-mode scene run. Live stream events and replay records intentionally
normalize into the same in-memory event object before reaching receivers.

## Maintainer-Relevant Boundary

For EDIA-style collaboration, this Unity project should stay a thin adapter:
it should subscribe to broker streams, route generic values to local scene
events, and publish local markers later if needed. It should not move EDIA
experiment structure, UXF trial semantics, or Unity scene meaning into the
broker.
