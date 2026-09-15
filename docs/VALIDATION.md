# Refactor validation

Tested 2026-09-15 on macOS / Apple M4, Godot 4.7.2 Mono, .NET 10, Compatibility / OpenGL.
The reference is pinned Anime2.5DRig commit `7450341934a8ff77bf05b90d9f708786e3eb3996`.

## Results

- Original JavaScript oracle: **167 cases, 1,143,442 comparisons**. Maximum CPU vertex error **0 pixels**;
  maximum parameter error **2.22e-16**. Both samples, all parameter limits, four frame rates, expressions,
  mouse input, physics-off and long-blink behavior are covered.
- Architecture suite: **173 assertions** covering arbitrary model/part names, V2 models, profile defaults/ranges/reset,
  custom expressions and mesh settings, authoring-state isolation, GPU-mode CPU bypass, readable/legacy keys,
  malformed profiles and invalid deltas.
- Actual CPU-versus-GPU rendering: **194 image comparisons**, including every parameter boundary, continuous motion,
  custom profiles, arbitrary part labels and alternate closed-eye layers. Worst mean channel error **0.00119 / 255**;
  worst fraction of pixels with a channel difference above 12: **0.000153%**.
- Original browser/WebGL versus Godot GPU rendering: **14 captures**, worst mean channel error **0.05065 / 255**.
  Existing acceptance thresholds remain unchanged: mean ≤ 0.25, fraction above 12 ≤ 0.25%.
- Visibility, iris-mask removal, layer reordering, independent instances and repeated clear/reload checks pass.
- Desktop mouse sampling is independent of actor transforms; paused simulation does not advance.
- A separate project containing only the addon and sample model builds and loads without implicit usings,
  demo code, converter dependencies or enabling the editor plugin.
- Demo capture verified visually. Build: **0 warnings, 0 errors**. Generated shader bindings and `git diff --check` pass.

## CPU submission measurement

Debug build; median of five 120-step batches after warm-up. Each step includes simulation and Godot resource submission.
This measures CPU-side cost, **not GPU execution time or end-to-end FPS**.

| Sample | CPU reference | GPU deformation | Per-step vertex upload, CPU → GPU |
| --- | ---: | ---: | ---: |
| A | ~0.12 ms | ~0.01 ms | 4,976 → 0 bytes |
| B | ~0.22 ms | ~0.01 ms | 8,808 → 0 bytes |

GPU mode uploads a 256-byte pose texture plus per-part scalar/spring uniforms instead of vertices.
Measured managed allocation is ~224 bytes per GPU step from the submission path; the CPU reference loop has no
measurable steady-state managed allocation beyond the measurement harness. This is not a zero-allocation GPU claim.

## Reproduce

From the repository root, with Node dependencies installed in `tools` and Godot on the path:

```sh
node tools/reference.cjs
dotnet run --project tests/CoreTests.csproj -- .
node tools/shader-bindings.cjs --check
dotnet build --nologo
godot --path . -- --render-tests
node tools/render-reference.cjs
godot --path . --disable-vsync -- --backend-tests
node tools/verify-consumer.cjs
godot --path . -- --capture-demo
```

`verify-consumer.cjs` accepts `GODOT_BIN`; other commands above assume `godot` resolves to the Mono executable.
Reports are generated under `artifacts/core-tests.json`, `artifacts/image-comparison.json`,
`artifacts/backend-checks/report.json` and `artifacts/consumer-test.json`.
Failed GPU comparisons save both images for inspection. The numerical oracle is generated from pinned original
JavaScript, not from the refactored implementation.

## Not claimed

No validation yet for other rendering backends/platforms, packaged exports, multi-monitor hardware layouts,
large simultaneous character counts, sustained memory soak tests, or unrelated user-authored models.
GPU image parity is tolerance-based; it is not bit-identical floating-point computation.
