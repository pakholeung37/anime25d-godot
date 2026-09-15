# Anime25D

An extensible Godot 4.7 C# 2D model runtime: immutable meshes and layers, model instances,
code-authored motions and expressions, custom deformation, CPU/GPU rendering and explicit masks.

![The Anime2.5D sample in Godot](docs/media/runtime.gif)

The [addon](addons/anime25d/README.md) owns the complete model execution and rendering lifecycle.
Applications can display a simple model using built-in layer transforms or supply custom CPU/GPU
formulas while reusing the same renderer. Parameters and animation assets are defined in code.

The [sample](demo/SampleRig/README.md) provides Anime2.5DRig character rules and artwork integration.
Try **Nod**, **Loop sway**, expressions and mouse tracking together. PSD/model conversion remains an
offline sample tool.

See the [implementation and validation report](docs/MODEL_RUNTIME_IMPLEMENTATION.md) and
[design plan](docs/MODEL_RUNTIME_REFACTOR_PLAN.md).

Code is [MIT licensed](addons/anime25d/LICENSE). Sample artwork belongs to its original authors.
