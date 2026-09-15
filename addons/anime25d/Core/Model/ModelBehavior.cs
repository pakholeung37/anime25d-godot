using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Anime25D.Core;

/// <summary>Algorithm stages only. ModelInstance owns clocks, frame buffers and execution.</summary>
public interface IModelBehavior : IDisposable
{
    void PreparePose(ParameterSet pose, double deltaSeconds, bool advance);
    void ResolvePose(ParameterSet pose, double deltaSeconds, bool advance);
    void EvaluateLayers(ReadOnlyPose pose, LayerFrame[] layers);
    void DeformCpu(int layer, ReadOnlyPose pose, ReadOnlySpan<float> rest, Span<float> output);
}

public abstract class ModelBehavior : IModelBehavior
{
    public virtual void PreparePose(ParameterSet pose, double deltaSeconds, bool advance) { }
    public virtual void ResolvePose(ParameterSet pose, double deltaSeconds, bool advance) { }
    public virtual void EvaluateLayers(ReadOnlyPose pose, LayerFrame[] layers) { }
    public virtual void DeformCpu(int layer, ReadOnlyPose pose, ReadOnlySpan<float> rest, Span<float> output) => rest.CopyTo(output);
    public virtual void Dispose() { }
}

public enum LayerProperty { TranslationX, TranslationY, Rotation, ScaleX, ScaleY, Opacity }
public sealed record LayerParameterBinding(string Parameter, string Layer, LayerProperty Property, double Scale = 1, double Offset = 0, Vector2 Pivot = default);

/// <summary>Built-in 2D transforms; geometry needs no custom renderer or shader.</summary>
public sealed class BasicModelBehavior : ModelBehavior
{
    private readonly (int Parameter, int Layer, LayerParameterBinding Binding)[] bindings;
    public BasicModelBehavior() => bindings = Array.Empty<(int, int, LayerParameterBinding)>();
    public BasicModelBehavior(ModelDefinition model, IEnumerable<LayerParameterBinding> bindings)
    {
        var copy = bindings.ToArray();
        if (copy.Any(b => b is null || !Enum.IsDefined(b.Property) || !double.IsFinite(b.Scale) || !double.IsFinite(b.Offset) || !float.IsFinite(b.Pivot.X) || !float.IsFinite(b.Pivot.Y)))
            throw new ArgumentException("Invalid layer parameter binding.");
        this.bindings = copy.Select(b => (model.Animation.Parameters.IndexOf(b.Parameter), model.LayerIndex(b.Layer), b)).ToArray();
    }
    public override void EvaluateLayers(ReadOnlyPose pose, LayerFrame[] layers)
    {
        foreach (var (parameter, layer, b) in bindings)
        {
            float value = (float)(pose[parameter] * b.Scale + b.Offset);
            var target = layers[layer];
            var transform = b.Property switch
            {
                LayerProperty.TranslationX => Matrix3x2.CreateTranslation(value, 0),
                LayerProperty.TranslationY => Matrix3x2.CreateTranslation(0, value),
                LayerProperty.Rotation => Matrix3x2.CreateRotation(value, b.Pivot),
                LayerProperty.ScaleX => Matrix3x2.CreateScale(value, 1, b.Pivot),
                LayerProperty.ScaleY => Matrix3x2.CreateScale(1, value, b.Pivot),
                _ => Matrix3x2.Identity
            };
            if (b.Property == LayerProperty.Opacity) target.Opacity *= value;
            else target.Transform *= transform;
        }
    }
}
