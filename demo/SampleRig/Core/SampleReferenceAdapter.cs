using System;
using System.Collections.Generic;
using Anime25D.Core;

namespace Anime25D.Sample.Core;

/// <summary>Reference/test facade only. All model evaluation is performed by ModelInstance.</summary>
public sealed class SampleReferenceAdapter
{
    public ModelInstance Instance { get; }
    public SampleBehavior Behavior => (SampleBehavior)Instance.Behavior;
    public AnimationRuntime Runtime => Instance.Animation;
    public RigDefinition Definition => Behavior.Definition;
    public RigProfile Profile => Behavior.Profile;
    public PartState[] Parts => Behavior.Parts;
    public Parameters Target => Behavior.Target;
    public Parameters Current => Behavior.Current;
    public FrameParameters Frame => Behavior.Frame;
    public AutomaticMotion AutomaticMotion => Behavior.AutomaticMotion;
    public PhysicsSolver Physics => Behavior.Physics;
    public Spring Bounce => Behavior.Bounce;
    public double TimeMilliseconds => Behavior.TimeMilliseconds;
    public string? ActivePreset => Behavior.ActivePreset;
    public int BlinkVariant => Behavior.BlinkVariant;
    public bool EvaluateCpuGeometry { get => Instance.EvaluateCpuGeometry; set => Instance.EvaluateCpuGeometry = value; }
    public static IReadOnlyDictionary<string, ExpressionPose> Presets => ExpressionPose.Defaults;
    public event Action<Parameters>? PreparingPose { add => Behavior.PreparingPose += value; remove => Behavior.PreparingPose -= value; }
    public SampleReferenceAdapter(RigDefinition definition, Func<double>? randomSource = null, RigProfile? profile = null, AnimationModel? animations = null)
        : this(new ModelInstance(SampleModelBuilder.Build(definition, profile, animations, randomSource))) { }
    public SampleReferenceAdapter(ModelInstance instance) => Instance = instance;
    public void SetParameter(Parameter key, double value, bool immediate = false) => Behavior.SetParameter(key, value, immediate);
    public void SetPreset(string? name, bool immediate = false) => Behavior.SetPreset(name, immediate);
    public void SetBlinkEnabled(bool enabled) => Behavior.SetBlinkEnabled(enabled);
    public void ResetParameters() => Behavior.ResetParameters();
    public void Step(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        Instance.Advance(Math.Min(Profile.Motion.MaximumDeltaSeconds, delta));
        SyncReferenceVertices();
    }
    public void UpdateGeometry() { Instance.Refresh(); SyncReferenceVertices(); }
    public void SyncReferenceVertices()
    {
        if (!Instance.Frame.HasCpuGeometry) return;
        for (int i = 0; i < Parts.Length; i++) Instance.Frame.Layers[i].Positions.CopyTo(Parts[i].Positions);
    }
}
