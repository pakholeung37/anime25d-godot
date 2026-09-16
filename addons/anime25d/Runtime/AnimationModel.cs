using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Anime25D.Runtime;

public sealed record MotionTrack(string Parameter, MotionCurve Curve, BlendMode Blend = BlendMode.Override);
public sealed record ExpressionValue(string Parameter, double Value, BlendMode Blend = BlendMode.Override);

public sealed class MotionDefinition
{
    public double Duration { get; }
    public bool Loop { get; }
    public double FadeIn { get; }
    public double FadeOut { get; }
    public IReadOnlyList<MotionTrack> Tracks { get; }
    public MotionDefinition(double duration, IEnumerable<MotionTrack> tracks, bool loop = false, double fadeIn = 0.2, double fadeOut = 0.2)
    {
        if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentException("Motion duration must be positive and finite.");
        ValidateFade(fadeIn); ValidateFade(fadeOut);
        var copy = tracks.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in copy)
            if (track is null || string.IsNullOrWhiteSpace(track.Parameter) || track.Curve is null ||
                track.Curve.EndTime > duration || !ids.Add(track.Parameter) || !Enum.IsDefined(track.Blend))
                throw new ArgumentException("Invalid or duplicate motion track, or keys beyond duration.");
        Duration = duration; Loop = loop; FadeIn = fadeIn; FadeOut = fadeOut;
        Tracks = Array.AsReadOnly(copy);
    }
    internal static void ValidateFade(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Fade duration must be finite and nonnegative.");
    }
}

public sealed class ExpressionDefinition
{
    public double FadeIn { get; }
    public double FadeOut { get; }
    public IReadOnlyList<ExpressionValue> Values { get; }
    public ExpressionDefinition(IEnumerable<ExpressionValue> values, double fadeIn = 0.2, double fadeOut = 0.2)
    {
        MotionDefinition.ValidateFade(fadeIn); MotionDefinition.ValidateFade(fadeOut);
        var copy = values.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in copy)
            if (value is null || string.IsNullOrWhiteSpace(value.Parameter) || !double.IsFinite(value.Value) ||
                !ids.Add(value.Parameter) || !Enum.IsDefined(value.Blend)) throw new ArgumentException("Invalid expression value.");
        Values = Array.AsReadOnly(copy); FadeIn = fadeIn; FadeOut = fadeOut;
    }
}

/// <summary>Code-authored, shareable assets. No playback state, file IO, or rendering assumptions.</summary>
public sealed class AnimationModel
{
    public ParameterLayout Parameters { get; }
    public IReadOnlyDictionary<string, MotionDefinition> Motions { get; }
    public IReadOnlyDictionary<string, ExpressionDefinition> Expressions { get; }
    public AnimationModel(IEnumerable<ParameterDefinition> parameters,
        IReadOnlyDictionary<string, MotionDefinition>? motions = null,
        IReadOnlyDictionary<string, ExpressionDefinition>? expressions = null)
    {
        Parameters = new(parameters);
        var m = new Dictionary<string, MotionDefinition>(StringComparer.Ordinal);
        foreach (var pair in motions ?? new Dictionary<string, MotionDefinition>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) throw new ArgumentException("Invalid motion registration.");
            foreach (var track in pair.Value.Tracks) Parameters.IndexOf(track.Parameter);
            m.Add(pair.Key, pair.Value);
        }
        var e = new Dictionary<string, ExpressionDefinition>(StringComparer.Ordinal);
        foreach (var pair in expressions ?? new Dictionary<string, ExpressionDefinition>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) throw new ArgumentException("Invalid expression registration.");
            foreach (var value in pair.Value.Values) Parameters.IndexOf(value.Parameter);
            e.Add(pair.Key, pair.Value);
        }
        Motions = new ReadOnlyDictionary<string, MotionDefinition>(m);
        Expressions = new ReadOnlyDictionary<string, ExpressionDefinition>(e);
    }
}
