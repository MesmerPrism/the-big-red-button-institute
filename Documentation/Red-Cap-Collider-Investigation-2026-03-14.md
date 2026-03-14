# Red Cap Collider Investigation

Date: March 14, 2026

## Goal

Wrap a proper dynamic collider shell around the animated red cap at `Big Red Button/RootNode/button` on Quest, similar to the visible shells already working for:

- the hand collider
- the passive base at `Big Red Button/RootNode/stand1`

The user-visible requirement is not only press interaction but also a debug shell in-headset that clearly matches the red cap itself.

## Current Baseline In This Commit

The currently working baseline in this commit is:

- the hand shell is visible
- the base shell is visible
- the fallback press disc is visible again
- the fallback press disc can drive the animation without depending on the cap mesh collider
- the disc currently uses a simple moving horizontal band based on the cap renderer bounds

This baseline is intentionally conservative. It is the last version that restored working Quest interaction after later pose-fitting experiments regressed the disc completely.

## Scene / Model Facts Confirmed

- The red cap renderer is a `SkinnedMeshRenderer` at `Big Red Button/RootNode/button`.
- The base is a `MeshRenderer` at `Big Red Button/RootNode/stand1`.
- The red cap is not just a blink effect. It is a real skinned mesh driven by a legacy `Animation` clip named `pressed`.
- The cap mesh uses one bone, `Big Red Button/RootNode/joint1`.
- The authored scene keeps both:
  - a generated mesh collider path on the cap
  - a separate `Button Trigger Surface` fallback object

Relevant local evidence:

- `Builds/Android/big-red-button-diagnostics.txt`
- `Builds/Android/big-red-button-mesh-introspection.txt`
- `Builds/Android/big-red-button-animation-samples.txt`

## Approaches Attempted

### 1. Static authored cap mesh collider snapshot

Approach:

- Add `BigRedButtonGeneratedBodyCollider` to the red cap.
- Capture a serialized snapshot mesh in the scene outside play mode.
- Drive a `MeshCollider` from that snapshot.

Why it was attempted:

- This matches how the base shell can be authored and inspected outside play mode.
- It keeps the scene representative before entering play mode.

Observed result:

- The cap could own a `MeshCollider`.
- The scene serialized correctly.
- This was not enough to guarantee an in-headset shell that visibly matched the animated cap.

Failure mode:

- The authored snapshot is only one pose.
- The red cap is skinned, so a static snapshot can drift from the actual runtime cap shape during animation.

### 2. Runtime baked skinned mesh collider

Approach:

- Change `BigRedButtonGeneratedBodyCollider` so that in play mode it bakes from the live `SkinnedMeshRenderer` instead of preferring the serialized snapshot.

Why it was attempted:

- This is the closest analogue to a true dynamic mesh collider around the visible red cap.

Observed result:

- The cap mesh collider could exist and update during play.
- It removed earlier stale-pose mismatch between authored snapshot and runtime animation.

Failure mode:

- Existence of a baked `MeshCollider` did not reliably produce a readable shell in-headset.
- Later logic accidentally made the entire press path depend on this mesh staying valid, which caused regressions when the cap path was not readable.

### 3. Renderer-shell overlay around the cap mesh

Approach:

- Add `BigRedButtonRendererMeshDebug` to draw an oversized baked shell from the cap renderer itself.

Why it was attempted:

- It was meant to make the cap shell visually obvious even if the collider shell was hard to distinguish.

Observed result:

- It could produce a large visible shell.

Failure mode:

- It became visually confusing because it was not the actual collider shell.
- In screenshots it often looked like a floating lid or a second unrelated object.
- It made debugging harder because it mixed "visualization of renderer" with "visualization of collider."

Status:

- The runtime controller currently disables this extra shell path.

### 4. Horizontal fallback disc based on renderer bounds

Approach:

- Keep a separate `Button Trigger Surface` object.
- Move it every frame from `pressTriggerRenderer.bounds`.
- Keep it horizontal and visibly oversize it as a disc-like debug band.

Why it was attempted:

- This is simple.
- It is resilient to mesh-collider failures.
- It gives the user a visible and testable press surface on Quest.

Observed result:

- This was the most reliable Quest interaction path.
- After the non-convex query bug was fixed, this surface could trigger the press again.

Failure mode:

- It does not match the cap top plane.
- It is offset from the visible red cap when viewed from the side.

### 5. Mesh-derived trigger-surface pose fitting

Approach:

- Use baked skinned vertices or the cap mesh collider mesh.
- Infer the top face by filtering triangles using an expected press normal.
- Fit the fallback disc to the inferred top plane instead of keeping it horizontal.

Why it was attempted:

