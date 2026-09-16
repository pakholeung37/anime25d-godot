using Anime25D.Runtime;
using System.Numerics;

internal static class ModelChecks
{
    public static int Run()
    {
        int count = 0;
        void Require(bool pass, string label) { count++; if (!pass) throw new Exception(label); }
        void Reject(Action action) { count++; try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid model accepted."); }
        var animation = new AnimationModel(new[] { new ParameterDefinition("shift", 0, -100, 100) },
            new Dictionary<string, MotionDefinition> { ["move"] = new(1, new[] { new MotionTrack("shift", MotionCurve.Linear((0, 0), (1, 10))) }, fadeIn: 0, fadeOut: 0) });
        var raw = new float[] { 0, 0, 10, 0, 0, 10 };
        var mesh = new MeshDefinition(raw, new float[6], new[] { 0, 1, 2 }); raw[0] = 99;
        Require(mesh.RestPositions[0] == 0, "Geometry aliases authoring arrays.");
        var def = new ModelDefinition(animation, 64, 64, new[] { new LayerDefinition("panel", mesh) },
            plan: new ModelPlan([new("bindings", ModelStage.Layers, d => new LayerBindingComponent(d, new[] { new LayerParameterBinding("shift", "panel", LayerProperty.TranslationX) }))]));
        using var first = new ModelInstance(def); using var second = new ModelInstance(def);
        first.Refresh(); second.Refresh();
        Require(first.Frame.Layers[0].Visible && first.Frame.Layers[0].Positions[0] == 0, "Default geometry missing.");
        first.Animation.PlayMotion("move"); first.Advance(0.5);
        Require(Math.Abs(first.EvaluateCpuSnapshot().Positions[0][0] - 5) < 1e-6, "Common transform not present in diagnostics.");
        Require(first.Frame.Layers[0].Positions[0] == 0, "Transform applied twice before upload.");
        Require(second.Frame.Layers[0].Transform == Matrix3x2.Identity, "Instance transforms leaked.");
        var snapshot = first.EvaluateCpuSnapshot(); double time = first.Animation.Time;
        first.Refresh(); Require(first.Animation.Time == time && first.EvaluateCpuSnapshot().Positions[0].SequenceEqual(snapshot.Positions[0]), "Refresh advanced animation or accumulated transforms.");
        first.Layers[0].Opacity = 0.25; first.Refresh(); first.Refresh();
        Require(first.Frame.Layers[0].Opacity == 0.25, "Opacity accumulates across frames.");
        first.Layers[0].Visible = false; first.Refresh(); Require(!first.Frame.Layers[0].Visible, "Visibility override ignored.");
        first.Layers[0].DrawOrder = int.MaxValue; first.Refresh(); Require(first.Frame.Layers[0].DrawOrder == int.MaxValue, "Order was silently clamped.");
        var tracker = new TrackingBehavior();
        using var tracked = new ModelInstance(new(animation, 64, 64, def.Layers, plan: new ModelPlan([new("tracker", ModelStage.Derived, _ => tracker)])));
        tracked.Advance(0.2); tracked.Refresh(); tracked.EvaluateCpuSnapshot();
        Require(tracker.Advances == 1, "Refresh or CPU inspection advanced simulation.");
        var before = tracked.Frame; var oldVertices = before.Layers[0].Positions.ToArray(); tracker.Fail = true;
        bool fault = false; try { tracked.Advance(0.1); } catch (InvalidOperationException) { fault = true; }
        Require(fault && tracked.Faulted && ReferenceEquals(before, tracked.Frame) && tracked.Frame.Layers[0].Positions.SequenceEqual(oldVertices), "Failed evaluation published a partial frame.");
        tracked.Dispose(); tracked.Dispose(); Require(tracker.Disposals == 1, "Behavior disposed twice.");
        using var gpuLike = new ModelInstance(def) { EvaluateCpuGeometry = false }; gpuLike.Refresh();
        Require(!gpuLike.Frame.HasCpuGeometry && gpuLike.Frame.Layers[0].Positions.Length == 0, "GPU path evaluated vertices implicitly.");
        Require(gpuLike.EvaluateCpuSnapshot().Positions[0].Length == 6 && !gpuLike.Frame.HasCpuGeometry, "Explicit diagnostics changed backend state.");
        Reject(() => new ModelDefinition(animation, 64, 64, new[] { new LayerDefinition("p", mesh), new LayerDefinition("p", mesh) }));
        Reject(() => new ModelDefinition(animation, 64, 64, new[] { new LayerDefinition("p", mesh, MaskId: "unknown") }));
        Reject(() => new ModelDefinition(animation, 64, 64, new[] { new LayerDefinition("p", mesh, MaskId: "self") }, new[] { new MaskDefinition("self", new[] { "p" }) }));
        Reject(() => new ModelDefinition(animation, 64, 64, def.Layers, new[] { new MaskDefinition("bad", new[] { "missing" }) }));
        Reject(() => GridMeshBuilder.Create(0, 0, 10, 10, 0, 1));
        var grid = GridMeshBuilder.Create(2, 3, 10, 20, 2, 2);
        Require(grid.VertexCount == 9 && grid.Triangles.Length == 24 && grid.RestPositions[16] == 12 && grid.RestPositions[17] == 23, "Grid topology changed.");
        int notifications = 0; first.Animation.PlaybackChanged += _ => notifications++;
        first.Animation.PlayMotion("move"); first.Refresh(); Require(notifications == 0, "Refresh dispatched playback events.");
        first.Advance(0); Require(notifications > 0, "Advance lost queued notifications.");
        using var controlled = new ModelInstance(def);
        var controller = new CounterController(); controlled.Animation.BeforeControllers.Add(controller);
        controlled.Advance(0.5); controlled.Refresh(); controlled.Refresh();
        Require(controller.Advances == 1 && controlled.Frame.Pose["shift"] == 3, "Refresh advanced a stateful input controller.");
        var rejectedFrame = controlled.Frame;
        Reject(() => controlled.Advance(double.NaN));
        Require(!controlled.Faulted && ReferenceEquals(rejectedFrame, controlled.Frame), "Invalid caller delta faulted an otherwise valid model.");
        Console.WriteLine($"Model checks passed: {count} assertions."); return count;
    }
    private sealed class CounterController : IPoseController
    {
        public int Advances;
        public void AdvanceState(double deltaSeconds) => Advances++;
        public void Apply(ParameterSet pose) => pose["shift"] = 3;
    }
    private sealed class TrackingBehavior : ModelComponent
    {
        public int Advances, Disposals;
        public bool Fail;
        public override void AdvanceState(ComponentContext c, double delta) => Advances++;
        public override void EvaluateOutput(ComponentContext c) { if (Fail) throw new InvalidOperationException("Injected failure"); }
        public override void Dispose() => Disposals++;
    }
}
