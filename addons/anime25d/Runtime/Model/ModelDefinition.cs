using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Anime25D.Runtime;

public sealed record LayerDefinition(string Id, MeshDefinition Mesh, int TextureSlot = 0,
    int DrawOrder = 0, double Opacity = 1, string? MaskId = null)
{
    public Matrix3x2 Transform { get; init; } = Matrix3x2.Identity;
}
public sealed class MaskDefinition
{
    public string Id { get; }
    public IReadOnlyList<string> Sources { get; }
    public float SourceThreshold { get; }
    public float ReceiverThreshold { get; }
    public MaskDefinition(string id, IEnumerable<string> sources, float sourceThreshold = 0.25f, float receiverThreshold = 0.5f)
    {
        Id = id; Sources = Array.AsReadOnly(sources.ToArray()); SourceThreshold = sourceThreshold; ReceiverThreshold = receiverThreshold;
        if (string.IsNullOrWhiteSpace(id) || !float.IsFinite(sourceThreshold) || sourceThreshold < 0 || sourceThreshold > 1 ||
            !float.IsFinite(receiverThreshold) || receiverThreshold <= 0 || receiverThreshold > 1 || Sources.Distinct().Count() != Sources.Count)
            throw new ArgumentException("Invalid binary mask definition.");
    }
}

public sealed class ModelDefinition
{
    public AnimationModel Animation { get; }
    public ModelPlan Plan { get; }
    public IReadOnlyList<LayerDefinition> Layers { get; }
    public IReadOnlyList<MaskDefinition> Masks { get; }
    public Bounds2D Bounds { get; }
    public int CanvasWidth { get; }
    public int CanvasHeight { get; }
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);
    public ModelDefinition(AnimationModel animation, int canvasWidth, int canvasHeight, IEnumerable<LayerDefinition> layers,
        IEnumerable<MaskDefinition>? masks = null, Bounds2D? bounds = null, ModelPlan? plan = null)
    {
        Animation = animation ?? throw new ArgumentNullException(nameof(animation));
        if (canvasWidth < 1 || canvasHeight < 1 || canvasWidth > 16384 || canvasHeight > 16384) throw new ArgumentException("Invalid canvas.");
        CanvasWidth = canvasWidth; CanvasHeight = canvasHeight;
        Bounds = bounds ?? new(-canvasWidth, -canvasHeight, canvasWidth * 3, canvasHeight * 3);
        if (!Bounds.IsValid) throw new ArgumentException("Invalid conservative bounds.");
        var copy = layers.ToArray();
        if (copy.Length == 0 || copy.Length > 4096) throw new ArgumentException("Invalid layer count.");
        for (int i = 0; i < copy.Length; i++)
        {
            var layer = copy[i];
            if (layer is null || string.IsNullOrWhiteSpace(layer.Id) || layer.Mesh is null || layer.TextureSlot < 0 ||
                !double.IsFinite(layer.Opacity) || layer.Opacity < 0 || layer.Opacity > 1 || !Finite(layer.Transform) || !indices.TryAdd(layer.Id, i))
                throw new ArgumentException("Invalid or duplicate layer.");
        }
        var maskCopy = (masks ?? Array.Empty<MaskDefinition>()).ToArray();
        var maskIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mask in maskCopy)
        {
            if (mask is null || !maskIds.Add(mask.Id)) throw new ArgumentException("Duplicate mask.");
            foreach (var source in mask.Sources)
                if (copy[LayerIndex(source)].MaskId is not null) throw new ArgumentException("Nested or self-referencing masks are unsupported.");
        }
        foreach (var layer in copy)
            if (layer.MaskId is not null && !maskIds.Contains(layer.MaskId)) throw new ArgumentException("Unknown mask.");
        Layers = Array.AsReadOnly(copy); Masks = Array.AsReadOnly(maskCopy);
        Plan = plan ?? new ModelPlan(); Plan.Validate(this);
    }
    public int LayerIndex(string id) => indices.TryGetValue(id, out int index) ? index : throw new ArgumentException($"Unknown layer: {id}");
    internal static bool Finite(Matrix3x2 m) => float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M31) && float.IsFinite(m.M32);
}
