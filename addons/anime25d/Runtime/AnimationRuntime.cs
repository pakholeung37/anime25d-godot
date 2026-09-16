using System;
using System.Collections.Generic;
using System.Linq;

namespace Anime25D.Runtime;

public enum PlaybackState { Playing, FadingOut, Completed, Interrupted, Stopped }
public enum PlaybackEventKind { Started, Looped, Completed, Interrupted, Stopped }
public sealed record PlaybackEvent(PlaybackHandle Playback, PlaybackEventKind Kind, long LoopCount = 0);
public sealed record PlayOptions(bool? Loop = null, double Speed = 1, double? FadeIn = null, double? FadeOut = null);

/// <summary>Mutable state belongs to one play request, never to a shared definition.</summary>
public sealed class PlaybackHandle
{
    public string Name { get; }
    public bool IsExpression { get; }
    public bool Loop { get; }
    public double Speed { get; }
    public bool Paused { get; set; }
    public double Time { get; internal set; }
    public PlaybackState State { get; internal set; } = PlaybackState.Playing;
    public bool IsFinished => State is PlaybackState.Completed or PlaybackState.Interrupted or PlaybackState.Stopped;
    internal readonly (int Index, MotionCurve Curve, BlendMode Blend)[] Tracks;
    internal readonly double Duration, FadeIn, FadeOut;
    internal double Elapsed, Age, ExitAge, ExitDuration, ExitWeight;
    internal PlaybackState ExitReason;
    internal PlaybackHandle(string name, bool expression, (int, MotionCurve, BlendMode)[] tracks,
        double duration, bool loop, double speed, double fadeIn, double fadeOut)
    {
        Name = name; IsExpression = expression; Tracks = tracks; Duration = duration;
        Loop = loop; Speed = speed; FadeIn = fadeIn; FadeOut = fadeOut;
    }
    internal double Weight
    {
        get
        {
            if (IsFinished) return 0;
            if (State == PlaybackState.FadingOut)
                return ExitDuration == 0 ? 0 : ExitWeight * Math.Max(0, 1 - ExitAge / ExitDuration);
            double fade = FadeIn == 0 ? 1 : Math.Min(1, Age / FadeIn);
            if (!IsExpression && !Loop && FadeOut > 0)
                fade *= Math.Clamp((Duration - Elapsed) / Speed / FadeOut, 0, 1);
            return fade;
        }
    }
}

/// <summary>A synchronous frame-local modifier. Do not retain pose buffers or recursively advance.</summary>
public interface IPoseModifier { void Apply(ParameterSet pose, double deltaSeconds); }
/// <summary>State advancement is separate from repeatable sampling, including paused refresh.</summary>
public interface IPoseController
{
    void AdvanceState(double deltaSeconds);
    void Apply(ParameterSet pose);
}
/// <summary>External physics/deformation/rendering boundary; its implementation owns all output state.</summary>
public interface IAnimationOutput { void Evaluate(ParameterSet pose, double deltaSeconds); }

