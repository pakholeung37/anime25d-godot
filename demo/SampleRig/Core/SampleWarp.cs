using System;
using System.Collections.Generic;
using System.Linq;
using Anime25D.Runtime;
namespace Anime25D.Sample.Core;

public sealed record SamplePart(PartDefinition Definition, RigMeshGeometry Geometry);
public readonly struct SamplePose(ReadOnlyPose pose, int[] indices)
{
    public double this[Parameter key] => pose[indices[(int)key]];
    public double Breath => pose[indices[SampleParameters.Specs.Count]];
    public double BreathHead => pose[indices[SampleParameters.Specs.Count + 1]];
    public double IrisBounceX => pose[indices[SampleParameters.Specs.Count + 2]];
    public double IrisBounceY => pose[indices[SampleParameters.Specs.Count + 3]];
}
/// <summary>One pure composite geometry algorithm. State is supplied entirely by the model frame.</summary>
public sealed class SampleWarp : DeformerDefinition
{
    private readonly int[] parameterIndices, springOffsets;
    public RigDefinition Rig { get; }
    public RigProfile Profile { get; }
    public IReadOnlyList<SamplePart> Parts { get; }
    public SampleWarp(RigDefinition rig, RigProfile profile, RigMeshGeometry[] geometry, AnimationModel animation)
        : base("sample-warp", rig.Layers.Select((_, i) => "layer-" + i), ["spring-displacements", "physics-enabled"])
    {
        parameterIndices = SampleParameters.Specs.Select(p => p.Key.ToString()).Concat(new[] {SampleAnimations.Breath, SampleAnimations.BreathHead, SampleAnimations.IrisX, SampleAnimations.IrisY}).Select(animation.Parameters.IndexOf).ToArray();
        springOffsets = new int[rig.Layers.Length]; int offset = 0;
        for (int i = 0; i < springOffsets.Length; i++) { springOffsets[i] = offset; offset += (rig.Layers[i].Strands?.Length ?? 0) * 2; }
        Rig = rig; Profile = profile; Parts = Array.AsReadOnly(rig.Layers.Select((p, i) => new SamplePart(p, geometry[i])).ToArray()); }
    public override void Validate(ModelDefinition model)
    {
        var names = SampleParameters.Specs.Select(p => p.Key.ToString()).Concat(new[] {SampleAnimations.Breath, SampleAnimations.BreathHead, SampleAnimations.IrisX, SampleAnimations.IrisY}).ToArray();
        for (int i = 0; i < names.Length; i++) if (model.Animation.Parameters.IndexOf(names[i]) != parameterIndices[i]) throw new ArgumentException("Sample warp was compiled for a different parameter layout.");
        foreach (var id in new[] {SampleAnimations.Breath, SampleAnimations.BreathHead, SampleAnimations.IrisX, SampleAnimations.IrisY}) model.Animation.Parameters.IndexOf(id);
    }
    public override void Deform(int layer, ModelFrame frame, ReadOnlySpan<float> rest, ReadOnlySpan<float> input, Span<float> output)
    {
        var parameters = new DeformationInput(Rig.Anchors, new SamplePose(frame.Pose, parameterIndices), Profile.Deformation,
            frame.Channels["spring-displacements"][^1], frame.Channels.Scalar("physics-enabled") > 0, frame, springOffsets[layer], frame.Pose[DepthParameter(layer)]);
        RigDeformer.Deform(Parts[layer], parameters, input, output);
    }
    public static string DepthParameter(int layer) => "LayerDepth" + layer;
    public int SpringOffset(int layer) => springOffsets[layer];
}
