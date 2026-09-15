using System;
using System.Collections.Generic;
using System.Linq;
using static Anime25D.Core.Parameter;
using static System.Math;

namespace Anime25D.Core;

public sealed class RigSimulation
{
    public RigDefinition Definition { get; }
    public PartState[] Parts { get; }
    public Parameters Target { get; } = new();
    public Parameters Current { get; } = new();
    public FrameParameters Frame { get; } = new();
    public AutomaticMotion Auto { get; } = new();
    public Spring Bounce { get; } = new();
    public double TimeMilliseconds { get; private set; }
    public string? ActivePreset { get; private set; }
    public int BlinkVariant { get; private set; } = 1;
    public bool MouseInside { get; set; }
    public double MouseX { get; set; }
    public double MouseY { get; set; }

    private readonly Parameters targetFrame = new();
    private readonly Func<double> random;
    private double blinkT = -1, irisBounceT = -1, nextBlink = 1800, nextRandom, nextTalkState, nextSyllable;
    private double randomX, randomY, randomZ, randomBody, randomEyeX, randomEyeY, talkValue, talkTarget;
    private bool blinkBounceStarted, talkOn;
    private readonly bool hasEyeClose2;

    // The injected random source is also used by reference tests; default instances are independent.
    public RigSimulation(RigDefinition definition, Func<double>? randomSource = null)
    {
        Definition = definition; Parts = definition.Layers.Select(p => new PartState(p, definition)).ToArray();
        random = randomSource ?? new Random().NextDouble;
        hasEyeClose2 = Parts.Any(p => p.BaseName == "eye_close2");
        Frame.Values.CopyFrom(Current);
    }
    public void SetParameter(Parameter key, double value, bool immediate = false)
    {
        Target.Set(key, value); ActivePreset = null;
        if (immediate) { Current[key] = Target[key]; Frame[key] = Target[key]; }
    }
    public void SetBlinkEnabled(bool enabled)
    {
        Auto.Blink = enabled;
        if (!enabled) { blinkT = -1; BlinkVariant = 1; irisBounceT = -1; }
    }
    public void ResetParameters()
    {
        ActivePreset = null; Target.Reset(); Current.CopyFrom(Target); Frame.Values.CopyFrom(Current);
    }
    public static readonly IReadOnlyDictionary<string, double[]> Presets = new Dictionary<string, double[]>
    {
        ["neutral"] = [1,1,0,0,0,1], ["smile"] = [0,0,0.45,0,0.9,1], ["usume"] = [0.5,0.5,0.35,1,0.8,1],
        ["surprise"] = [1,1,1,0.75,-0.1,0.7], ["jito"] = [0.4,0.4,-0.6,0,-0.4,1],
        ["winkL"] = [0,1,0.2,0.4,0.7,1], ["winkR"] = [1,0,0.2,0.4,0.7,1]
    };
    public void SetPreset(string? name, bool immediate = false)
    {
        if (name is null) { ActivePreset = null; return; }
        if (!Presets.TryGetValue(name, out var values)) throw new ArgumentException($"Unknown preset: {name}");
        Parameter[] keys = [eyeOpenL, eyeOpenR, brow, mouthOpen, mouthForm, irisScale];
        for (int i = 0; i < keys.Length; i++) SetParameter(keys[i], values[i], immediate);
        ActivePreset = name;
    }
    public void Step(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        double dt = Min(0.05, delta); TimeMilliseconds += dt * 1000;
        double now = TimeMilliseconds, t = now / 1000;
        var tgt = targetFrame; tgt.CopyFrom(Target);
        if (Auto.Mouse && MouseInside)
        {
            tgt[angleX] = Clamp(MouseX * 0.9, -1, 1); tgt[angleY] = Clamp(-MouseY * 0.7, -1, 1);
            tgt[eyeX] = Clamp(MouseX * 1.2, -1, 1); tgt[eyeY] = Clamp(-MouseY * 0.8, -1, 1);
        }
        if (Auto.Idle)
        {
            tgt[angleX] += 0.13 * Sin(t * 0.42) + 0.05 * Sin(t * 1.13);
            tgt[angleY] += 0.08 * Sin(t * 0.31 + 1.7); tgt[angleZ] += 0.07 * Sin(t * 0.23 + 0.5); tgt[body] += 0.10 * Sin(t * 0.19 + 2.1);
        }
        if (Auto.Random)
        {
            if (now > nextRandom)
            {
                nextRandom = now + 1400 + random() * 2600;
                randomX = (random() * 2 - 1) * 0.55; randomY = (random() * 2 - 1) * 0.40;
                randomZ = (random() * 2 - 1) * 0.35; randomBody = (random() * 2 - 1) * 0.30;
                randomEyeX = (random() * 2 - 1) * 0.60; randomEyeY = (random() * 2 - 1) * 0.35;
            }
            tgt[angleX] = Clamp(tgt[angleX] + randomX, -1, 1); tgt[angleY] = Clamp(tgt[angleY] + randomY, -1, 1);
            tgt[angleZ] = Clamp(tgt[angleZ] + randomZ, -1, 1); tgt[body] = Clamp(tgt[body] + randomBody, -1, 1);
            tgt[eyeX] = Clamp(tgt[eyeX] + randomEyeX, -1, 1); tgt[eyeY] = Clamp(tgt[eyeY] + randomEyeY, -1, 1);
        }
        if (Auto.Talk && ActivePreset is null)
        {
            if (now > nextTalkState) { talkOn = !talkOn; nextTalkState = now + (talkOn ? 1200 + random() * 2200 : 600 + random() * 1800); }
            if (talkOn && now > nextSyllable) { nextSyllable = now + 70 + random() * 110; talkTarget = random() < 0.25 ? 0.04 : 0.25 + random() * 0.75; }
            if (!talkOn) talkTarget = 0;
            talkValue += (talkTarget - talkValue) * Min(1, dt * 22); tgt[mouthOpen] = Max(tgt[mouthOpen], talkValue);
        }
        if (Auto.Blink && ActivePreset is null)
        {
            if (blinkT < 0 && now > nextBlink)
            {
                blinkT = 0; blinkBounceStarted = false; BlinkVariant = hasEyeClose2 && random() < 0.2 ? 2 : 1;
                nextBlink = now + 1600 + random() * 3800; if (random() < 0.18) nextBlink = now + 280;
            }
            if (blinkT >= 0)
            {
                blinkT += dt; double d = blinkT, hold = BlinkVariant == 2 ? 3.4 : 0.34, v;
                if (d < 0.08) v = 1 - d / 0.08;
                else if (d < 0.08 + hold) v = 0;
                else if (d < 0.24 + hold)
                {
                    v = (d - 0.08 - hold) / 0.16;
                    if (!blinkBounceStarted && v > 0.12) { blinkBounceStarted = true; irisBounceT = 0; }
                }
                else { v = 1; blinkT = -1; }
                tgt[eyeOpenL] = Min(tgt[eyeOpenL], v); tgt[eyeOpenR] = Min(tgt[eyeOpenR], v);
            }
        }
        if (irisBounceT >= 0) { irisBounceT += dt; if (irisBounceT > 0.52) irisBounceT = -1; }
        double smoothing = 1 - Exp(-dt * 14);
        foreach (var spec in Parameters.Specs) Current[spec.Key] += (Clamp(tgt[spec.Key], spec.Min, spec.Max) - Current[spec.Key]) * smoothing;
        Frame.Values.CopyFrom(Current); Frame.IrisBounceX = Frame.IrisBounceY = 1;
        if (irisBounceT >= 0)
        {
            double p = irisBounceT / 0.52, damp = Exp(-2.2 * p);
            double scale = 1 + 0.18 * Sin(p * PI * 4) * damp, squash = 0.10 * Sin(p * PI * 4 + PI / 2) * damp;
            Frame.IrisBounceX = scale * (1 + squash); Frame.IrisBounceY = scale * (1 - squash);
        }
        Frame.Breath = 0.5 + 0.5 * Sin(t * 2 * PI / 3.4); Frame.BreathHead = 0.5 + 0.5 * Sin(t * 2 * PI / 3.4 - 0.6);
        var a = Definition.Anchors; double fs = a.FaceScale;
        double headDx = (Frame[angleX] * 14 + Frame[angleZ] * 0.07 * (a.NeckPivot.Cy - a.Face.Cy)) * fs;
        foreach (var part in Parts) foreach (var sp in part.Springs)
        {
            double wind = Auto.Idle ? 1.8 * Sin(t * 0.8 + sp.Phase) + Sin(t * 1.9 + sp.Phase * 2.3) : 0;
            double target = headDx + wind * fs;
            sp.Stiff.Step(target, 70, 9, dt); sp.Stiff.Displacement = -(sp.Stiff.X - target) * 2.2;
            sp.Soft.Step(target, 16, 1.3, dt); sp.Soft.Displacement = -(sp.Soft.X - target) * 3;
        }
        double bustTarget = (Frame.Breath * 3 - Frame[angleY] * 6 + Frame[body] * 4) * fs;
        Bounce.Step(bustTarget, 140, 4.2, dt); Bounce.Displacement = -(Bounce.X - bustTarget) * 3;
        UpdateGeometry();
    }
    public void UpdateGeometry()
    {
        foreach (var part in Parts)
        {
            part.Alpha = part.Visible ? RigDeformer.Fade(part, this) * Clamp(part.Opacity, 0, 1) : 0;
            if (part.Alpha >= 0.004) RigDeformer.Deform(part, this);
        }
    }
}
