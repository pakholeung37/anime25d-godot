using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Anime25D.Runtime;
namespace Anime25D.Sample.Core;
// Test-only bridge: keeps the original algorithm available as a regression oracle.
public static class LegacyPlan
{
    public static ModelPlan Create(string[] layers, Func<ModelDefinition, SampleBehavior> factory)
    {
        var frames = new ConditionalWeakTable<ModelFrame, SampleBehavior>();
        return new ModelPlan([
            new("legacy", ModelStage.BasePose, d => new LegacyState(factory(d))),
            new("legacy-resolve", ModelStage.Derived, _ => new Resolve()),
            new("legacy-layers", ModelStage.Layers, _ => new Layers(frames))
        ], deformers: [new Geometry(layers, frames)]);
    }
    public sealed class LegacyState(SampleBehavior behavior) : ModelComponent
    {
        public SampleBehavior Behavior => behavior;
        private bool advanced;
        public override void AdvanceState(ComponentContext c, double delta) { behavior.PreparePose(c.Pose, delta, true); advanced = true; }
        public override void EvaluateOutput(ComponentContext c) { if (!advanced) behavior.PreparePose(c.Pose, 0, false); advanced = false; }
        public override void Dispose() => behavior.Dispose();
    }
    private sealed class Resolve : ModelComponent
    {
        private bool advanced;
        public override void AdvanceState(ComponentContext c, double delta) { c.Component<LegacyState>("legacy").Behavior.ResolvePose(c.Pose, delta, true); advanced = true; }
        public override void EvaluateOutput(ComponentContext c) { if (!advanced) c.Component<LegacyState>("legacy").Behavior.ResolvePose(c.Pose, 0, false); advanced = false; }
    }
    private sealed class Layers(ConditionalWeakTable<ModelFrame, SampleBehavior> frames) : ModelComponent
    {
        public override void EvaluateOutput(ComponentContext c)
        { var b = c.Component<LegacyState>("legacy").Behavior; frames.Remove(c.Frame); frames.Add(c.Frame, b); b.EvaluateLayers(c.Pose.ReadOnly, c.Layers); }
    }
    private sealed class Geometry(string[] layers, ConditionalWeakTable<ModelFrame, SampleBehavior> frames) : DeformerDefinition("legacy", layers)
    {
        public override void Deform(int layer, ModelFrame frame, ReadOnlySpan<float> rest, ReadOnlySpan<float> input, Span<float> output) => frames.GetValue(frame, _ => throw new InvalidOperationException()).DeformCpu(layer, frame.Pose, rest, output);
    }
}
