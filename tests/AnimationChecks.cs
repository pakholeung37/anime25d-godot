using Anime25D.Core;

internal static class AnimationChecks
{
    public static int Run()
    {
        int count = 0;
        void Require(bool value, string message) { count++; if (!value) throw new Exception(message); }
        void Near(double a, double b, string message) => Require(Math.Abs(a - b) < 1e-10, $"{message}: {a} != {b}");
        void Reject(Action action)
        {
            count++;
            try { action(); } catch (ArgumentException) { return; }
            throw new Exception("Invalid animation input was accepted.");
        }
        var motion = new MotionDefinition(2, new[] { new MotionTrack("custom", MotionCurve.Linear((0, 0), (2, 1))) }, fadeIn: 0, fadeOut: 0);
        var m = new Dictionary<string, MotionDefinition> {
            ["move"] = motion,
            ["hold"] = new(10, new[] { new MotionTrack("custom", MotionCurve.Constant(1)) }, fadeIn: 0, fadeOut: 1),
            ["opposite"] = new(10, new[] { new MotionTrack("custom", MotionCurve.Constant(-1)) }, fadeIn: 1, fadeOut: 1),
            ["unrelated"] = new(10, new[] { new MotionTrack("other", MotionCurve.Constant(0.7)) }, fadeIn: 1, fadeOut: 1)
        };
        var model = new AnimationModel(new[] { new ParameterDefinition("custom"), new ParameterDefinition("other") }, m,
            new Dictionary<string, ExpressionDefinition> {
                ["offset"] = new(new[] { new ExpressionValue("custom", 0.2, BlendMode.Add) }, fadeIn: 0, fadeOut: 1),
                ["scale"] = new(new[] { new ExpressionValue("custom", 0.5, BlendMode.Multiply) }, fadeIn: 1, fadeOut: 1)
            });
        m.Clear(); Require(model.Motions.Count == 4, "Model definitions were not snapshotted.");
        var runtime = new AnimationRuntime(model);
        runtime.Advance(10);
        Near(runtime.Pose["custom"], 0, "Empty runtime must remain neutral");
        var notices = new List<PlaybackEvent>(); runtime.PlaybackChanged += notices.Add;
        var first = runtime.PlayMotion("move", new(Loop: true));
        runtime.Advance(0.5); Near(runtime.Pose["custom"], 0.25, "Linear sampling");
        runtime.SetExpression("offset"); runtime.Advance(0.5);
        Near(runtime.Pose["custom"], 0.7, "Motion and expression coexist");
        runtime.Advance(0); Near(runtime.Pose["custom"], 0.7, "Additive expression must not accumulate");
        runtime.Advance(5); Near(first.Time, 0, "Exact loop boundary");
        Require(notices.Where(e => e.Kind == PlaybackEventKind.Looped).Sum(e => e.LoopCount) == 3, "Skipped loops lost");
        first.Paused = true; runtime.Advance(0.5); Near(first.Time, 0, "Handle pause");
        first.Paused = false;
        runtime.Paused = true; double clock = runtime.Time; runtime.Advance(1); Near(runtime.Time, clock, "Runtime pause"); runtime.Paused = false;
        runtime.ClearExpression(0); runtime.StopMotion(0); runtime.Advance(0);
        Require(first.State == PlaybackState.Stopped && runtime.Motion is null && runtime.Expression is null, "Stop did not release layers");
        Near(runtime.Pose["custom"], 0, "Stop restores base");
        var oneShot = runtime.PlayMotion("move", new(Speed: 2)); runtime.Advance(0.5); Near(oneShot.Time, 1, "Speed");
        runtime.Advance(0.5); Require(oneShot.State == PlaybackState.Completed, "One-shot did not complete");
        Require(notices.Count(e => e.Playback == oneShot && e.Kind == PlaybackEventKind.Completed) == 1, "Completion event count");
        var old = runtime.PlayMotion("hold"); runtime.Advance(0);
        runtime.PlayMotion("opposite"); runtime.Advance(0.5);
        Near(runtime.Pose["custom"], 0, "Crossfade must preserve complementary weights");
        runtime.Advance(0.5); Near(runtime.Pose["custom"], -1, "Crossfade endpoint");
        Require(old.State == PlaybackState.Interrupted, "Replaced motion not interrupted");
        runtime.PlayMotion("unrelated"); runtime.Advance(0.5);
        Near(runtime.Pose["custom"], -0.5, "Missing incoming channel fades to base");
        Near(runtime.Pose["other"], 0.35, "Independent incoming track");
        runtime.StopMotion(0); runtime.Advance(0);
        runtime.BasePose["custom"] = 0.4;
        runtime.SetExpression("offset"); runtime.Advance(0);
        runtime.SetExpression("scale"); runtime.Advance(0.5);
        Near(runtime.Pose["custom"], 0.4, "Expression crossfade combines operations against same source");
        runtime.Advance(0.5); Near(runtime.Pose["custom"], 0.2, "Expression multiply");
        runtime.ClearExpression(); runtime.Advance(0.5); Near(runtime.Pose["custom"], 0.3, "Expression clear fade");
        runtime.Advance(0.5); Near(runtime.Pose["custom"], 0.4, "Expression clears to base");
        runtime.AfterAnimation.Add(new OverrideInput());
        var output = new Output(); runtime.Output = output;
        runtime.PlayMotion("move"); runtime.SetExpression("offset"); runtime.Advance(0.5);
        Near(runtime.Pose["custom"], 0.9, "External input final priority"); Near(output.Value, 0.9, "External output sees mixed pose");
        var other = new AnimationRuntime(model); other.Advance(2); Near(other.Pose["custom"], 0, "Shared asset instance isolation");
        Near(MotionCurve.Step((0, 0), (1, 1), (2, -1)).Sample(1), 1, "Step boundary");
        Near(MotionCurve.Smooth((0, 0), (1, 1)).Sample(0.5), 0.5, "Smooth interpolation");
        Near(new MotionCurve(new(0, 0, Interpolation.CubicHermite, OutTangent: 1), new(1, 1, InTangent: 1)).Sample(0.25), 0.25, "Hermite tangents");
        var frameRates = new[] { 20, 60, 144 };
        foreach (int fps in frameRates)
        {
            var r = new AnimationRuntime(model); r.PlayMotion("move", new(Loop: true));
            for (int i = 0; i < fps; i++) r.Advance(1.0 / fps);
            Near(r.Pose["custom"], 0.5, "Frame rate independent sampling");
        }
        var reentrant = new AnimationRuntime(model);
        reentrant.PlaybackChanged += e => { if (e.Kind == PlaybackEventKind.Completed) reentrant.PlayMotion("hold"); };
        reentrant.PlayMotion("move"); reentrant.Advance(2);
        Require(reentrant.Motion?.Name == "hold", "Playback callbacks cannot start follow-up motion");
        reentrant.Advance(0); Near(reentrant.Pose["custom"], 1, "Callback motion next frame");
        var stopping = new AnimationRuntime(model);
        var stoppingEvents = new List<PlaybackEvent>(); stopping.PlaybackChanged += stoppingEvents.Add;
        var looping = stopping.PlayMotion("move", new(Loop: true)); stopping.Advance(0);
        stopping.Stop(looping, 0.2); stopping.Advance(20);
        Require(looping.State == PlaybackState.Stopped && !stoppingEvents.Any(e => e.Kind == PlaybackEventKind.Looped),
            "Large outgoing update emitted loops after playback had stopped");
        var retained = new AnimationRuntime(model); retained.PlayMotion("hold"); retained.Advance(0);
        Reject(() => retained.PlayMotion("missing"));
        Require(retained.Motion?.Name == "hold", "Rejected request interrupted active motion");
        var recursive = new AnimationRuntime(model);
        bool recursiveRejected = false;
        recursive.PlaybackChanged += _ => {
            try { recursive.Advance(0); } catch (InvalidOperationException) { recursiveRejected = true; }
        };
        recursive.PlayMotion("move"); recursive.Advance(0);
        Require(recursiveRejected, "Playback callbacks could recursively advance");
        Reject(() => runtime.Advance(double.NaN)); Reject(() => runtime.Advance(-1));
        Reject(() => runtime.PlayMotion("missing")); Reject(() => runtime.PlayMotion("move", new(Speed: 0)));
        Reject(() => new ParameterLayout(new[] { new ParameterDefinition("x"), new ParameterDefinition("x") }));
        Reject(() => new MotionCurve(new(0, 0), new(0, 1)));
        Reject(() => new MotionCurve(new Keyframe(0, double.NaN)));
        Reject(() => new MotionDefinition(0, Array.Empty<MotionTrack>()));
        Reject(() => new AnimationModel(new[] { new ParameterDefinition("x") }, new Dictionary<string, MotionDefinition> { ["bad"] = motion }));
        Console.WriteLine($"Animation checks passed: {count} assertions.");
        return count;
    }
    private sealed class OverrideInput : IPoseModifier { public void Apply(ParameterSet pose, double deltaSeconds) => pose["custom"] = 0.9; }
    private sealed class Output : IAnimationOutput
    {
        public double Value;
        public void Evaluate(ParameterSet pose, double deltaSeconds) => Value = pose["custom"];
    }
}
