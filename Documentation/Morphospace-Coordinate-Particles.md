# Morphospace Coordinate Particles

Status: first BRB port slice from the Astral Karate Dojo indirect particle
renderer.

## Purpose

The BRB coordinate particle runtime renders many particles at explicit
coordinate points. Its first target is the number-mesh coordinate packages
produced by Rusty Matter Mesh Lab: one particle per sampled mesh coordinate,
with optional use of the coordinate normal for color and normal offset.

This is a BRB-owned Unity runtime surface. It consumes coordinate artifacts and
renders scene-local particles. It does not own mesh extraction, source
provenance, Manifold command authority, or Matter coordinate-map generation.

## Source Reference

The implementation borrows the indirect draw shape from the local Astral
Karate Dojo reference project:

```text
S:\Work\repos\reference\AstralKarateDojo
```

Relevant source family:

```text
Assets\Scripts\IndirectParticles\ParticleEngine
Assets\Shaders\IndirectParticles
```

The reference repo has no license file in place at the time of this port. This
BRB slice is therefore a narrow adaptation of the rendering pattern and GPU
payload shape, not a wholesale copy of the full oscillator engine.

## Implemented Slice

Runtime scripts:

```text
Assets\Scripts\TheBigRedButtonInstitute\IndirectParticles\ParticleEngine\BrbIndirectCoordinateParticleSystem.cs
Assets\Scripts\TheBigRedButtonInstitute\IndirectParticles\ParticleEngine\BigRedButtonParticlePressCounterDisplay.cs
Assets\Scripts\TheBigRedButtonInstitute\IndirectParticles\ParticleEngine\BrbParticleCoordinateSet.cs
```

Shader:

```text
Assets\Resources\Shaders\IndirectParticles\BRB_IndirectCoordinateBillboard.shader
```

Default particle texture:

```text
Assets\Resources\Textures\IndirectParticles\BRB_DiffuseFeatherDot.png
```

`BrbIndirectCoordinateParticleSystem` uses the same core GPU particle payload
shape as the Astral renderer:

```text
positionWS
size
color
rotation
frame
aux0
aux1
```

It renders a camera-facing quad per particle through `Graphics.RenderMeshIndirect`
and a `StructuredBuffer<ParticleGPU>`. The shader keeps the Astral lesson that
indirect instance IDs should not be routed through Unity instancing macros.

## Coordinate Inputs

The component can consume:

- a `BrbParticleCoordinateSet` ScriptableObject;
- a Matter Mesh Lab-style coordinate-map package JSON in a `TextAsset`;
- a coordinate-map JSON with `samples.samples`;
- a simple JSON cloud with `points` or `coordinates`;
- direct runtime calls to `SetCoordinatePoints(...)`;
- a deterministic procedural fallback cloud for smoke testing before real
  coordinate packages exist.

The button press counter now has a runtime bridge that can replace the
world-space TMP count with particle digits. `BigRedButtonWorldPressCounter`
creates `BigRedButtonParticlePressCounterDisplay` at play time, parents the
particle digits under the existing `CountText` transform, and keeps the old
text transform, position, scale, camera-facing behavior, color pulse, and Polar
blink timing as the authority. Until digit coordinate assets are assigned, the
display uses static procedural digit coordinates so the scene can be tested
without editing `Assets\Scenes\SampleScene.unity`.

The existing counter component now exposes a `particleDigitCoordinateSets`
array for digit assets `0..9`. Assign imported number coordinate sets there
when the Matter Mesh Lab number packages are available; the runtime display
normalizes each digit asset into the current `CountText` box.

Expected Matter-style package shape:

```json
{
  "coordinate_map": {
    "samples": {
      "samples": [
        {
          "position": { "x": 0.0, "y": 0.0, "z": 0.0 },
          "normal": { "x": 0.0, "y": 1.0, "z": 0.0 }
        }
      ]
    }
  }
}
```

Simple cloud shape:

```json
{
  "points": [
    { "x": 0.0, "y": 0.0, "z": 0.0, "nx": 0.0, "ny": 1.0, "nz": 0.0 }
  ]
}
```

## Non-Scope

This port intentionally excludes:

- Kuramoto phase coupling;
- neighbor tiers;
- small-world edges;
- oscillator config assets;
- biofeedback runtime drivers;
- integrated tracer ring buffers;
- baked animation texture arrays;
- scene installation or modification.

