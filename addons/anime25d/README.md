# Anime25D model runtime

Godot 4.7.2 Mono / .NET 10. Copy this addon into `res://addons/anime25d` and build.
`Anime25D.Runtime` is plain C#; `Anime25D` supplies Godot integration. No model-specific node subclass or animation file loader is required.

## Model and animation registration

```csharp
using Anime25D;
using Anime25D.Runtime;
using Godot;

var animation = new AnimationModel(
    new[] { new ParameterDefinition("offset", 0, -40, 40) },
    motions: new Dictionary<string, MotionDefinition> {
        ["sway"] = new(2, new[] {
            new MotionTrack("offset", MotionCurve.Smooth((0, 0), (0.5, 30), (1.5, -30), (2, 0)))
        }, loop: true)
    });
var plan = new ModelPlan(components: new[] {
    new ComponentDefinition("position", ModelStage.Layers,
        model => new LayerBindingComponent(model, new[] {
            new LayerParameterBinding("offset", "panel", LayerProperty.TranslationX)
        }))
});
var definition = new ModelDefinition(animation, 256, 256,
    new[] { new LayerDefinition("panel", GridMeshBuilder.Create(64, 64, 128, 128)) }, plan: plan);
var actor = new AnimeModelNode();
AddChild(actor);
actor.Load(new ModelView(definition, new[] { texture })); // already-loaded Texture2D
actor.Instance!.Animation.PlayMotion("sway");
```

`AnimationModel` snapshots model-local motion/expression dictionaries. Motion tracks contain timed curves;
expressions contain persistent parameter values. Both support override/add/multiply and fades. A new motion
replaces the current motion; a new expression replaces the current expression, with temporary overlap during
crossfade. Expressions mix after motions. Use `PlayMotion`, `StopMotion`, `SetExpression`, `ClearExpression`.

## Component plan

The immutable `ModelPlan` contains ordered component registrations, derived channel definitions and ordered
deformer definitions. Each component factory creates independent state for each instance. Factories and
custom definitions must not capture mutable state shared between instances. Use the optional random factory
on the plan to supply an independent seeded generator per instance.

Stages execute in this order:

1. Base input parameters → `BasePose` components → animation's before hooks.
2. Motion → expression → animation's after hooks.
3. `FinalPose` components → node/instance `FinalizingPose` external input.
4. `Derived` components, including physics.
5. Reset layer outputs → `Layers` components → authored/user layer overrides.
6. CPU deformation when selected → validate and publish frame → render → playback notifications.

Components implement `AdvanceState(context, delta)` and `EvaluateOutput(context)`. Refresh calls only the
second method. Stateless components implement only evaluation. `context.Time` is seconds;
`TimeMilliseconds` exposes the same model clock accumulated in milliseconds. Animation handles own their
playback time/speed; all model components share the model clock. Do not advance a private model clock.

Use `context.Pose` during component evaluation; `context.Frame.Pose` is not the working base-stage pose.
Do not retain writable buffers, invoke model evaluation recursively, or mutate another component during a frame.
Channel reads/writes are declared on registration; each channel has one producer, and reads must follow writes.
A disabled producer emits zero channels. Stateless parameter effects disappear when disabled; disabled stateful
components freeze their state. Model plans require explicit stage order and reject forward channel references.

### Included components

- `SineDriver`: one or more parameter signals, with amplitude/frequency/phase/offset and blend mode.
- `SmoothDriver`: exponential smoothing of selected parameters, independently stored per instance.
- `EnvelopeDriver`: repeating attack/hold/release/interval envelope.
- `ParameterMapDriver`, `SignalMap`, `MotionCurve.Sample`: numeric mappings and code-authored curves.
- `ChannelWriter`: pure derived scalar/array outputs; span overload avoids per-frame allocations.
- `SpringBank`: batch spring integration and relative-target displacement output.
- `LayerBindingComponent`: parameter-to-transform/opacity bindings.
- `LayerOpacityBinding`: computed opacity mapping; `LayerSelector`: discrete layer selection.
- `AffineDeformer`, `VertexOffsetDeformer`: ordered geometry operations.

