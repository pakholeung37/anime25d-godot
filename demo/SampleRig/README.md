# Anime2.5D sample implementation

The sample supplies character-specific behavior to the generic addon model runtime.

- `SampleModelBuilder` compiles imported layers to immutable generic meshes, explicit mask references,
  code-authored animation assets and a behavior factory. Geometry and strand weights are baked once
  per definition and exposed read-only; instance physics state remains independent.
- `SampleBehavior` implements baseline control, resolved pose/physics, layer visibility and CPU vertex
  formulas. It creates no Godot draws, mask views or renderer.
- `SampleGpuFactory` / `GpuDeformationBinding` provide sample GPU inputs; the addon owns mesh/material
  lifecycle. The sample shaders supply deformation and include the addon's shared color/mask passes.
- `AnimeRigNode` delegates model execution, loading transactions and submission to `AnimeModelNode`.
  `Model`, `ProfileOverride` and parameter helpers are sample authoring conveniences.
- `SampleReferenceAdapter` is a thin test/legacy-input facade over the same ModelInstance. It retains
  the original capped Step and preset lock for pinned numerical tests; there is no separate evaluator.

The generic parameter set has no anatomical names. Sample channels map to the old rig only here.
Breath/iris bounce/closed-eye variant are model-defined channels. Automatic sample controllers are
sample behavior; generic models remain static until explicitly driven.

Use `actor.Animation.PlayMotion("nod")`, `PlayMotion("sway")`, and `SetExpression("smile")` in the demo.
External mouse tracking subscribes to the inherited `FinalizingPose` hook after animation mixing.
For manual operation call `actor.Advance(delta)` or `RefreshPose()` to evaluate and submit together.
Set `AnimeRigModel.Animations` from code before loading for other sample-compatible animation assets.

Existing model/profile IO remains in the sample; no animation curve file reader was introduced.
The model builder freezes imported arrays before sharing definitions. Original formulas derive from
Anime2.5DRig by hakoniwa, commit `7450341934a8ff77bf05b90d9f708786e3eb3996`.
See `addons/anime25d/LICENSE`; sample artwork is not covered by the code license.