/// <summary>One motion layer and one expression layer, followed by external input and output.
/// No automatic motion, anatomical channels, smoothing, or delta truncation is built in.</summary>
public sealed class AnimationRuntime
{
    public AnimationModel Model { get; }
    public ParameterSet BasePose { get; }
    public ParameterSet Pose { get; }
    public bool Paused { get; set; }
    public double Time { get; private set; }
    public PlaybackHandle? Motion { get; private set; }
    public PlaybackHandle? Expression { get; private set; }
    public IList<IPoseModifier> BeforeAnimation { get; } = new List<IPoseModifier>();
    public IList<IPoseModifier> AfterAnimation { get; } = new List<IPoseModifier>();
    public IList<IPoseController> BeforeControllers { get; } = new List<IPoseController>();
    public IList<IPoseController> AfterControllers { get; } = new List<IPoseController>();
    public IAnimationOutput? Output { get; set; }
    /// <summary>Delivered after evaluation. LoopCount aggregates crossings in this update.
    /// Play/stop calls from callbacks take effect on the next update.</summary>
    public event Action<PlaybackEvent>? PlaybackChanged;
    private readonly List<PlaybackHandle> motions = new(), expressions = new();
    private readonly List<PlaybackEvent> events = new();
    private readonly double[] contributions, weights;
    private bool evaluating, dispatching;
    public AnimationRuntime(AnimationModel model)
    {
        Model = model; BasePose = new(model.Parameters); Pose = new(model.Parameters);
        contributions = new double[model.Parameters.Definitions.Count];
        weights = new double[contributions.Length];
    }
    public PlaybackHandle PlayMotion(string name, PlayOptions? options = null)
    {
        CheckMutation();
        if (!Model.Motions.TryGetValue(name, out var clip)) throw new ArgumentException($"Unknown motion: {name}");
        options ??= new(); Validate(options);
        var handle = new PlaybackHandle(name, false,
            clip.Tracks.Select(t => (Model.Parameters.IndexOf(t.Parameter), t.Curve, t.Blend)).ToArray(),
            clip.Duration, options.Loop ?? clip.Loop, options.Speed, options.FadeIn ?? clip.FadeIn, options.FadeOut ?? clip.FadeOut);
        Start(motions, handle);
        return Motion = handle;
    }
    public PlaybackHandle SetExpression(string name, double? fadeIn = null, double? fadeOut = null)
    {
        CheckMutation();
        if (!Model.Expressions.TryGetValue(name, out var clip)) throw new ArgumentException($"Unknown expression: {name}");
        MotionDefinition.ValidateFade(fadeIn ?? clip.FadeIn); MotionDefinition.ValidateFade(fadeOut ?? clip.FadeOut);
        var handle = new PlaybackHandle(name, true,
            clip.Values.Select(t => (Model.Parameters.IndexOf(t.Parameter), MotionCurve.Constant(t.Value), t.Blend)).ToArray(),
            double.PositiveInfinity, false, 1, fadeIn ?? clip.FadeIn, fadeOut ?? clip.FadeOut);
        Start(expressions, handle);
        return Expression = handle;
    }
    public void StopMotion(double? fadeOut = null) => StopLayer(motions, fadeOut);
    public void ClearExpression(double? fadeOut = null) => StopLayer(expressions, fadeOut);
    public void Stop(PlaybackHandle handle, double? fadeOut = null)
    {
        CheckMutation();
        if (fadeOut is { } fade) MotionDefinition.ValidateFade(fade);
        var layer = handle.IsExpression ? expressions : motions;
        if (!layer.Contains(handle))
        {
            if (handle.IsFinished) return;
            throw new ArgumentException("Playback does not belong to this runtime.");
        }
        Exit(handle, fadeOut ?? handle.FadeOut, PlaybackState.Stopped);
    }
    private static void Validate(PlayOptions options)
    {
        if (!double.IsFinite(options.Speed) || options.Speed <= 0) throw new ArgumentException("Playback speed must be positive and finite.");
        if (options.FadeIn is { } fi) MotionDefinition.ValidateFade(fi);
        if (options.FadeOut is { } fo) MotionDefinition.ValidateFade(fo);
    }
    private void CheckMutation()
    {
        if (evaluating) throw new InvalidOperationException("Change playback outside pose evaluation or in PlaybackChanged callbacks.");
    }
    private void Start(List<PlaybackHandle> layer, PlaybackHandle next)
    {
        foreach (var old in layer)
            if (old.State == PlaybackState.Playing) Exit(old, old.FadeOut, PlaybackState.Interrupted);
        layer.Add(next);
        events.Add(new(next, PlaybackEventKind.Started));
    }
    private void StopLayer(List<PlaybackHandle> layer, double? fadeOut)
    {
        CheckMutation();
        if (fadeOut is { } fade) MotionDefinition.ValidateFade(fade);
        foreach (var handle in layer) Exit(handle, fadeOut ?? handle.FadeOut, PlaybackState.Stopped);
    }
    private static void Exit(PlaybackHandle handle, double duration, PlaybackState reason)
    {
        if (handle.IsFinished) return;
        handle.ExitWeight = handle.Weight;
        handle.ExitAge = 0; handle.ExitDuration = duration; handle.ExitReason = reason;
        handle.State = PlaybackState.FadingOut;
    }
    public void Advance(double deltaSeconds)
    {
        EvaluateFrame(deltaSeconds, true);
        if (Paused) return;
        evaluating = true;
        try { Output?.Evaluate(Pose, deltaSeconds); }
        finally { evaluating = false; }
        DispatchEvents();
    }
    public void Refresh() => EvaluateFrame(0, false);
    internal void EvaluateFrame(double deltaSeconds, bool advance, Action<ParameterSet>? prepare = null)
    {
        CheckMutation();
        if (dispatching) throw new InvalidOperationException("Do not recursively advance from playback callbacks.");
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0 || !double.IsFinite(Time + deltaSeconds))
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (advance && Paused) return;
        foreach (var h in motions.Concat(expressions))
            if (!double.IsFinite(h.Elapsed + deltaSeconds * h.Speed) ||
                (!h.IsExpression && h.Loop && (h.Elapsed + deltaSeconds * h.Speed) / h.Duration >= long.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        evaluating = true;
        try
        {
            if (advance) Time += deltaSeconds;
            Pose.CopyFrom(BasePose);
            prepare?.Invoke(Pose);
            foreach (var controller in BeforeControllers.ToArray())
            {
                if (advance) controller.AdvanceState(deltaSeconds);
                controller.Apply(Pose);
            }
            foreach (var modifier in BeforeAnimation.ToArray()) modifier.Apply(Pose, advance ? deltaSeconds : 0);
            if (advance) Update(motions, deltaSeconds);
            Mix(motions);
            if (advance) Update(expressions, deltaSeconds);
            Mix(expressions);
            foreach (var controller in AfterControllers.ToArray())
            {
                if (advance) controller.AdvanceState(deltaSeconds);
                controller.Apply(Pose);
            }
            foreach (var modifier in AfterAnimation.ToArray()) modifier.Apply(Pose, advance ? deltaSeconds : 0);
        }
        finally { evaluating = false; }
    }
    public void DiscardEvents() => events.Clear();
    public void DispatchEvents()
    {
        CheckMutation();
        if (dispatching) throw new InvalidOperationException("Recursive event dispatch.");
        var notifications = events.ToArray(); events.Clear();
        dispatching = true;
        try { foreach (var notification in notifications) PlaybackChanged?.Invoke(notification); }
        finally { dispatching = false; }
    }
    private void Update(List<PlaybackHandle> layer, double delta)
    {
        foreach (var h in layer)
        {
            if (h.IsFinished) continue;
            if (!h.Paused)
            {
                double previous = h.Elapsed;
                double activeDelta = h.State == PlaybackState.FadingOut ? Math.Min(delta, Math.Max(0, h.ExitDuration - h.ExitAge)) : delta;
                h.Elapsed += activeDelta * h.Speed; h.Age += activeDelta;
                h.Time = h.IsExpression ? h.Elapsed : h.Loop ? h.Elapsed % h.Duration : Math.Min(h.Elapsed, h.Duration);
                if (h.Loop)
                {
                    long crossed = (long)Math.Floor(h.Elapsed / h.Duration) - (long)Math.Floor(previous / h.Duration);
                    if (crossed > 0) events.Add(new(h, PlaybackEventKind.Looped, crossed));
                }
            }
            if (h.State == PlaybackState.FadingOut)
            {
                h.ExitAge += delta;
                if (h.ExitAge >= h.ExitDuration) Finish(h, h.ExitReason);
            }
            else if (!h.IsExpression && !h.Loop && h.Elapsed >= h.Duration) Finish(h, PlaybackState.Completed);
        }
        layer.RemoveAll(h => h.IsFinished);
    }
    private void Finish(PlaybackHandle handle, PlaybackState state)
    {
        handle.State = state;
        if (ReferenceEquals(Motion, handle)) Motion = null;
        if (ReferenceEquals(Expression, handle)) Expression = null;
        events.Add(new(handle, state switch
        {
            PlaybackState.Completed => PlaybackEventKind.Completed,
            PlaybackState.Interrupted => PlaybackEventKind.Interrupted,
            _ => PlaybackEventKind.Stopped
        }));
    }
    private void Mix(List<PlaybackHandle> layer)
    {
        Array.Clear(contributions); Array.Clear(weights);
        foreach (var h in layer)
        {
            double weight = h.Weight;
            if (weight == 0) continue;
            foreach (var track in h.Tracks)
            {
                double source = Pose[track.Index], value = track.Curve.Sample(h.Time);
                double target = track.Blend switch { BlendMode.Add => source + value, BlendMode.Multiply => source * value, _ => value };
                contributions[track.Index] += (target - source) * weight;
                weights[track.Index] += weight;
            }
        }
        for (int i = 0; i < contributions.Length; i++)
            if (weights[i] > 0) Pose[i] += contributions[i] / Math.Max(1, weights[i]);
    }
}
