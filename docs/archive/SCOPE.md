# 0.1 acceptance scope

Agreed September 15, 2026.

- Independent Godot 4.7.2 Mono / C# project; runtime lives entirely in `addons/anime25d`.
- Faithful port of Anime2.5DRig parameters, defaults, formulas and visible behavior.
- Preserve idle, random movement, blink (including eye_close2), random talk, mouse follow, breathing, hair and bust physics, facial presets, layer depth/order/opacity.
- Runtime consumes converted textures and rig data. JavaScript tools perform PSD parsing, cleaning, auto-rigging and missing-diff synthesis outside the addon.
- Original sample A (`sample2.psd`) and B (`sample.psd`) are the acceptance assets.
- No VN defaults, VN integration, camera, microphone, tracking relay, OBS or Web target.
- The demo is a comparison harness, not an editor product. PSD authoring and complete settings/project UI are out of scope.
- Compare every vertex and opacity against the pinned original JavaScript for fixed poses and seeded animation sequences. Verify actual Godot rendering, clipping, transparency and multiple independent instances.

## Reference

https://github.com/852wa/Anime2.5DRig

Local reference commit: `7450341934a8ff77bf05b90d9f708786e3eb3996`.
The source snapshot in `tools/vendor/anime25d` is used by the converter and numerical oracle only; the addon has no JavaScript dependency.
