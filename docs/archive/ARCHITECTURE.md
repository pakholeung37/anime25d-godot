# Runtime architecture

The refactor preserves the original default motion and deformation, not its monolithic organization.
The project-level README remains an introduction; addon usage is documented in `addons/anime25d/README.md`.

## Responsibilities

| Module | Responsibility |
| --- | --- |
| `Core/Model` | Typed rig metadata and mutable per-character layer state |
| `Core/Compatibility` | One-time V1 name-to-role translation |
| `Core/Parameters` | Readable pose keys, defaults, ranges, legacy string mapping |
| `Core/Configuration` | Immutable motion, physics, mesh, deformation and expression settings |
| `Core/Animation` | Independent idle, random, talk and blink state machines |
| `Core/Physics` | Spring integration and motion-driven secondary movement |
| `Core/Geometry` | Rest meshes, triangle indices and baked strand/fringe weights |
| `Core/Deformation` | Layer visibility and the CPU reference deformation stages |
| `Core/RigSimulation` | Orders pose composition, smoothing, physics and optional CPU evaluation |
| `Rendering` | Godot resource ownership and GPU bindings |
| `Shaders` | GPU feature, head, body and hair deformation shared by color/mask passes |
| `AnimeRigNode` | Public character lifecycle and playback controls |
| `demo/Input` | Demo-only desktop pointer sampling and pointer-to-pose mapping |

The core has no Godot dependency. The node is not a VN-specific integration layer.

## Frame contract

Target parameters → external frame-local pose modifiers → idle → random → talk → blink → smoothing → breath/iris bounce → springs → visibility → rendering.

`PreparingPose` is a device-agnostic extension point, not an input system. It receives a scratch parameter buffer;
overrides affect only that frame and never replace authored targets or unlock expressions. The Godot node relays the
simulation event so controllers survive model reloads, and detaches from discarded simulations.
Only the demo subscribes a desktop mouse controller. Its enable switch, gains and global-screen sampling live in `demo/Input`;
there is no mouse state, tracking algorithm, OS input call or mouse configuration in the addon. No separate mouse plugin is shipped.

Ordering is deliberate: the original random-number consumption and blending precedence are regression-tested.
Expressions suppress automatic talk/blink while active. Breathing and chest springs continue with the same original semantics;
the Physics toggle gates hair deformation, not all secondary movement.

In GPU mode the CPU never walks vertices during normal playback. It updates one per-actor RGBA float pose texture,
per-part depth, alpha, and up to six spring displacement pairs. Meshes and three RGBA weight texels per vertex are uploaded only on load.
The visible pass and eye-mask pass include the same deformation shader.

The CPU evaluator retains double precision and the original Float32 boundary before final body rotation.
GPU arithmetic is float precision, so GPU equivalence is image-tolerance based, not a promise of bit-identical vertices.
The Godot implementation uses the documented [CanvasItem vertex shader interface](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/canvas_item_shader.html).

## Configuration and compatibility

Artistic coefficients have descriptive names and live in profiles; polynomial constants, buffer layouts and safety limits remain implementation details.
Angular frequency is radians/second; delays explicitly use milliseconds; spring integration and durations use seconds.
Pixel coefficients retain their original model-space behavior. Only terms explicitly multiplied by FaceScale are face-scaled;
normalizing every old pixel constant would change the visual baseline and is intentionally not part of this refactor.

Godot profile resources use validated JSON backed by typed C# records. This avoids duplicating every setting in a second inspector schema.
Profile overrides replace the entire model profile, with omitted fields falling back to reference defaults.
Live simulations snapshot the authoring dictionaries; resource edits require a reload.

PartRole, PartGroup, PartSide and FadeMode replace string-based behavior selection.
Both bundled models use V2. The runtime compatibility adapter accepts V1, and offline tooling migrates existing manifests without touching textures.
The original JavaScript oracle receives an explicit V2-to-V1 metadata translation; its formulas remain unmodified.

Shader parameter, role and channel indices are generated from C# declarations.
Run `node tools/shader-bindings.cjs --check` after changes, or `--write` after deliberately changing the binding contract.
CPU and GPU formulas remain separate implementations, covered by numerical and rendered regression tests.

## Deliberate boundaries

No VN engine integration, PSD conversion in the addon, webcam, microphone, web support, or general-purpose model editor.
Full-canvas eye-mask viewports remain in use. Multi-character GPU throughput, other renderers, exports and other desktop platforms are not yet benchmarked.
