using System;
using System.Linq;
using System.Numerics;

namespace Anime25D.Runtime;

public sealed class LayerOverrides
{
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; } = 1;
    public int? DrawOrder { get; set; }
    public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;
}
public sealed class LayerFrame
{
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; } = 1;
    public int DrawOrder { get; set; }
    public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;
    internal float[] Vertices = Array.Empty<float>();
    public ReadOnlySpan<float> Positions => Vertices;
}
public sealed class LayerFrameView
{
    private readonly LayerFrame value;
    internal LayerFrameView(LayerFrame value) => this.value = value;
    public bool Visible => value.Visible;
    public double Opacity => value.Opacity;
    public int DrawOrder => value.DrawOrder;
    public Matrix3x2 Transform => value.Transform;
    public ReadOnlySpan<float> Positions => value.Positions;
}
public sealed class ModelFrame
{
    public long Version { get; internal set; }
    public FrameChannels Channels { get; }
    public ModelDefinition Definition { get; }
    internal ParameterSet Values { get; }
    public ReadOnlyPose Pose => Values.ReadOnly;
    private readonly LayerFrame[] layers;
    public System.Collections.Generic.IReadOnlyList<LayerFrameView> Layers { get; }
    public bool HasCpuGeometry { get; internal set; }
    internal LayerFrame[] WritableLayers => layers;
    internal ModelFrame(ModelDefinition definition)
    {
        Definition = definition;
        Values = new(definition.Animation.Parameters);
        Channels = new(definition.Plan.Channels);
        layers = definition.Layers.Select(_ => new LayerFrame()).ToArray();
        Layers = Array.AsReadOnly(layers.Select(l => new LayerFrameView(l)).ToArray());
    }
}
public sealed record CpuGeometrySnapshot(long FrameVersion, float[][] Positions);

