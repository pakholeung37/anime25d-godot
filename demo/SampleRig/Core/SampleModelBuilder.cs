using System;
using System.Collections.Generic;
using System.Linq;
using Anime25D.Runtime;
namespace Anime25D.Sample.Core;

/// <summary>Model data and capability bindings; execution is entirely owned by ModelInstance.</summary>
public static class SampleModelBuilder
{
    public static ModelDefinition Build(RigDefinition source, RigProfile? profile = null, AnimationModel? animations = null, Func<double>? random = null)
    {
        var rig = source with { Layers = source.Layers.Select(l => l with { Strands = l.Strands?.ToArray() }).ToArray(), Warnings = source.Warnings.ToArray() };
        var config = (profile ?? new RigProfile()).CreateSnapshot();
        var original = animations ?? SampleAnimations.Create(config);
        var catalog = new AnimationModel(original.Parameters.Definitions.Concat(rig.Layers.Select((p, i) => new ParameterDefinition(SampleWarp.DepthParameter(i), p.Depth, 0, 2))), original.Motions, original.Expressions);
        var masks = new List<MaskDefinition>();
        foreach (var side in new[] { PartSide.Left, PartSide.Right })
            if (rig.Layers.Any(l => l.Role == PartRole.Iris && l.Side == side)) masks.Add(new(side.ToString(), rig.Layers.Select((l, i) => (l, i)).Where(p => p.l.Role == PartRole.EyeWhite && p.l.Side == side).Select(p => "layer-" + p.i)));
        var geometries = rig.Layers.Select(part => new RigMeshGeometry(part, rig, config)).ToArray();
        var layers = rig.Layers.Select((part, i) => new LayerDefinition("layer-" + i, new MeshDefinition(geometries[i].RestPositions, geometries[i].TextureCoordinates, geometries[i].TriangleIndices), part.TextureIndex, part.InitialDrawOrder, part.Opacity, part.Role == PartRole.Iris && part.Side != PartSide.None ? part.Side.ToString() : null));
        var components = new List<ComponentDefinition>(); var m = config.Motion;
        SineBinding Wave(string parameter, Wave w) => new(parameter, w.Amplitude, w.AngularFrequency, w.Phase);
        components.Add(new("Idle", ModelStage.BasePose, d => new SineDriver(d, Wave("HeadYaw", m.Idle.YawPrimary), Wave("HeadYaw", m.Idle.YawSecondary), Wave("HeadPitch", m.Idle.Pitch), Wave("HeadRoll", m.Idle.Roll), Wave("BodyRoll", m.Idle.BodyRoll))));
        components.Add(new("Random", ModelStage.BasePose, _ => new SampleRandomDriver(m.Random)));
        components.Add(new("Talk", ModelStage.BasePose, _ => new SampleTalkDriver(m.Talk)));
        components.Add(new("Blink", ModelStage.BasePose, _ => new SampleBlinkDriver(m.Blink, rig.Layers.Any(p => p.Role == PartRole.AlternateClosedEye))));
        components.Add(new("Smoothing", ModelStage.BasePose, d => new SmoothDriver(d, config.CreateCatalog().Select(p => p.Key.ToString()), m.SmoothingRate)));
        components.Add(new("Breath", ModelStage.BasePose, d => new SineDriver(d,
            new(SampleAnimations.Breath, .5, 2 * Math.PI / m.Breath.PeriodSeconds, 0, .5, BlendMode.Override),
            new(SampleAnimations.BreathHead, .5, 2 * Math.PI / m.Breath.PeriodSeconds, -m.Breath.HeadPhaseLagRadians, .5, BlendMode.Override))));
        int count = rig.Layers.Sum(p => (p.Strands?.Length ?? 0) * 2) + 1;
        components.Add(new("SpringTargets", ModelStage.Derived, _ => new ChannelWriter("spring-targets", (c, output) => Targets(c, rig, config, output)), writes: ["spring-targets"]));
        var springs = rig.Layers.SelectMany(p => Enumerable.Range(0, p.Strands?.Length ?? 0).SelectMany(_ => new[] { config.Physics.StiffHair, config.Physics.SoftHair })).Append(config.Physics.Chest).Select(s => new SpringBinding(s.Stiffness, s.Damping, s.DisplacementScale)).ToArray();
        components.Add(new("Springs", ModelStage.Derived, _ => new SpringBank("spring-targets", "spring-displacements", springs, config.Physics.IntegrationStepSeconds), reads: ["spring-targets"], writes: ["spring-displacements"]));
        components.Add(new("Physics", ModelStage.Derived, _ => new ChannelWriter("physics-enabled", (c, output) => output[0] = 1), writes: ["physics-enabled"]));
        components.Add(new("EyeSelection", ModelStage.Layers, _ => new EyeSelection(rig), writes: ["alternate-eyes"]));
        for (int i = 0; i < rig.Layers.Length; i++)
        {
            int layer = i;
            components.Add(new("Fade" + i, ModelStage.Layers, d => new LayerOpacityBinding(d, "layer-" + layer, c => LayerFade(c, rig.Layers[layer], config.Deformation)), reads: ["alternate-eyes"]));
        }
        var plan = new ModelPlan(components, [new("spring-targets", count), new("spring-displacements", count), new("physics-enabled"), new("alternate-eyes", 3)], [new SampleWarp(rig, config, geometries, catalog)], random is null ? null : () => random);
        return new(catalog, rig.Canvas.Width, rig.Canvas.Height, layers, masks, plan: plan);
    }
    private static void Targets(ComponentContext c, RigDefinition rig, RigProfile config, Span<double> output)
    {
        var pose = c.Pose; var s = config.Physics; var d = config.Deformation; var a = rig.Anchors;
        double head = (pose["HeadYaw"] * d.HeadYawPixels + pose["HeadRoll"] * d.HeadRollRadians * (a.NeckPivot.CenterY - a.Face.CenterY)) * a.FaceScale;
        int at = 0;
        foreach (var part in rig.Layers) for (int i = 0; i < (part.Strands?.Length ?? 0); i++)
        {
            double phase = i * s.StrandPhaseSpacing + part.InitialDrawOrder;
            double wind = c.IsEnabled("Idle") ? s.WindPrimaryAmplitude * Math.Sin(c.Time * s.WindPrimaryFrequency + phase) + s.WindSecondaryAmplitude * Math.Sin(c.Time * s.WindSecondaryFrequency + phase * s.WindSecondaryPhaseMultiplier) : 0;
            output[at++] = head + wind * a.FaceScale; output[at++] = head + wind * a.FaceScale;
        }
        output[^1] = (pose[SampleAnimations.Breath] * s.ChestBreathDrive - pose["HeadPitch"] * s.ChestPitchDrive + pose["BodyRoll"] * s.ChestBodyDrive) * a.FaceScale;
    }
    // Character-specific selection policy; output is a channel, not hidden mutable PartState.
    private sealed class EyeSelection(RigDefinition rig) : ModelComponent
    {
        public override void EvaluateOutput(ComponentContext c)
        {
            var result = c.Output("alternate-eyes"); result.Clear();
            if ((int)Math.Round(c.Pose[SampleAnimations.EyeVariant]) != 2) return;
            for (int i = 0; i < rig.Layers.Length; i++) if (rig.Layers[i].Fade == FadeMode.AlternateClosedEye && c.Overrides[i].Visible && c.Overrides[i].Opacity * rig.Layers[i].Opacity > 0) result[(int)rig.Layers[i].Side] = 1;
        }
    }
    private static double LayerFade(ComponentContext c, PartDefinition part, DeformationSettings settings)
    {
            double openness = c.Pose[part.Side == PartSide.Left ? "LeftEyeOpenness" : "RightEyeOpenness"];
            double eye = SignalMap.SmoothStep((openness - (settings.EyeFadeOffset + c.Pose["EyeTransition"] * settings.EyeFadeSensitivity)) / settings.EyeFadeWidth);
            double mouth = SignalMap.SmoothStep((c.Pose["MouthOpenness"] - (settings.MouthFadeOffset + c.Pose["MouthTransition"] * settings.MouthFadeSensitivity)) / settings.MouthFadeWidth);
            bool alternate = c.Channels["alternate-eyes"][(int)part.Side] > 0;
            return part.Fade switch {
                FadeMode.OpenEye => eye,
                FadeMode.ClosedEye or FadeMode.AlternateClosedEye => (part.Fade == FadeMode.AlternateClosedEye) != alternate ? 0 : 1 - eye,
                FadeMode.OpenMouth => mouth, FadeMode.ClosedMouth => 1 - mouth, _ => 1 };
        }
}
