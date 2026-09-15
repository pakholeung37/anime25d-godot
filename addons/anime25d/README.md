# Anime25D runtime addon

Godot 4.7.2 Mono / C#, targeting .NET 10 in the example project.

Copy this entire directory to `res://addons/anime25d` in a Godot C# project and build. The `AnimeRigNode` node and `AnimeRigModel` resource are registered with `[GlobalClass]`; enabling the editor plugin is optional. No JavaScript or PSD library is required by the addon.

Assign a converted `AnimeRigModel` to the node's `Model` before adding it to the tree. Use `LoadModel` to replace a model after startup. Textures must have `process/premult_alpha=true`, `process/fix_alpha_border=false`, and lossless import; keep the converter-generated `.png.import` files.

Public node controls:

- `SetParameter(string name, double value, bool immediate = false)` uses the original keys and smoothing.
- `SetPreset(string? name)` supports neutral, smile, usume, surprise, jito, winkL, winkR. Null removes the preset lock without changing parameter values.
- `Playing` freezes/unfreezes automatic simulation.
- `AutomaticProcessing=false` gives the caller manual timing via `Advance(delta)`.
- `RefreshPose()` applies direct layer changes without advancing time.
- `Simulation.Auto`, `Simulation.Target`, `Simulation.Frame` and `Simulation.Parts` expose per-instance controls. Call `Simulation.SetBlinkEnabled` to cancel/reset an active blink when switching it off.
- With `Simulation.Auto.Mouse=true`, automatic processing polls the global desktop cursor, normalized against the display containing the game window. Tracking continues outside the character and window; other-display positions clamp to the same reference display's edges. Manual `Advance` continues to use caller-supplied normalized mouse values.
- `ClearModel()` releases instance rendering resources. Shared model textures remain usable by other instances.

The local origin is the top-left of the original model canvas. Rendering uses dynamic 2D meshes and per-instance eye-mask SubViewports. Validate other rendering backends before using them; the sample project uses Compatibility.

Core formulas are derived from Anime2.5DRig by hakoniwa, commit `7450341934a8ff77bf05b90d9f708786e3eb3996`, https://github.com/852wa/Anime2.5DRig. See LICENSE for the retained MIT notice. Artwork is not licensed by this code license.
