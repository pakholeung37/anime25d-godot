using System;
using static Anime25D.Core.Parameter;
using static System.Math;

namespace Anime25D.Core;

/// <summary>Ordering preserves blending precedence and the reference random-number sequence.</summary>
public sealed class MotionPipeline
{
    private readonly RandomMotion randomMotion = new();
    private readonly TalkMotion talk = new();
    private readonly BlinkMotion blink;
    private readonly Func<double> random;
    private readonly MotionSettings settings;
    public int BlinkVariant => blink.Variant;

    public MotionPipeline(MotionSettings settings, bool alternateBlinkAvailable, Func<double> random)
    {
        this.settings = settings;
        this.random = random;
        blink = new(settings.Blink, alternateBlinkAvailable);
    }

    public void ResetBlink() => blink.Reset();

    public void Compose(Parameters target, AutomaticMotion enabled, PointerInput pointer, bool expressionActive, double now, double deltaSeconds)
    {
        if (enabled.Mouse && pointer.IsAvailable)
            GazeMotion.Apply(target, settings.Gaze, pointer.Horizontal, pointer.Vertical);
        if (enabled.Idle)
            IdleMotion.Apply(target, settings.Idle, now / 1000);
        if (enabled.Random)
            randomMotion.Apply(target, settings.Random, now, random);
        if (enabled.Talk && !expressionActive)
            talk.Apply(target, settings.Talk, now, deltaSeconds, random);
        if (enabled.Blink && !expressionActive)
            blink.Apply(target, now, deltaSeconds, random);
        blink.AdvanceBounce(deltaSeconds);
    }

    public void Decorate(FrameParameters frame, double timeSeconds)
    {
        blink.ApplyBounce(frame);
        frame.Breath = 0.5 + 0.5 * Sin(timeSeconds * 2 * PI / settings.Breath.PeriodSeconds);
        frame.BreathHead = 0.5 + 0.5 * Sin(timeSeconds * 2 * PI / settings.Breath.PeriodSeconds - settings.Breath.HeadPhaseLagRadians);
    }
}
