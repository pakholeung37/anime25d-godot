using System;
using System.Collections.Generic;
using System.Linq;

namespace Anime25D.Runtime;

public enum ModelStage { BasePose, FinalPose, Derived, Layers }
public sealed record ChannelDefinition(string Id, int Length = 1);
public sealed class FrameChannels
{
    private readonly Dictionary<string, double[]> values;
    internal FrameChannels(IEnumerable<ChannelDefinition> definitions) => values = definitions.ToDictionary(d => d.Id, d => new double[d.Length], StringComparer.Ordinal);
    public ReadOnlySpan<double> this[string id] => values[id];
    public double Scalar(string id) => values[id][0];
    internal void Reset() { foreach (var value in values.Values) Array.Clear(value); }
    internal Span<double> Write(string id) => values[id];
    internal void Validate() { foreach (var pair in values) foreach (double v in pair.Value) if (!double.IsFinite(v)) throw new InvalidOperationException($"Nonfinite channel {pair.Key}."); }
}
public sealed class ComponentContext
{
    private readonly PlanInstance owner;
    private readonly HashSet<string> outputs;
    internal ComponentContext(PlanInstance owner, IEnumerable<string> outputs) { this.owner = owner; this.outputs = new(outputs); }
    public ParameterSet Pose => owner.Pose;
    public ModelFrame Frame => owner.Frame;
    public T Component<T>(string id) where T : ModelComponent => owner.Get<T>(id);
    public FrameChannels Channels => owner.Frame.Channels;
    public LayerFrame[] Layers => owner.Frame.WritableLayers;
    public LayerOverrides[] Overrides => owner.Model.Layers;
    public double Time => owner.Model.ElapsedMilliseconds / 1000;
    public double TimeMilliseconds => owner.Model.ElapsedMilliseconds;
    public bool IsEnabled(string id) => owner.IsEnabled(id);
    public double Random() => owner.Random();
    public Span<double> Output(string id) => outputs.Contains(id) ? Channels.Write(id) : throw new InvalidOperationException($"Undeclared output: {id}");
}
public abstract class ModelComponent : IDisposable
{
    public virtual void AdvanceState(ComponentContext context, double deltaSeconds) { }
    public abstract void EvaluateOutput(ComponentContext context);
    public virtual void Reset() { }
    public virtual void Dispose() { }
}
public sealed class ComponentDefinition
{
    public string Id { get; }
    public ModelStage Stage { get; }
    public Func<ModelDefinition, ModelComponent> Create { get; }
    public IReadOnlyList<string> Reads { get; }
    public IReadOnlyList<string> Writes { get; }
    public bool Enabled { get; }
    public ComponentDefinition(string id, ModelStage stage, Func<ModelDefinition, ModelComponent> create,
        IEnumerable<string>? reads = null, IEnumerable<string>? writes = null, bool enabled = true)
    {
        if (string.IsNullOrWhiteSpace(id) || !Enum.IsDefined(stage)) throw new ArgumentException("Invalid component.");
        Id = id; Stage = stage; Create = create ?? throw new ArgumentNullException(nameof(create)); Enabled = enabled;
        Reads = Array.AsReadOnly((reads ?? []).ToArray()); Writes = Array.AsReadOnly((writes ?? []).ToArray());
    }
}
public abstract class DeformerDefinition
{
    public string Id { get; }
    public IReadOnlyList<string> Layers { get; }
    public IReadOnlyList<string> Reads { get; }
    protected DeformerDefinition(string id, IEnumerable<string> layers, IEnumerable<string>? reads = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Invalid deformer ID.");
        Id = id; Layers = Array.AsReadOnly(layers.ToArray()); Reads = Array.AsReadOnly((reads ?? []).ToArray());
    }
    public virtual void Validate(ModelDefinition model) { }
    public abstract void Deform(int layer, ModelFrame frame, ReadOnlySpan<float> rest, ReadOnlySpan<float> input, Span<float> output);
}
/// <summary>Ordered, immutable model-local capabilities. Factories must create independent instance state.</summary>
public sealed class ModelPlan
{
    public IReadOnlyList<ComponentDefinition> Components { get; }
    public IReadOnlyList<ChannelDefinition> Channels { get; }
    public IReadOnlyList<DeformerDefinition> Deformers { get; }
    internal Func<Func<double>> CreateRandom { get; }
    public ModelPlan(IEnumerable<ComponentDefinition>? components = null, IEnumerable<ChannelDefinition>? channels = null,
        IEnumerable<DeformerDefinition>? deformers = null, Func<Func<double>>? random = null)
    {
        Components = Array.AsReadOnly((components ?? []).ToArray()); Channels = Array.AsReadOnly((channels ?? []).ToArray());
        Deformers = Array.AsReadOnly((deformers ?? []).ToArray()); CreateRandom = random ?? (() => new Random().NextDouble);
        var ids = new HashSet<string>();
        foreach (var c in Channels) if (c is null || string.IsNullOrWhiteSpace(c.Id) || c.Length < 1 || !ids.Add(c.Id)) throw new ArgumentException("Invalid/duplicate channel.");
        ids.Clear(); var produced = new HashSet<string>(); var stage = ModelStage.BasePose;
        foreach (var c in Components)
        {
            if (c is null || !ids.Add(c.Id) || c.Stage < stage) throw new ArgumentException("Duplicate component or out-of-order stage.");
            stage = c.Stage;
            foreach (var read in c.Reads) if (!produced.Contains(read)) throw new ArgumentException($"Read before write: {read} in {c.Id}.");
            foreach (var write in c.Writes) if (!Channels.Any(d => d.Id == write) || !produced.Add(write)) throw new ArgumentException($"Missing/duplicate channel output {write}.");
        }
        if (produced.Count != Channels.Count) throw new ArgumentException("Every channel needs a producer.");
        ids.Clear();
        foreach (var d in Deformers)
        {
            if (d is null || !ids.Add(d.Id)) throw new ArgumentException("Duplicate deformer.");
            foreach (var read in d.Reads) if (!produced.Contains(read)) throw new ArgumentException($"Unknown deformer channel {read}.");
        }
    }
    internal void Validate(ModelDefinition model)
    {
        foreach (var d in Deformers)
        {
            if (d.Layers.Count == 0 || d.Layers.Distinct().Count() != d.Layers.Count) throw new ArgumentException("Invalid deformer targets.");
            foreach (string layer in d.Layers) model.LayerIndex(layer);
            d.Validate(model);
        }
    }
}
internal sealed class PlanInstance : IDisposable
{
    internal ModelInstance Model { get; }
    internal ModelFrame Frame = null!;
    internal ParameterSet Pose = null!;
    internal Func<double> Random { get; }
    private readonly List<(ComponentDefinition Definition, ModelComponent Component, ComponentContext Context)> components = new();
    private readonly Dictionary<string, bool> enabled = new();
    private readonly DeformerDefinition[][] deformers;
    private readonly float[][] scratch;
    internal PlanInstance(ModelInstance model)
    {
        Model = model; var plan = model.Definition.Plan; Random = plan.CreateRandom();
        deformers = model.Definition.Layers.Select(l => plan.Deformers.Where(d => d.Layers.Contains(l.Id)).ToArray()).ToArray();
        scratch = new float[model.Definition.Layers.Count][];
        try
        {
            foreach (var definition in plan.Components)
            {
                var component = definition.Create(model.Definition) ?? throw new ArgumentException("Component factory returned null.");
                components.Add((definition, component, new(this, definition.Writes))); enabled.Add(definition.Id, definition.Enabled);
            }
        }
        catch { Dispose(); throw; }
    }
    internal bool IsEnabled(string id) => enabled.TryGetValue(id, out var value) ? value : throw new ArgumentException($"Unknown component: {id}");
    internal void SetEnabled(string id, bool value) { _ = IsEnabled(id); enabled[id] = value; }
    internal T Get<T>(string id) where T : ModelComponent => (T)components.Single(c => c.Definition.Id == id).Component;
    internal void Reset() { foreach (var c in components) c.Component.Reset(); }
    internal void Snap(string parameter, double value) { foreach (var c in components) if (c.Component is SmoothDriver smooth) smooth.SetImmediate(parameter, Model.Definition.Animation.Parameters, value); }
    internal void Begin(ModelFrame frame) { Frame = frame; frame.Channels.Reset(); }
    internal void Run(ModelStage stage, ParameterSet pose, double delta, bool advance)
    {
        Pose = pose;
        foreach (var c in components)
        {
            if (c.Definition.Stage != stage || !enabled[c.Definition.Id]) continue;
            if (advance) c.Component.AdvanceState(c.Context, delta);
            c.Component.EvaluateOutput(c.Context);
        }
    }
    internal void Deform(int layer, ModelFrame frame, ReadOnlySpan<float> rest, Span<float> output)
    {
        rest.CopyTo(output);
        foreach (var deformer in deformers[layer])
        {
            scratch[layer] ??= new float[rest.Length];
            output.CopyTo(scratch[layer]);
            deformer.Deform(layer, frame, rest, scratch[layer], output);
        }
    }
    public void Dispose()
    {
        List<Exception>? errors = null;
        for (int i = components.Count - 1; i >= 0; i--) try { components[i].Component.Dispose(); } catch (Exception e) { (errors ??= new()).Add(e); }
        components.Clear();
        if (errors is not null) throw new AggregateException(errors);
    }
}
