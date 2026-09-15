# Runtime architecture

## Boundaries

The only runtime deployment unit is `addons/anime25d`. Its C# source compiles in the consuming Godot project. The optional editor plugin manifest is discoverable in Project Settings; `[GlobalClass]` registers the node and resource after compilation without plugin activation.

The converter consumes layered PSDs and performs all content inference outside the addon. Runtime construction generates regular mesh topology and vertex influence weights from already-detected anchors and strand controls; it never parses PSD, scans image alpha, identifies layer names beyond the normalized rig conventions, or synthesizes missing textures.

`AnimeRigModel` holds manifest JSON and explicitly referenced texture resources. No absolute asset paths, original repository path, JavaScript files, or runtime file-system scan are needed to load the model in another project. Model definitions are treated as shared read-only data; simulation arrays are always per-instance.

## Simulation

- `Parameters`: all 35 original keys, bounds and defaults. Enum names match the upstream strings.
- `RigSimulation`: independent target/current/frame values, instance clock, automatic movement, blinks, random talk, original expression presets and spring states. A random callback makes animation tests reproducible.
- `PartState`: base/working positions, normalized UVs, triangle indices, Gaussian strand weights, longitudinal progress and front-hair three-block weights.
- `RigDeformer`: exact ordered port of the original local feature, head, breath, bust, arm, bang, hair and final body transforms.

Intermediate scalar arithmetic uses double precision as JavaScript does. Base positions, influence weights and final positions use Float32 arrays. The intermediate Float32 write before the final body rotation is retained. Values of `soft` above 1 intentionally extrapolate the hard/soft spring mixture; restricting that mixture would change the reference appearance.

The instance clock starts at zero rather than inheriting browser page uptime. Delta is capped at 0.05 seconds, and spring substeps are at most 1/120 seconds, matching the reference loop. This preserves the original low-FPS slowdown policy rather than adding elapsed-time catch-up.

Breathing remains active independently of the Idle switch. The Physics switch controls hair displacement, while the bust formula continues to use its own parameter and spring, matching the original behavior. `Playing=false` freezes the complete simulation.

## Rendering

Each part has one `MeshInstance2D`, texture and per-instance material. Topology and UVs are uploaded once; subsequent frames call `ArrayMesh.SurfaceUpdateVertexRegion` with a span over the existing Float32 position buffer. The mesh uses two-dimensional vertices and the dynamic-update flag. Mesh bounds include displacement beyond the unwarped rectangle.

PNG imports premultiply alpha before texture filtering and disable alpha-border repair. The part shader uses `blend_premul_alpha` and correctly incorporates parent `Modulate` alpha. This matches the upstream WebGL `UNPACK_PREMULTIPLY_ALPHA_WEBGL` plus `ONE, ONE_MINUS_SRC_ALPHA` behavior.

Each character has separate left/right mask SubViewports at model canvas resolution. Active white-eye meshes are shared with the corresponding mask viewport. The mask shader discards texture alpha below 0.25 and writes an opaque mask. The iris shader samples the corresponding mask using interpolated deformed model coordinates, independent of parent transforms and layer draw order. Nearest sampling and a 0.5 cutoff keep the mask binary.

This replaces WebGL stencil operations with an explicit mask texture. It costs two canvas-sized render targets per instance, but preserves individual eye masks, arbitrary order and separate instance state. Rendering is currently validated with the Compatibility backend.

Godot API references: [ArrayMesh](https://docs.godotengine.org/en/4.7/classes/class_arraymesh.html), [CanvasItem shader reference](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/canvas_item_shader.html). The installed 4.7.2 GodotSharp XML was also checked for the C# span upload overload.

## Ownership and lifecycle

`LoadModel` validates the model and constructs CPU state before replacing the active instance. The node owns meshes, materials, draw nodes and mask viewports; `ClearModel` and tree exit release those resources. Textures are shared `Resource` objects owned by the model and are not explicitly disposed by individual characters.

Model resource assignment is intended before entering the scene tree. Use `LoadModel` for a running instance. `AutomaticProcessing=false` delegates timing to the caller, which invokes `Advance`; `RefreshPose` updates layer edits without advancing clocks or physics. Target parameter edits use original smoothing unless `immediate` is requested or the node is paused.

## Validation approach

The numerical oracle extracts `prepareLayers`, `fadeAlpha`, `deform` and `animate` from the unchanged upstream `app.js`, with browser/GPU functions stubbed only during numerical tests. Every reference animation frame evaluates the original deformation, including hidden layers retaining their prior positions. Both implementations receive the same LCG random sequence, pose inputs and frame deltas.

The image oracle reuses the upstream shader compilation, texture upload, binding and complete stencil/draw pass in actual Chrome WebGL. Positions come from the original numerical oracle. Godot independently simulates the corresponding sequence and captures its own render target. Comparison uses premultiplied RGBA; browser PNG output is unpremultiplied while Godot render-target readback is premultiplied.

The standalone consumer test copies only the addon and one converted model into a fresh temporary project, builds with implicit usings disabled, imports and runs it. This checks the addon boundary independently of the demo application.
