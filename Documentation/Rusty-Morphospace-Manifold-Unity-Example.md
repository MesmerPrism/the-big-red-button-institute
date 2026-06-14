# Rusty Morphospace Manifold Unity Example

Status: active integration note for the Big Red Button Unity project.

## Purpose

The Big Red Button Institute is a small Quest scene that makes Manifold
contracts tangible. The red button, HUD, questionnaire launcher, direct Polar
route, direct LSL route, and direct OSC route stay inside Unity. Manifold owns
the typed authority vocabulary around commands, stream descriptors,
subscriptions, leases, acknowledgements, rejections, modules, hosts, clocks,
and audit evidence.

The legacy Rusty XR broker version is preserved on
`codex/legacy-rusty-xr-broker-example`. This branch should not carry that
adapter code.

## Ownership Boundary

This Unity project owns:

- visible scene meaning and button behavior;
- Quest packages, player settings, and APK build entry points;
- runtime command extras for scene-level validation;
- direct Unity OSC, LSL, and Polar input routes;
- questionnaire-panel launch integration;
- Unity DTOs and edit-mode tests that prove Manifold JSON shapes are consumable.

Manifold owns:

- source-of-truth schemas and Rust model types;
- command descriptors and command envelopes;
- stream registry snapshots and subscription requests;
- lease, capability, precondition, safety, acknowledgement, and rejection rules;
- source-only authority review and dispatch receipts;
- fixtures and validation CLI routes.

Unity should request, subscribe, display, or adapt. It should not become the
authority that accepts mutable Manifold state.

## Implemented Unity Contract Slice

`Assets/Scripts/Morphospace/Manifold/ManifoldProtocol.cs` currently covers:

- `rusty.manifold.command.envelope.v1`
- `rusty.manifold.command.ack.v1`
- `rusty.manifold.command.rejection.v1`
- `rusty.manifold.stream.registry_snapshot.v1`
- `rusty.manifold.stream.manifest.v1`
- `rusty.manifold.stream.subscription_request.v1`
- `rusty.manifold.stream.subscription.v1`
- `rusty.manifold.stream.subscription_rejection.v1`
- `rusty.manifold.sample.scalar_f32.v1`

The first BRB-specific stream identity is:

```text
stream.brb.button_drive
brb.manifold.sample.button_drive.v1
```

Direct OSC diagnostics use these public addresses:

```text
/brb/manifold/drive/button
/brb/manifold/drive/ack
```

## Validation Pair

Manifold side, from the Manifold repo:

```powershell
cargo fmt --all --check
cargo test --workspace
cargo run -p rusty-manifold-fixtures -- validate
cargo run -p rusty-manifold-schema -- export --check
```

Unity side, from this repo:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Run-ManifoldEditModeTests.ps1
```

Quest APK build entry point:

```text
TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk
```

Use the repo helper instead of hand-writing Unity arguments:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Invoke-UnityBatch.ps1 `
  -UnityPath 'S:\Work\tools\Unity\Editors\6000.3.16f1\Editor\Unity.exe' `
  -ProjectPath (Get-Location).Path `
  -LogFile 'Temp\unity-import.log'

powershell -ExecutionPolicy Bypass -File .\Tools\Invoke-UnityBatch.ps1 `
  -UnityPath 'S:\Work\tools\Unity\Editors\6000.3.16f1\Editor\Unity.exe' `
  -ProjectPath (Get-Location).Path `
  -LogFile 'Temp\unity-quest-apk-build.log' `
  -ExecuteMethod 'TheBigRedButtonInstitute.Editor.QuestVrApkBuilder.InstallSceneAndBuildApk' `
  -BackgroundWaitSeconds 900
```

The APK output is `Builds\Android\TheBigRedButtonInstitute.apk`.

## Migration Notes

- Do not reintroduce legacy broker scripts, command names, serialized scene
  fields, test runners, or docs into this branch.
- If a live Manifold transport appears later, add it as a Manifold adapter with
  explicit platform APIs, process boundary, schema IDs, rate class, queue
  bounds, failure modes, and fixtures.
- Keep high-rate sample payloads on a data plane. Low-rate Unity command JSON
  should carry requests, subscriptions, acknowledgements, rejections, and
  metadata.
- Prefer adding fixture-backed DTO/tests before wiring live transport behavior.
