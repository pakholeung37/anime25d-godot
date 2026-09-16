# Anime25D

An extensible Godot 4.7 C# 2D model runtime: immutable meshes and layers, model instances,
code-authored motions and expressions, composable drivers and physics, ordered deformation, CPU/GPU rendering and explicit masks.

![The Anime2.5D sample in Godot](docs/media/runtime.gif)

The [addon](addons/anime25d/README.md) owns the complete model execution and rendering lifecycle.
Applications compose built-in signals, smoothing, springs, layer bindings and deformers, or supply custom
CPU/GPU formulas while reusing the same execution pipeline and renderer. Parameters and animation assets are defined in code.

The [sample](demo/SampleRig/README.md) provides Anime2.5DRig character rules and artwork integration.
Try **Nod**, **Loop sway**, expressions and mouse tracking together. PSD/model conversion remains an
offline sample tool.

See the [implementation and validation report](docs/RUNTIME_COMPOSITION_IMPLEMENTATION.md) and
[design plan](docs/RUNTIME_COMPOSITION_PLAN.md).

Code is [MIT licensed](addons/anime25d/LICENSE). Sample artwork belongs to its original authors.
