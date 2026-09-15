using System;
using System.Collections.Generic;
using System.Linq;

namespace Anime25D.Core;

/// <summary>Coordinates pose composition, smoothing, physics and optional CPU geometry evaluation.</summary>
public sealed class RigSimulation
{
    public RigDefinition Definition { get; }
    public RigProfile Profile { get; }
    public PartState[] Parts { get; }
    public Parameters Target { get; }
    public Parameters Current { get; }
    public FrameParameters Frame { get; }
    public AutomaticMotion AutomaticMotion { get; } = new();
    public PhysicsSolver Physics { get; }
    public Spring Bounce => Physics.Chest;
    public double TimeMilliseconds { get; private set; }
    public string? ActivePreset { get; private set; }
    public int BlinkVariant => motions.BlinkVariant;
    public PointerInput Pointer { get; } = new();
    public bool EvaluateCpuGeometry { get; set; } = true;
    public static IReadOnlyDictionary<string, ExpressionPose> Presets => ExpressionPose.Defaults;

    private readonly Parameters targetFrame;
    private readonly MotionPipeline motions;

    public RigSimulation(RigDefinition definition, Func<double>? randomSource = null, RigProfile? profile = null)
    {
        Definition = definition;
        Profile = (profile ?? new RigProfile()).CreateSnapshot();
        Physics = new(Profile.Physics, Profile.Deformation, Definition.Anchors);
        var catalog = Profile.CreateCatalog();
        Target = new(catalog);
        Current = new(catalog);
        Frame = new(catalog);
        targetFrame = new(catalog);
        Parts = definition.Layers.Select(part => new PartState(part, definition, Profile)).ToArray();
        motions = new(Profile.Motion, Parts.Any(part => part.Definition.Role == PartRole.AlternateClosedEye),
            randomSource ?? new Random().NextDouble);
        Frame.Values.CopyFrom(Current);
    }

    public void SetParameter(Parameter key, double value, bool immediate = false)
    {
        Target.Set(key, value);
        ActivePreset = null;
        if (immediate)
            Current[key] = Frame[key] = Target[key];
    }

    public void SetBlinkEnabled(bool enabled)
    {
        AutomaticMotion.Blink = enabled;
        if (!enabled)
            motions.ResetBlink();
    }

    public void ResetParameters()
    {
        ActivePreset = null;
        Target.Reset();
        Current.CopyFrom(Target);
        Frame.Values.CopyFrom(Current);
    }

    public void SetPreset(string? name, bool immediate = false)
    {
        if (name is null)
        {
            ActivePreset = null;
            return;
        }
        if (!Profile.Expressions.TryGetValue(name, out var expression))
            throw new ArgumentException($"Unknown expression: {name}");
        expression.Apply(this, immediate);
        ActivePreset = name;
    }

    public void Step(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0)
            throw new ArgumentOutOfRangeException(nameof(delta));
        double deltaSeconds = Math.Min(Profile.Motion.MaximumDeltaSeconds, delta);
        TimeMilliseconds += deltaSeconds * 1000;
        targetFrame.CopyFrom(Target);
        motions.Compose(targetFrame, AutomaticMotion, Pointer, ActivePreset is not null, TimeMilliseconds, deltaSeconds);
        double smoothing = 1 - Math.Exp(-deltaSeconds * Profile.Motion.SmoothingRate);
        for (int index = 0; index < Current.Catalog.Count; index++)
        {
            var spec = Current.Catalog[index];
            Current[spec.Key] += (Math.Clamp(targetFrame[spec.Key], spec.Min, spec.Max) - Current[spec.Key]) * smoothing;
        }
        Frame.Values.CopyFrom(Current);
        motions.Decorate(Frame, TimeMilliseconds / 1000);
        Physics.Step(Frame, Parts, AutomaticMotion.Idle, TimeMilliseconds / 1000, deltaSeconds);
        UpdateGeometry();
    }

    public void UpdateGeometry()
    {
        LayerVisibility.Update(Parts, Frame, BlinkVariant, Profile.Deformation);
        if (!EvaluateCpuGeometry)
            return;
        var input = new DeformationInput(Definition.Anchors, Frame, Profile.Deformation, Physics.Chest.Displacement, AutomaticMotion.Physics);
        foreach (var part in Parts)
        {
            if (part.Alpha >= LayerVisibility.RenderThreshold)
                RigDeformer.Deform(part, input);
        }
    }
}
