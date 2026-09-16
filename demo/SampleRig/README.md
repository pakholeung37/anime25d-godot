# Sample model definition

The production demo uses `AnimeModelNode` directly. `SampleModelBuilder` creates a `ModelDefinition` with:

- code-authored parameters, motions and expressions;
- built-in sine/smoothing drivers and small sample random/talk/blink policies;
- model-specific spring targets driving the runtime's `SpringBank`;
- runtime opacity bindings and a small alternate-eye selection policy;
- `SampleWarp`, a pure composite deformation consuming published pose and spring channels;
- static meshes, weights and explicit masks.

`SampleGpuFactory` supplies matching shaders and static layer data. `SampleGpuLayout` declares the frame texture
layout; runtime `FrameTextureBinding` owns packing and uploads. CPU and GPU read the same model frame.
No production `SampleBehavior`, `PartState`, `MotionPipeline`, `PhysicsSolver` or duplicate pose/frame exists.

The importer/profile retain the existing sample model format. No motion JSON or animation file IO was added.
Old reference algorithms and the old node facade live only under `tests/Compatibility`, included in Debug test
builds. Release builds exclude them. `tests/Godot/CompositionRenderChecks.cs` tests the new production pipeline.
