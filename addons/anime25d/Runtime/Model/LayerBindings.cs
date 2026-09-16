using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Anime25D.Runtime;

public sealed class LayerBindingComponent : ModelComponent
{
    private readonly (int Layer, int Parameter, LayerProperty Property, Func<double,double> Map, Vector2 Pivot)[] bindings;
    public LayerBindingComponent(ModelDefinition model, IEnumerable<LayerParameterBinding> bindings)
    { this.bindings = bindings.Select(b => (model.LayerIndex(b.Layer), model.Animation.Parameters.IndexOf(b.Parameter), b.Property, (Func<double,double>)(v => v * b.Scale + b.Offset), b.Pivot)).ToArray(); }
    public LayerBindingComponent(ModelDefinition model, string layer, string parameter, LayerProperty property, Func<double,double> map)
    { bindings = [(model.LayerIndex(layer), model.Animation.Parameters.IndexOf(parameter), property, map, default)]; }
    public override void EvaluateOutput(ComponentContext c)
    {
        foreach (var (layer, parameter, property, map, pivot) in bindings)
        {
            double value = map(c.Pose[parameter]); var target = c.Layers[layer];
            if (property == LayerProperty.Opacity) target.Opacity *= value;
            else target.Transform *= property switch {
                LayerProperty.TranslationX => Matrix3x2.CreateTranslation((float)value, 0), LayerProperty.TranslationY => Matrix3x2.CreateTranslation(0, (float)value),
                LayerProperty.Rotation => Matrix3x2.CreateRotation((float)value, pivot), LayerProperty.ScaleX => Matrix3x2.CreateScale((float)value, 1, pivot),
                LayerProperty.ScaleY => Matrix3x2.CreateScale(1, (float)value, pivot), _ => throw new ArgumentException("Invalid layer property.") };
        }
    }
}
public sealed class LayerSelector : ModelComponent
{
    private readonly int[] layers;
    private readonly int parameter;
    public LayerSelector(ModelDefinition model, string parameter, IEnumerable<string> layers)
    { this.parameter = model.Animation.Parameters.IndexOf(parameter); this.layers = layers.Select(model.LayerIndex).ToArray(); }
    public override void EvaluateOutput(ComponentContext c)
    { int selected = (int)Math.Round(c.Pose[parameter]); for (int i = 0; i < layers.Length; i++) c.Layers[layers[i]].Visible &= i == selected; }
}
public sealed class LayerOpacityBinding : ModelComponent
{
    private readonly int layer;
    private readonly Func<ComponentContext, double> evaluate;
    public LayerOpacityBinding(ModelDefinition model, string layer, Func<ComponentContext, double> evaluate)
    { this.layer = model.LayerIndex(layer); this.evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate)); }
    public override void EvaluateOutput(ComponentContext c) => c.Layers[layer].Opacity *= evaluate(c);
}