`SetParameter(id, value, immediate)` changes base input; immediate also snaps registered smoothing components.
`SetComponentEnabled(id, enabled)` controls a component. `ResetInputs` resets base inputs and component state,
but does not stop playback, reset the model clock, clear layer overrides, or re-enable components.
`Component<T>(id)` is available for explicit custom control outside evaluation; callers must respect component
lifecycle and avoid state mutation from evaluation callbacks.

## Physics and frame channels

Declare `ChannelDefinition("target", count)` and `ChannelDefinition("lag", count)`. A producer writes targets,
then a `SpringBank` reads targets and writes lag. Register both reads and writes. Each spring has stiffness,
damping and displacement scale. Integration subdivides delta by a maximum step; it does not silently discard
time. More than 10,000 substeps per call is rejected. This is a bounded variable-step solver, not a fixed-step
accumulator. `SpringState` is available for custom reusable solvers.

`ModelFrame` contains read-only final parameters, numeric channels and layer views. CPU/GPU extensions consume
this frame and immutable definitions, never private driver/solver state. Frame views expire on the next model
evaluation; copy values when retaining them. `EvaluateCpuSnapshot` copies current geometry without advancing
animation, drivers, randomness or physics. Hidden layer CPU buffers can retain previous geometry; diagnostics
explicitly evaluate all layers.

## Deformer registration

Subclass `DeformerDefinition`, specify a model-local ID, target layer IDs and channel reads, then put the
objects into `ModelPlan.Deformers` in execution order. Every target layer applies its matching subsequence.

```csharp
public override void Deform(int layer, ModelFrame frame,
    ReadOnlySpan<float> rest, ReadOnlySpan<float> input, Span<float> output)
{
    // rest: immutable original positions; input: previous operation's result.
    // Write every output coordinate; topology/UV remain unchanged.
    input.CopyTo(output);
}
```

Coordinates are model-space. The runtime owns traversal and scratch/output buffers. Operations are pure with
respect to simulation state. Weight baking belongs to immutable model construction. One complex character
formula can remain a composite operation when splitting would introduce numerical or semantic changes.
Layer transforms are applied after geometry; authored × computed × user order follows System.Numerics row vectors.

## GPU backend

`ModelView(definition, textures, optionalGpuFactory)` provides already-loaded textures and optional shaders.
`IGodotDeformationFactory.Supports(ModelDefinition)` must match the complete operation sequence/configuration
it implements; it must not choose a backend by a behavior type. Its binding receives published frames only.

Built-in GPU support covers affine/vertex-offset sequences of up to 16 operations per layer (up to 16,384 vertices
per layer). Color and mask passes use the same deformation. Unsupported sequences use CPU in Auto mode;
`BackendFallbackReason` explains why. Explicit GPU mode rejects unsupported models before replacing an existing
loaded model. Backend selection is model-wide; there is no automatic C#→shader compiler or per-layer hybrid mode.

`FloatTexture` owns reusable RGBA float storage. `FrameTextureBinding` compiles a declared parameter/channel
layout and uploads published values. Sample code supplies shader layouts and static uniforms, not a renderer
or a second parameter packing engine. Custom shader includes supply `model_deform` and use the shared runtime,
color and mask includes. Borrowed textures and shaders remain caller-owned.

## Masks and lifecycle

`MaskDefinition` names explicit source layer IDs; a target layer references `MaskId`. Binary source union,
multiple independent masks, visibility and common transforms are handled by the renderer. Nested and soft
masks are not supported. Model bounds are conservative authored bounds; maintain them for extreme deformations.

`AnimeModelNode` owns instance, meshes, materials, mask viewports and GPU bindings. Candidate load/refresh/submit
must succeed before replacing the old model. Clear/reload from playback callbacks is deferred to a safe boundary.
`Playing=false` freezes automatic playback; `RefreshPose` explicitly evaluates new inputs without advancing state.
`Animation.Paused` also prevents model advancement. Evaluation errors retain the last published frame and fault
the instance; arbitrary custom component state is not rolled back. Dispose releases component state once.

The old `IModelBehavior`, `BasicModelBehavior`, `CreateBehavior` and `Anime25D.Core` APIs have been removed.
See `tests/consumer` for a complete independent application using only this addon, including custom GPU fallback,
mask rendering, component composition, CPU/GPU parity and lifecycle checks.
