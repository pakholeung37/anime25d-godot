using System;
using Anime25D.Runtime;
using System.Collections.Generic;
using System.Linq;

namespace Anime25D.Sample.Core;

/// <summary>Coordinates pose composition, smoothing, physics and optional CPU geometry evaluation.</summary>
public sealed class SampleBehavior : IDisposable
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
    public int BlinkVariant { get; private set; } = 1;
    /// <summary>Optional frame-local pose modifiers, before built-in motion and smoothing.
    /// Modify the provided parameters synchronously; do not retain the buffer or re-enter Step.
    /// Target parameters and the active expression are not changed by these overrides.</summary>
    public event Action<Parameters>? PreparingPose;
    public static IReadOnlyDictionary<string, ExpressionPose> Presets => ExpressionPose.Defaults;

    private readonly Parameters targetFrame;
    private readonly MotionPipeline motions;
    private readonly FrameParameters deformationFrame, controllerFrame;

    public SampleBehavior(RigDefinition definition, Func<double>? randomSource = null, RigProfile? profile = null, RigMeshGeometry[]? geometries = null)
    {
        Definition = definition;
        Profile = (profile ?? new RigProfile()).CreateSnapshot();
        Physics = new(Profile.Physics, Profile.Deformation, Definition.Anchors);
        var catalog = Profile.CreateCatalog();
        Target = new(catalog);
        Current = new(catalog);
        Frame = new(catalog);
        deformationFrame = new(catalog);
        controllerFrame = new(catalog);
        targetFrame = new(catalog);
        Parts = definition.Layers.Select((part, i) => new PartState(part, definition, Profile, geometries?[i])).ToArray();
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
        SetParameter(Parameter.LeftEyeOpenness, expression.LeftEyeOpenness, immediate);
        SetParameter(Parameter.RightEyeOpenness, expression.RightEyeOpenness, immediate);
        SetParameter(Parameter.EyebrowHeight, expression.EyebrowHeight, immediate);
        SetParameter(Parameter.MouthOpenness, expression.MouthOpenness, immediate);
        SetParameter(Parameter.MouthShape, expression.MouthShape, immediate);
        SetParameter(Parameter.IrisScale, expression.IrisScale, immediate);
        ActivePreset = name;
    }

    public void PreparePose(ParameterSet pose, double deltaSeconds, bool advance)
    {
        if (advance)
        {
            TimeMilliseconds += deltaSeconds * 1000;
            targetFrame.CopyFrom(Target);
            PreparingPose?.Invoke(targetFrame);
            motions.Compose(targetFrame, AutomaticMotion, ActivePreset is not null, TimeMilliseconds, deltaSeconds);
            double smoothing = 1 - Math.Exp(-deltaSeconds * Profile.Motion.SmoothingRate);
            for (int index = 0; index < Current.Catalog.Count; index++)
            {
                var spec = Current.Catalog[index];
                Current[spec.Key] += (Math.Clamp(targetFrame[spec.Key], spec.Min, spec.Max) - Current[spec.Key]) * smoothing;
            }
            Frame.Values.CopyFrom(Current);
            motions.Decorate(controllerFrame, TimeMilliseconds / 1000);
        }
        foreach (var spec in Current.Catalog) pose[spec.Key.ToString()] = Current[spec.Key];
        pose[SampleAnimations.Breath] = controllerFrame.Breath;
        pose[SampleAnimations.BreathHead] = controllerFrame.BreathHead;
        pose[SampleAnimations.IrisX] = controllerFrame.IrisBounceX;
        pose[SampleAnimations.IrisY] = controllerFrame.IrisBounceY;
        pose[SampleAnimations.EyeVariant] = motions.BlinkVariant;
    }

    // Both render backends consume this output, after motion/expression/external input mixing.
    public void ResolvePose(ParameterSet pose, double deltaSeconds, bool advance)
    {
        foreach (var spec in Current.Catalog) Frame[spec.Key] = pose[spec.Key.ToString()];
        Frame.Breath = pose[SampleAnimations.Breath];
        Frame.BreathHead = pose[SampleAnimations.BreathHead];
        Frame.IrisBounceX = pose[SampleAnimations.IrisX];
        Frame.IrisBounceY = pose[SampleAnimations.IrisY];
        BlinkVariant = (int)Math.Round(pose[SampleAnimations.EyeVariant]);
        Physics.Step(Frame, Parts, AutomaticMotion.Idle, TimeMilliseconds / 1000, advance ? deltaSeconds : 0);
    }

    public void EvaluateLayers(ReadOnlyPose pose, LayerFrame[] layers)
    {
        LayerVisibility.Update(Parts, Frame, BlinkVariant, Profile.Deformation);
        for (int i = 0; i < Parts.Length; i++)
        {
            layers[i].Visible = Parts[i].Visible;
            layers[i].Opacity = Parts[i].Alpha;
            layers[i].DrawOrder = Parts[i].DrawOrder;
        }
    }
    public void Dispose() { }
    public void DeformCpu(int layer, ReadOnlyPose pose, ReadOnlySpan<float> rest, Span<float> output)
    {
        foreach (var spec in Current.Catalog) deformationFrame[spec.Key] = pose[spec.Key.ToString()];
        deformationFrame.Breath = pose[SampleAnimations.Breath];
        deformationFrame.BreathHead = pose[SampleAnimations.BreathHead];
        deformationFrame.IrisBounceX = pose[SampleAnimations.IrisX];
        deformationFrame.IrisBounceY = pose[SampleAnimations.IrisY];
        var input = new LegacyDeformationInput(Definition.Anchors, deformationFrame, Profile.Deformation, Physics.Chest.Displacement, AutomaticMotion.Physics);
        LegacyRigDeformer.Deform(Parts[layer], input, output);
    }
}
