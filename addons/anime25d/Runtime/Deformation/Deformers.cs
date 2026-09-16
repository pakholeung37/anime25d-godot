using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Anime25D.Runtime;

public sealed class AffineDeformer : DeformerDefinition
{
    public Matrix3x2 Transform { get; }
    public AffineDeformer(string id, IEnumerable<string> layers, Matrix3x2 transform) : base(id, layers)
    { if (!ModelDefinition.Finite(transform)) throw new ArgumentException("Invalid transform."); Transform = transform; }
    public override void Deform(int layer, ModelFrame frame, ReadOnlySpan<float> rest, ReadOnlySpan<float> input, Span<float> output)
    { for (int i = 0; i < input.Length; i += 2) { var p = Vector2.Transform(new(input[i], input[i + 1]), Transform); output[i] = p.X; output[i + 1] = p.Y; } }
}
public sealed class VertexOffsetDeformer : DeformerDefinition
{
    private readonly float[] offsets;
    public ReadOnlySpan<float> Offsets => offsets;
    public string Parameter { get; }
    public VertexOffsetDeformer(string id, IEnumerable<string> layers, string parameter, ReadOnlySpan<float> offsets) : base(id, layers)
    { Parameter = parameter; this.offsets = offsets.ToArray(); if (this.offsets.Any(v => !float.IsFinite(v))) throw new ArgumentException("Invalid offsets."); }
    public override void Validate(ModelDefinition model)
    { model.Animation.Parameters.IndexOf(Parameter); foreach (var id in Layers) if (model.Layers[model.LayerIndex(id)].Mesh.VertexCount * 2 != offsets.Length) throw new ArgumentException("Offset count mismatch."); }
    public override void Deform(int layer, ModelFrame frame, ReadOnlySpan<float> rest, ReadOnlySpan<float> input, Span<float> output)
    { double weight = frame.Pose[Parameter]; for (int i = 0; i < input.Length; i++) output[i] = (float)(input[i] + offsets[i] * weight); }
}

