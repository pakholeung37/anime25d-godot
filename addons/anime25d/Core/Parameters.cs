using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Anime25D.Core;

public sealed record ParameterDefinition(string Id, double Default = 0, double Minimum = -1, double Maximum = 1);
public enum BlendMode { Override, Add, Multiply }

/// <summary>Model-local channels. IDs have no built-in anatomical or rendering meaning.</summary>
public sealed class ParameterLayout
{
    public IReadOnlyList<ParameterDefinition> Definitions { get; }
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);
    public ParameterLayout(IEnumerable<ParameterDefinition> definitions)
    {
        var copy = definitions.ToArray();
        for (int i = 0; i < copy.Length; i++)
        {
            var p = copy[i];
            if (p is null || string.IsNullOrWhiteSpace(p.Id) || !double.IsFinite(p.Default) ||
                !double.IsFinite(p.Minimum) || !double.IsFinite(p.Maximum) || p.Minimum > p.Maximum ||
                p.Default < p.Minimum || p.Default > p.Maximum || !indices.TryAdd(p.Id, i))
                throw new ArgumentException("Parameters require unique IDs and finite, ordered ranges.");
        }
        Definitions = Array.AsReadOnly(copy);
    }
    public int IndexOf(string id) => indices.TryGetValue(id, out int index)
        ? index : throw new ArgumentException($"Unknown parameter: {id}");
}

public readonly struct ReadOnlyPose
{
    private readonly ParameterSet values;
    internal ReadOnlyPose(ParameterSet values) => this.values = values;
    public ParameterLayout Layout => values.Layout;
    public double this[string id] => values[id];
    public double this[int index] => values[index];
}

public sealed class ParameterSet
{
    public ReadOnlyPose ReadOnly => new(this);
    public ParameterLayout Layout { get; }
    private readonly double[] values;
    public ParameterSet(ParameterLayout layout)
    {
        Layout = layout;
        values = new double[layout.Definitions.Count];
        Reset();
    }
    public double this[string id] { get => this[Layout.IndexOf(id)]; set => this[Layout.IndexOf(id)] = value; }
    public double this[int index]
    {
        get => values[index];
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentException("Parameter values must be finite.");
            var p = Layout.Definitions[index];
            values[index] = Math.Clamp(value, p.Minimum, p.Maximum);
        }
    }
    public void Reset()
    {
        for (int i = 0; i < values.Length; i++) values[i] = Layout.Definitions[i].Default;
    }
    public void CopyFrom(ParameterSet source)
    {
        if (!ReferenceEquals(Layout, source.Layout)) throw new ArgumentException("Parameter layouts differ.");
        source.values.CopyTo(values, 0);
    }
    public void Blend(string id, double value, BlendMode mode = BlendMode.Override, double weight = 1)
    {
        if (!double.IsFinite(value) || !double.IsFinite(weight) || weight < 0 || weight > 1 || !Enum.IsDefined(mode))
            throw new ArgumentException("Invalid parameter blend.");
        int index = Layout.IndexOf(id);
        double current = values[index];
        double target = mode switch { BlendMode.Add => current + value, BlendMode.Multiply => current * value, _ => value };
        this[index] = current * (1 - weight) + target * weight;
    }
}
