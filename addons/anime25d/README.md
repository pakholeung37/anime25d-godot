# Anime25D runtime addon

Godot 4.7.2 Mono / .NET 10. Copy this directory to `res://addons/anime25d` and build.
The editor plugin is optional; the runtime needs no JavaScript or PSD library.

## Character node

Assign an `AnimeRigModel` before adding the node to the tree, or call `LoadModel` afterward.

```csharp
using Anime25D;
using Anime25D.Core;

var actor = new AnimeRigNode { Model = model };
AddChild(actor);
actor.SetParameter(Parameter.HeadYaw, 0.5);
actor.Simulation!.AutomaticMotion.Random = false;
actor.SetPreset("smile");
```

- `Playing` pauses simulation. `AutomaticProcessing = false` enables caller-driven `Advance(deltaSeconds)`.
- `SetParameter` clamps to the loaded profile's ranges and smooths toward the target; `immediate: true` bypasses smoothing.
- Readable enum names are preferred. String setters also accept original names such as `angleX`.
- `SetPreset(null)` unlocks automatic blink/talk without resetting parameter values.
- `Simulation.AutomaticMotion` controls idle, blink, random motion, talk, mouse and hair physics.
- `Simulation.SetBlinkEnabled(false)` also cancels the current blink and iris bounce.
- Desktop mouse tracking polls global screen coordinates, independent of actor transforms and window boundaries.
  With manual timing, supply `Simulation.Pointer.Horizontal`, `Vertical` and `IsAvailable` yourself.
- Edit per-instance layer visibility, opacity, depth and order through `Simulation.Parts`; call `RefreshPose()` when driving manually.
- `ClearModel()` releases instance rendering resources, not shared artwork textures.

## Profiles

`AnimeRigProfile.SettingsJson` contains optional authoring settings. Missing fields retain original defaults.
Assign a resource to `AnimeRigModel.Profile`, or use `AnimeRigNode.ProfileOverride` to **replace** the model profile.

```json
{
  "Motion": {
    "Idle": { "YawPrimary": { "Amplitude": 0.08, "AngularFrequency": 0.35 } },
    "Breath": { "PeriodSeconds": 4.2 }
  },
  "Deformation": { "HeadYawPixels": 18 },
  "Parameters": {
    "HeadYaw": { "Default": 0, "Minimum": -0.8, "Maximum": 0.8 }
  }
}
```

Profile groups are `Motion`, `Physics`, `Deformation`, `Mesh`, `Parameters` and `Expressions`.
See the typed definitions in `Core/Configuration` for all settings and units.
Unknown fields and invalid ranges/timing values are rejected rather than silently ignored.
Profiles are snapshotted on load: after editing a profile, model topology, or `Backend`, call `LoadModel` again.
Changing a live pose parameter does not require reloading.

## Model and rendering contract

V2 manifests specify `role`, `group`, `side` and `fade` explicitly; layer names are arbitrary display labels.
V1 manifests are translated once by `Core/Compatibility/LegacyRigAdapter`.
The offline converter emits V2. Keep its lossless, premultiplied-alpha `.png.import` files.

The default `DeformationBackend.Gpu` keeps rest meshes and baked strand weights on the GPU.
A shared pose texture and small spring uniforms drive the vertex shader, including eye masks.
Motion scheduling and spring integration remain on the CPU.
`DeformationBackend.CpuReference` retains the original double-precision vertex evaluation for diagnostics.

`PartState.Positions` is evaluated only in CPU mode; it is not a GPU readback API.
For explicit CPU inspection, enable `Simulation.EvaluateCpuGeometry` and call `UpdateGeometry()`.
The coordinate origin remains the top-left of the model canvas.

Validated on macOS / Apple M4 with the Compatibility renderer. Other renderers/platforms require validation.

## Attribution

Formulas derive from [Anime2.5DRig](https://github.com/852wa/Anime2.5DRig) by hakoniwa,
commit `7450341934a8ff77bf05b90d9f708786e3eb3996`.
See LICENSE for the retained MIT notice. Sample artwork is not covered by the code license.