- This was meant to preserve the reliable fallback disc while also matching the visible red cap tilt and footprint.

Observed result:

- On some iterations it visibly produced a rotated disc, but the rotation and placement were wrong.
- On later iterations the disc disappeared entirely on Quest and interaction regressed.

Failure mode:

- The cap top plane is not aligned with either obvious transform.
- Our placement math for the fitted disc was unstable.
- The user repeatedly observed:
  - a magenta disc rotated 90 degrees wrong
  - a magenta disc behind the cap
  - later no visible disc and no press

## Bugs And Regressions Found During Investigation

### Non-convex collider query bug

Observed symptom:

- Earlier Quest logcat showed `Physics.ClosestPoint can only be used with a BoxCollider, SphereCollider, CapsuleCollider and a convex MeshCollider.`

Fix:

- Switch the close-contact path away from invalid `ClosestPoint` usage and use `ComputePenetration` when allowed, with bounds fallback otherwise.

Result:

- That specific warning stopped appearing.

### Fallback disc was not a true fallback

Observed symptom:

- At one point the disc existed visually but could not trigger the press by itself.

Root cause:

- The controller returned early when the cap mesh collider was missing, so the fallback disc never got a chance to work on its own.

Fix:

- Make press evaluation accept either the cap mesh contact or the fallback surface.

Result:

- Disc-driven interaction was restored.

## Runtime Findings That Matter Most

The sampled animation report at `Builds/Android/big-red-button-animation-samples.txt` is the most important evidence gathered so far.

What it shows:

- `renderer.rotationEuler` stays effectively constant across the `pressed` clip.
- `rootBone.rotationEuler` stays effectively constant across the `pressed` clip.
- `topSurface.normal` also stays effectively constant across the sampled frames.

What that means:

- The cap is not "tilting during the press animation" in the sense we assumed while trying to make the disc follow a changing angle.
- The cap already lives in a fixed tilted pose.
- The animation appears to move or deform that already-tilted skinned mesh rather than rotating the visible top plane over time.

Also important:

- The sampled top normal is not aligned with the obvious transforms.
- In the sampled report:
  - the top-face normal is about 16.5 degrees away from `renderer.up`
  - the top-face normal is about 41.5 degrees away from `rootBone.up`

So these assumptions were both wrong:

- "Use the renderer transform for the cap-top orientation."
- "Use the joint transform for the cap-top orientation."

Finally:

- The sampled centroid output for the inferred top face became implausible in world space.
- This strongly suggests our mesh-derived placement code had a space mismatch or another geometry bug.
- That is why mesh-derived rotation may still be useful, but mesh-derived position is not currently trustworthy.

## Why The Request Has Not Yet Fully Succeeded

The project does already have enough information to identify the cap mesh and animate it. The failure is not "we cannot find the object." The failure is that our mental model of the cap geometry has been wrong in two ways:

1. We assumed the visible top tilt came from a transform we could reuse directly.
2. We assumed the baked skinned mesh was safe to use for both orientation and placement without first validating the coordinate-space math.

The first assumption was disproven by the sampled animation report.

The second assumption was disproven by repeated Quest regressions when the fitted disc either:

- appeared in the wrong place
- rotated incorrectly
- disappeared entirely
- stopped triggering interaction

## Recommended Next Step After This Commit

Keep the fallback disc alive and visible at all times.

For the next true cap-collider attempt:

- do not remove the disc fallback
- do not let press interaction depend solely on the cap mesh collider
- treat the cap mesh shell and the disc as separate systems
- validate baked-mesh data with runtime diagnostics before trusting it for placement
- if a cap shell is attempted again, prefer using it for contact and visualization only after its world-space pose has been verified against Quest screenshots

The safest technical direction is:

- keep bounds-based position for the disc
- only reintroduce mesh-derived orientation after its frame of reference is validated
- keep the disc as the guaranteed press path until the cap shell is proven reliable on device

## Checkpoint: Disc Placement Locked, Size And Sensitivity Still Wrong

This checkpoint is worth preserving because it restores the most important part of the fallback path:

- the trigger disc is back in the correct place on the cap
- the trigger disc follows the button animation again

Known remaining problems at this checkpoint:

- the disc is still oversized relative to the red cap surface
- press triggering is still too sensitive
- on the latest Quest screenshot, the visible hand shell can hover well above the visible disc shell and still register a hit

Interpretation:

- placement and animation-follow are now stable enough to keep
- the next work should be limited to footprint regression and collision-cause diagnosis

Immediate next step from this checkpoint:

- regress the disc footprint back to the play-mode-aligned fit that existed before the oversized-disc regression
- then instrument the actual collision query path to determine why interaction fires while the visible shells are still far apart