/// <summary>Complete model evaluation, independent of the display backend. Frame views expire on next evaluation.</summary>
public sealed class ModelInstance : IDisposable
{
    public const double RenderThreshold = 0.004;
    public ModelDefinition Definition { get; }
    public AnimationRuntime Animation { get; }
    public LayerOverrides[] Layers { get; }
    public event Action<ParameterSet>? FinalizingPose;
    public ModelFrame Frame { get; private set; }
    public bool EvaluateCpuGeometry { get; set; } = true;
    public bool Faulted { get; private set; }
    public bool IsDisposed { get; private set; }
    public double ElapsedMilliseconds { get; private set; }
    private ModelFrame back;
    private readonly PlanInstance plan;
    public void SetParameter(string id, double value, bool immediate = false) { CheckCommand(); Animation.BasePose[id] = value; if (immediate) plan.Snap(id, Animation.BasePose[id]); }
    public void ResetInputs() { CheckCommand(); Animation.BasePose.Reset(); plan.Reset(); }
    public void SetComponentEnabled(string id, bool enabled) { CheckCommand(); plan.SetEnabled(id, enabled); }
    public bool IsComponentEnabled(string id) => plan.IsEnabled(id);
    public T Component<T>(string id) where T : ModelComponent => plan.Get<T>(id);
    private void CheckCommand() { if (IsDisposed || Faulted || evaluating) throw new InvalidOperationException("Cannot change model during evaluation or after disposal/failure."); }
    private bool evaluating;
    public ModelInstance(ModelDefinition definition)
    {
        Definition = definition;
        Animation = new(definition.Animation);
        Frame = new(definition); back = new(definition);
        Layers = definition.Layers.Select(_ => new LayerOverrides()).ToArray();
        plan = new(this);
    }
    public void Advance(double deltaSeconds, bool dispatchEvents = true)
    {
        Evaluate(deltaSeconds, true);
        if (dispatchEvents && !Animation.Paused) Animation.DispatchEvents();
    }
    public void Refresh() => Evaluate(0, false);
    private void Evaluate(double delta, bool advance)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ModelInstance));
        if (Faulted || evaluating) throw new InvalidOperationException("Model is faulted or already evaluating.");
        if (!double.IsFinite(delta) || delta < 0 || !double.IsFinite(ElapsedMilliseconds + delta * 1000)) throw new ArgumentOutOfRangeException(nameof(delta));
        if (advance && Animation.Paused) return;
        evaluating = true;
        try
        {
            if (advance) ElapsedMilliseconds += delta * 1000;
            plan.Begin(back);
            Animation.EvaluateFrame(delta, advance, pose => { plan.Run(ModelStage.BasePose, pose, delta, advance); });
            back.Values.CopyFrom(Animation.Pose);
            plan.Run(ModelStage.FinalPose, back.Values, delta, advance);
            FinalizingPose?.Invoke(back.Values);
            plan.Run(ModelStage.Derived, back.Values, delta, advance);
            for (int i = 0; i < Layers.Length; i++)
            {
                var frame = back.WritableLayers[i]; var definition = Definition.Layers[i];
                frame.Visible = true; frame.Opacity = 1; frame.DrawOrder = definition.DrawOrder; frame.Transform = Matrix3x2.Identity;
            }
            plan.Run(ModelStage.Layers, back.Values, delta, advance);
            back.Channels.Validate();
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = back.WritableLayers[i]; var definition = Definition.Layers[i]; var user = Layers[i];
                layer.Opacity *= definition.Opacity * user.Opacity;
                layer.Transform = definition.Transform * layer.Transform * user.Transform; // Numerics uses row vectors.
                if (!double.IsFinite(layer.Opacity) || !ModelDefinition.Finite(layer.Transform)) throw new InvalidOperationException("Nonfinite layer output.");
                layer.Opacity = Math.Clamp(layer.Opacity, 0, 1);
                layer.Visible &= user.Visible && layer.Opacity >= RenderThreshold;
                layer.DrawOrder = user.DrawOrder ?? layer.DrawOrder;
                if (EvaluateCpuGeometry)
                {
                    if (layer.Vertices.Length != definition.Mesh.VertexCount * 2) layer.Vertices = new float[definition.Mesh.VertexCount * 2];
                    if (layer.Visible) Deform(i, back, layer.Vertices);
                    else if (Frame.HasCpuGeometry) Frame.Layers[i].Positions.CopyTo(layer.Vertices);
                    else definition.Mesh.RestPositions.CopyTo(layer.Vertices);
                    foreach (float value in layer.Vertices) if (!float.IsFinite(value)) throw new InvalidOperationException("Nonfinite deformed vertex.");
                }
            }
            back.HasCpuGeometry = EvaluateCpuGeometry;
            back.Version = Frame.Version + 1;
            (Frame, back) = (back, Frame);
        }
        catch { Faulted = true; throw; }
        finally { evaluating = false; }
    }
    public CpuGeometrySnapshot EvaluateCpuSnapshot()
    {
        if (IsDisposed || Faulted || evaluating) throw new InvalidOperationException("Cannot inspect this model state.");
        var output = new float[Layers.Length][];
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = new float[Definition.Layers[i].Mesh.VertexCount * 2];
            Deform(i, Frame, output[i]);
            for (int j = 0; j < output[i].Length; j += 2)
            {
                var point = Vector2.Transform(new Vector2(output[i][j], output[i][j + 1]), Frame.Layers[i].Transform);
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) throw new InvalidOperationException("Nonfinite diagnostic geometry.");
                output[i][j] = point.X; output[i][j + 1] = point.Y;
            }
        }
        return new(Frame.Version, output);
    }
    private void Deform(int layer, ModelFrame frame, Span<float> output)
    {
        var rest = Definition.Layers[layer].Mesh.RestPositions;
        plan.Deform(layer, frame, rest, output);
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        if (evaluating) throw new InvalidOperationException("Cannot dispose during evaluation.");
        IsDisposed = true; Animation.DiscardEvents(); plan.Dispose();
    }
}
