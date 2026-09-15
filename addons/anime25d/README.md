# Anime25D model runtime

Godot 4.7.2 Mono / .NET 10. Copy this directory to `res://addons/anime25d` and build.
The editor plugin is optional. `Core` is plain C#; `Godot` owns the model node and rendering backend.
Models, motions, expressions and extension bindings are authored in code. No animation file loader is required.

## A complete model, using the built-in renderer

```csharp
using Anime25D;
using Anime25D.Core;
using Godot;
using System.Collections.Generic;

// texture is an already-loaded Texture2D supplied by the application.
var animation = new AnimationModel(
    new[] { new ParameterDefinition("offset", 0, -40, 40) },
    new Dictionary<string, MotionDefinition>
    {
        ["sway"] = new(2, new[]
        {
            new MotionTrack("offset", MotionCurve.Smooth((0, 0), (0.5, 30), (1.5, -30), (2, 0)))
        }, loop: true)
    });
var definition = new ModelDefinition(animation, 256, 256,
    new[] { new LayerDefinition("panel", GridMeshBuilder.Create(64, 64, 128, 128)) },
    behavior: model => new BasicModelBehavior(model, new[]
    {
        new LayerParameterBinding("offset", "panel", LayerProperty.TranslationX)
    }));
var actor = new AnimeModelNode();
AddChild(actor);
actor.Load(new ModelView(definition, new[] { texture }));
actor.Instance!.Animation.PlayMotion("sway");
```

The addon creates meshes, materials and draw nodes. Applications do not need to implement a renderer.
Default `BasicModelBehavior` displays rest geometry; optional bindings drive translation, rotation
(in radians, around an explicit pivot), scale and opacity. Binding scale/offset map parameter values
to properties. Multiple transform bindings compose in declaration order. Scaling usually uses offset 1.

`ModelDefinition` snapshots layer/mask collections; `MeshDefinition` copies vertices, UVs and indices
and exposes read-only spans. Mesh vertices are in model coordinates. A definition may be shared by
multiple instances; each instance owns its animation, overrides, behavior and frame buffers.

## Model evaluation

`ModelInstance` coordinates:

1. Sample behavior baseline, optional pre-animation controllers/modifiers.
2. Main motion, then expression mixing.
3. Post-animation controllers/modifiers and node `FinalizingPose` input.
4. Behavior simulation / resolved pose.
5. Behavior layer properties and user overrides.
6. CPU vertex evaluation or GPU frame preparation.
7. Complete frame publication; the Godot node submits and then dispatches playback events.

The parameter-only `AnimationRuntime` remains usable independently. Its `IAnimationOutput` is a
convenience for non-model consumers; it does not own the model pipeline.

Implement `ModelBehavior` (or `IModelBehavior`) for custom algorithms:

- `PreparePose(pose, delta, advance)`: baseline controller output. Advance state only when `advance`.
- `ResolvePose(pose, delta, advance)`: optional simulation and derived pose; never write back BasePose.
- `EvaluateLayers(readOnlyPose, layers)`: per-frame visual opacity, transform and draw order.
- `DeformCpu(layer, readOnlyPose, rest, output)`: write model-space deformed vertices into core-owned output.
- `Dispose()`: release instance-specific extension state.

The definition's behavior factory must create an independent behavior for each instance. Bind required
parameter/layer IDs during construction, not in a per-frame name search. Core initializes output buffers;
CPU deformation must write all vertex coordinates and must not advance simulation or randomness.
Static topology is fixed for an instance. Invisible layers may retain prior CPU buffers; explicit CPU
diagnostics evaluate all layers freshly.

`LayerOverrides` holds persistent user visibility, opacity, order and transform. Behavior layer outputs
reset every evaluation. Effective opacity multiplies authored, behavior and user values once. Numerics
uses row vectors: authored transform, then behavior transform, then user transform. The common shader
applies that final matrix after custom deformation in both CPU and GPU modes.

## Frames, pause and failure

- `actor.Advance(delta)` evaluates, uploads and dispatches; set `AutomaticProcessing = false` when driving manually.
- `actor.Playing = false` stops automatic advancement. Explicit `RefreshPose()` re-evaluates without advancing time.
- `Instance.Animation.Paused` freezes advancement and pending playback notifications.
- `RefreshPose()` samples the current timeline and recomputes derived outputs without simulation integration.
- Stateful inputs should implement `IPoseController`: `AdvanceState` runs only during advancement; `Apply` may run on refresh.
  Register in `BeforeControllers` or `AfterControllers`. `IPoseModifier` lists remain convenient for synchronous stateless
  inputs; their legacy delta is zero on refresh, so they must not unconditionally advance state.
- `FinalizingPose` is a node-level external input hook, after animation mixing. Its subscriptions survive reloads.
- `Frame` exposes read-only pose/layer views and a version. Views expire on the next successful evaluation; copy for retention.
  Preallocated front/back buffers prevent publishing half-written geometry. Extension exceptions fault the instance;
  rebuild it to recover. Private extension state is not transactionally rolled back.
