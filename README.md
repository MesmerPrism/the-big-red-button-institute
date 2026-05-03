# The Big Red Button Institute

Unity 6 / URP / Meta Quest prototype centered on a large red button, a VR HUD,
and Polar H10 biofeedback input.

## Current state

- Quest / OpenXR runtime is set up in `Assets/Scenes/SampleScene.unity`.
- The big red button is imported into the scene and can be centered in front of
  the viewer from VR.
- The HUD supports multiple pages, controller-driven terminal commands, and
  Polar connection / permission status.
- A small world-space counter above the button shows accepted button presses
  without opening the HUD.
- Polar H10 heartbeat can drive the button press animation when the connection
  is live.
- The Quest broker sidecar path can drive the same button press routine through
  subscribed broker stream events.
- Android APK builds are supported from `Tools > Big Red Button > Build Quest APK`.

## Docs

- `Documentation/Quest-Polar-Workflow.md`
- `Documentation/Public-Broker-Latency-Example-Roadmap.md`