Those can be added later as explicit BRB or Morphospace needs, but the number
mesh use case only needs coordinate placement and rendering.

## Next Slice

Once Rusty Matter Mesh Lab emits the number mesh coordinate packages, add a
small importer/editor utility that turns each digit package into a
`BrbParticleCoordinateSet` asset or assigns the package JSON as a `TextAsset`.

Do not wire this into `Assets\Scenes\SampleScene.unity` until the current scene
layout work is stable.

## 2026-06-15 Implementation Notes

- Located the Astral source at `S:\Work\repos\reference\AstralKarateDojo`.
- Ported the indirect coordinate rendering slice into BRB-owned scripts and a
  BRB-owned shader.
- Verified the new C# scripts by compiling them directly against the Unity
  6000.3 reference assemblies. The only warnings were expected serialized-field
  warnings from compiling Unity/JsonUtility fields outside the editor.
- Verified whitespace with `git diff --check` for tracked edits and an
  explicit trailing-whitespace scan for the new particle files, shader, and
  documentation. The full repo check is still blocked by trailing whitespace in
  the already dirty `Assets\Scenes\SampleScene.unity` layout edit.
- Did not run Unity batchmode or modify the scene in this slice.

## 2026-06-15 PlayMode Evidence

Added an Editor/PlayMode evidence test:

```text
Assets\Editor\BigRedButtonParticleCounterPlayModeEvidence.cs
```

Run filter:

```text
TheBigRedButtonInstitute.Editor.BigRedButtonParticleCounterPlayModeEvidence.ParticleCounterRendersInPlayMode
```

The test creates a throwaway world-space counter, renders particle-only digits,
and captures local particle-size variants plus morph-transition evidence:

```text
Builds\PlayModeEvidence\particle-size-3.png
Builds\PlayModeEvidence\particle-size-5.png
Builds\PlayModeEvidence\particle-size-7.png
Builds\PlayModeEvidence\particle-morph-mid-123-to-456.png
Builds\PlayModeEvidence\particle-morph-final-456.png
Builds\PlayModeEvidence\particle-morph-mid-9-to-10.png
Builds\PlayModeEvidence\particle-morph-final-10.png
Builds\PlayModeEvidence\particle-counter-playmode-summary.json
```

Unity 6000.3.16f1 batchmode passed the test on 2026-06-15. The final morph
evidence run rendered size variants with lit-pixel counts of `7358` for size
`3`, `9951` for size `5`, and `10963` for size `7`. The same run captured
`123 -> 456` mid/final frames and `9 -> 10` mid/final frames. The default local
particle digit size remains `5`: size `3` is precise but thin, size `7` is
readable but heavy, and size `5` is the best current default for the BRB
world-space counter scale.

Digit particles now morph when the counter changes. Each digit place owns a
separate `BrbIndirectCoordinateParticleSystem`: renderer slot `0` is the ones
place, slot `1` is tens, and so on. This means `9 -> 10` keeps the old ones
particle cloud moving into the new `0` coordinate target while a separate tens
cloud displays `1`. Digit coordinate positions include their whole-number
layout offsets, so particles move smoothly when the number width changes.

`BigRedButtonWorldPressCounter` now owns the scene-facing particle digit tuning
surface:

- `particleDigitSize`
- `particleDigitRadialClip`
- `particleDigitSpacing`
- `particleDigitCoordinateFill`
- `particleDigitUseTexture`
- `particleDigitTexture`
- `particleDigitTextureUsesLuminanceAlpha`
- `particleDigitMorphChanges`
- `particleDigitMorphDuration`

These values pass through to the runtime-created
`BigRedButtonParticlePressCounterDisplay`, so the BRB scene can tune particle
thickness, spacing, texture mode, and morph timing without manually installing
the implementation component.

The default generated particle texture is a 2048 px RGBA feather dot. RGB is
white and the radial brightness falloff is stored in alpha, so tinting the
particles does not inherit dark edges. The shader also has a luminance-as-alpha
mode for grayscale source textures where black should become transparent.

Alpha preview for the generated texture:

```text
Builds\PlayModeEvidence\BRB_DiffuseFeatherDot_alpha_preview_on_black.png
```