- `EvaluateCpuSnapshot()` returns copied, versioned final model-space vertices without changing playback or physics.
  The renderer uploads pre-transform CPU vertices; diagnostic vertices already include the layer transform and must not be resubmitted.
- Loading constructs a hidden candidate, validates and initializes it before replacing the old instance. Failure releases
  candidate resources and leaves the old model usable. Node load/clear commands from callbacks are deferred until submission
  and event dispatch finish. Old instance events and input bindings are discarded on unload.

All evaluation APIs are synchronous and single-threaded. Do not retain writable callback buffers or recursively advance.
No global delta truncation or parameter smoothing is imposed by the core.

## Masks and rendering backends

`MaskDefinition(id, sourceLayerIds, sourceThreshold, receiverThreshold)` defines binary coverage.
A layer refers to one `MaskId`. A mask may union multiple sources and be shared by targets. Sources
cannot themselves be masked; nested/self-referencing masks are rejected. Hidden sources do not
contribute, and an empty mask hides the target. Models without masks allocate no mask viewports.
Source and target geometry use the same final model coordinate system. Default source/receiver
thresholds are 0.25 / 0.5. Textures use premultiplied alpha, matching the sample importer.

- `GeometryBackend.Cpu`: custom CPU deformation plus addon mesh uploads.
- `Gpu`: rest geometry stays on GPU; requires the default Basic behavior or a matching GPU extension.
- `Auto`: chooses supported GPU deformation, otherwise CPU. `ActualBackend` reports the result.

A custom `IGodotDeformationFactory` supplies color/mask Shader resources, behavior compatibility, and
an instance `IGodotDeformationBinding`. The binding configures addon-created materials and uploads
frame inputs. It owns only its extension buffers/textures and is disposed by the renderer.
It must not create its own layer renderer or mask views.

Shared shader ABI lives in `Godot/Shaders/runtime.gdshaderinc`. Define
`vec2 model_deform(vec2 p, int vertex_id)` then include `color_pass.gdshaderinc` or `mask_pass.gdshaderinc`.
The addon common passes apply transforms, opacity and binary masking. `runtime_*` uniforms are reserved.
Bindings should upload from the published `ModelFrame`, and simulation-derived state must be consistent
with that frame. Custom GPU deformation must remain within `ModelDefinition.Bounds`, a conservative
model-space bound supplied at construction. No automatic C# to shader conversion is provided.

Renderer-owned meshes/materials/mask nodes and extension buffers are released on clear. Supplied textures
and shaders are borrowed; the caller controls their lifetime and must keep them alive while in use.
GPU instances do not implicitly evaluate CPU vertices. `LastVertexUploadBytes` exposes submission cost.

## Motion and expression authoring

`MotionCurve.Linear`, `Smooth`, `Step`, `Constant`, and `Keyframe` with `CubicHermite` tangents construct
immutable curves. Time is in seconds; Hermite slopes are value units per second. Keys must be ordered,
finite and within motion duration. Outside curve keys, sampling holds the nearest endpoint. Looping
assets should author matching start/end values.

`AnimationModel` registers named `MotionDefinition`s and `ExpressionDefinition`s. Unknown parameter
IDs fail during model construction. Definitions contain no playback state. Nothing starts automatically.

One main motion and one expression are selected. New entries fade old entries out while fading in;
this is crossfade, not a sequential queue. Both layers support Override/Add/Multiply. Each layer blends
weighted targets against its incoming pose; missing channels do not claim a parameter, and excess
weights are normalized. Expressions hold until replaced/cleared and do not disable other inputs.

Use `PlayMotion(name, PlayOptions)`, `StopMotion`, `SetExpression`, `ClearExpression`, or stop a specific
PlaybackHandle. Options override loop, positive speed and fade times; handle pause is independent.
One-shots release at duration; loops repeat until stopped/replaced, with initial fade-in only. Fades use
runtime seconds. An outgoing entry can fade even if its timeline is paused.

PlaybackChanged reports Started, Looped, Completed, Interrupted and Stopped. LoopCount aggregates
crossings in large updates. Model-node callbacks run after upload; callback load/clear is safe. Commands
made in callbacks affect a subsequent evaluation. Refresh does not dispatch notifications.

## Sample and validation

`demo/SampleRig` compiles old rig/profile input into generic definitions with `SampleModelBuilder`.
`SampleBehavior` owns anatomy, springs and CPU formulas; `SampleGpuFactory` owns only the matching
shader ABI/buffers. `AnimeRigNode` is a thin sample authoring facade over `AnimeModelNode`.
`SampleReferenceAdapter` retains pinned reference input conventions and delta caps for numerical tests;
production node advancement uses the generic clock.

`tests/consumer` demonstrates real pixel validation of an unrelated model using addon rendering,
three mask groups, built-in transforms and a custom CPU/GPU extension, without any sample files.
See `docs/MODEL_RUNTIME_IMPLEMENTATION.md` for results and implementation mapping.
