using Anime25D.Runtime;
using System.Numerics;
internal static class CompositionChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string label) { count++; if (!value) throw new Exception(label); }
        var animations = new AnimationModel([new("wave", 0, -10, 10)]);
        var mesh = GridMeshBuilder.Create(1, 1, 2, 2);
        var model = new ModelDefinition(animations, 32, 32, [new("panel", mesh)], plan: new ModelPlan(
            components: [
                new("wave", ModelStage.BasePose, d => new SineDriver(d, new SineBinding("wave", 1, 2))),
                new("target", ModelStage.Derived, _ => new ChannelWriter("target", c => [c.Pose["wave"]]), writes: ["target"]),
                new("spring", ModelStage.Derived, _ => new SpringBank("target", "lag", [new(70, 9)]), reads: ["target"], writes: ["lag"]),
                new("alpha", ModelStage.Layers, d => new LayerOpacityBinding(d, "panel", c => Math.Clamp(.5 + c.Channels.Scalar("lag"), 0, 1)), reads: ["lag"])
            ], channels: [new("target"), new("lag")], deformers: [
                new AffineDeformer("shift", ["panel"], Matrix3x2.CreateTranslation(3, 0)),
                new AffineDeformer("scale", ["panel"], Matrix3x2.CreateScale(2))
            ]));
        using var a = new ModelInstance(model); using var b = new ModelInstance(model);
        a.Advance(0.1); b.Refresh();
        Check(a.Frame.Pose["wave"] != b.Frame.Pose["wave"], "Instance clocks leaked.");
        Check(a.EvaluateCpuSnapshot().Positions[0][0] == 8, "Noncommuting deformer order wrong.");
        Check(a.Frame.Layers[0].Opacity > 0.5, "Layer binding ignored.");
        double lag = a.Frame.Channels.Scalar("lag"); var vertices = a.EvaluateCpuSnapshot().Positions[0];
        a.Refresh(); a.Refresh();
        Check(a.Frame.Channels.Scalar("lag") == lag && a.Animation.Time == 0.1, "Refresh integrated physics.");
        Check(a.EvaluateCpuSnapshot().Positions[0].SequenceEqual(vertices), "Snapshot changed state.");
        a.SetComponentEnabled("wave", false); a.Refresh(); Check(a.Frame.Pose["wave"] == 0, "Disabled driver still applied.");
        try { _ = new ModelPlan([new("bad", ModelStage.Derived, _ => new ChannelWriter("y", c => [0]), reads: ["x"], writes: ["y"])], [new("y")]); throw new Exception("Unresolved channel accepted."); } catch (ArgumentException) { count++; }
        try { _ = new SpringState(); var spring = new SpringState(); spring.Step(1, 1, 1, 1000); throw new Exception("Unbounded work accepted."); } catch (InvalidOperationException) { count++; }
        var released = new DisposableComponent();
        try
        {
            _ = new ModelInstance(new ModelDefinition(animations, 32, 32, model.Layers, plan: new ModelPlan([
                new("first", ModelStage.BasePose, _ => released),
                new("fail", ModelStage.BasePose, _ => throw new InvalidOperationException("Injected creation failure"))
            ])));
            throw new Exception("Construction failure was swallowed.");
        }
        catch (InvalidOperationException) { Check(released.Disposals == 1, "Partial component construction leaked resources."); }
        var offsetData = new float[mesh.VertexCount * 2]; offsetData[0] = 5;
        var offset = new VertexOffsetDeformer("offset", ["panel"], "wave", offsetData); offsetData[0] = 100;
        using var offsetInstance = new ModelInstance(new ModelDefinition(animations, 32, 32, model.Layers, plan: new ModelPlan(deformers: [offset])));
        offsetInstance.SetParameter("wave", 1); offsetInstance.Refresh();
        Check(offsetInstance.Frame.Layers[0].Positions[0] == 6, "Vertex offset aliases mutable authoring data.");
        var broken = new BadOutput();
        using var failing = new ModelInstance(new ModelDefinition(animations, 32, 32, model.Layers, plan: new ModelPlan([
            new("output", ModelStage.Derived, _ => broken, writes: ["result"])
        ], [new("result")])));
        failing.Refresh(); var published = failing.Frame; broken.Fail = true;
        try { failing.Advance(.1); throw new Exception("Nonfinite channel accepted."); } catch (InvalidOperationException) { Check(failing.Faulted && ReferenceEquals(failing.Frame, published), "Partial channel frame published."); }
        Console.WriteLine($"Composition checks passed: {count} assertions."); return count;
    }
    private sealed class DisposableComponent : ModelComponent
    {
        public int Disposals;
        public override void EvaluateOutput(ComponentContext c) { }
        public override void Dispose() => Disposals++;
    }
    private sealed class BadOutput : ModelComponent
    {
        public bool Fail;
        public override void EvaluateOutput(ComponentContext c) => c.Output("result")[0] = Fail ? double.NaN : 1;
    }
}
