using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anime25D.Sample;
using Anime25D.Sample.Core;
using Anime25D.Examples;
using Godot;

// Actual GPU readback versus CPU deformation, including every exposed parameter boundary.
public partial class BackendChecks : Node
{
    private readonly List<object> reports = [];
    private readonly List<object> benchmarks = [];
    private string output = "";
    private static Func<double> Seeded()
    {
        uint seed = 1234;
        return () => { seed = unchecked(seed * 1664525 + 1013904223); return seed / 4294967296.0; };
    }
    public override async void _Ready()
    {
        try
        {
            output = ProjectSettings.GlobalizePath("res://artifacts/backend-checks");
            System.IO.Directory.CreateDirectory(output);
            foreach (string sample in new[] { "sample-a", "sample-b" })
                await CheckModel(sample);
            // Drain render-thread resource releases before terminating the native renderer.
            await DrawFrames();
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
            {
                reports,
                benchmarks
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"PASS: {reports.Count} CPU/GPU image comparisons; performance report saved.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private async Task DrawFrames()
    {
        for (int index = 0; index < 3; index++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }
    private (SubViewport Viewport, AnimeRigNode Actor) CreateActor(AnimeRigModel model, DeformationBackend backend)
    {
        var size = model.ReadDefinition().Canvas;
        var viewport = new SubViewport { Size = new Vector2I(size.Width, size.Height), TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        var actor = new AnimeRigNode { Backend = backend, AutomaticProcessing = false };
        viewport.AddChild(actor);
        return (viewport, actor);
    }
    private async Task CheckModel(string name)
    {
        var model = GD.Load<AnimeRigModel>($"res://demo/models/{name}/model.tres");
        var cpu = CreateActor(model, DeformationBackend.CpuReference);
        var gpu = CreateActor(model, DeformationBackend.Gpu);
        void Reset(AnimeRigModel? resource = null)
        {
            cpu.Actor.LoadModel(resource ?? model, Seeded());
            gpu.Actor.LoadModel(resource ?? model, Seeded());
            cpu.Actor.Simulation!.AutomaticMotion.DisableAll();
            gpu.Actor.Simulation!.AutomaticMotion.DisableAll();
        }
        async Task Compare(string label)
        {
            cpu.Actor.RefreshPose();
            gpu.Actor.RefreshPose();
            if (gpu.Actor.LastVertexUploadBytes != 0 || cpu.Actor.LastVertexUploadBytes == 0)
                throw new Exception("Unexpected vertex upload path.");
            await DrawFrames();
            using var expected = cpu.Viewport.GetTexture().GetImage();
            using var actual = gpu.Viewport.GetTexture().GetImage();
            var first = expected.GetData();
            var second = actual.GetData();
            long sum = 0;
            int visible = 0, bad = 0, maximum = 0;
            for (int pixel = 0; pixel < first.Length; pixel += 4)
            {
                if (first[pixel + 3] == 0 && second[pixel + 3] == 0)
                    continue;
                visible++;
                int peak = 0;
                for (int channel = 0; channel < 4; channel++)
                {
                    int difference = Math.Abs(first[pixel + channel] - second[pixel + channel]);
                    sum += difference;
                    peak = Math.Max(peak, difference);
                }
                maximum = Math.Max(maximum, peak);
                if (peak > 12)
                    bad++;
            }
            double mean = sum / (visible * 4.0), badFraction = bad / (double)visible;
            reports.Add(new
            {
                sample = name,
                label,
                mean,
                badFraction,
                maximum
            });
            if (visible < 10000 || mean > 0.10 || badFraction > 0.001)
            {
                expected.SavePng(System.IO.Path.Combine(output, name + "-" + label + "-cpu.png"));
                actual.SavePng(System.IO.Path.Combine(output, name + "-" + label + "-gpu.png"));
                throw new Exception($"GPU parity failed: {name}/{label}, mean={mean}, badFraction={badFraction}");
            }
        }

        foreach (var spec in Parameters.Specs)
            foreach (double value in new[] { spec.Min, spec.Max })
            {
                Reset();
                cpu.Actor.SetParameter(spec.Key, value, true);
                gpu.Actor.SetParameter(spec.Key, value, true);
                cpu.Actor.Advance(0);
                gpu.Actor.Advance(0);
                await Compare(spec.Key + (value == spec.Min ? "Min" : "Max"));
            }
        GD.Print(name + ": all 35 parameter boundaries match CPU.");
        Reset();
        foreach (var actor in new[] { cpu.Actor, gpu.Actor })
        {
            var automatic = actor.Simulation!.AutomaticMotion;
            automatic.Idle = automatic.Blink = automatic.Random = automatic.Talk = automatic.Physics = true;
        }
        for (int frame = 1; frame <= 600; frame++)
        {
            cpu.Actor.Advance(1.0 / 60);
            gpu.Actor.Advance(1.0 / 60);
            if (frame % 60 == 0)
                await Compare("animation-" + frame);
        }

        // Code-authored motion + independent expression use the same CPU/GPU output adapter.
        Reset();
        foreach (var actor in new[] { cpu.Actor, gpu.Actor })
        {
            actor.Animation!.PlayMotion("sway");
            actor.Animation.SetExpression("smile");
        }
        for (int frame = 1; frame <= 240; frame++)
        {
            if (frame == 100)
                foreach (var actor in new[] { cpu.Actor, gpu.Actor }) actor.Animation!.PlayMotion("nod");
            if (frame == 140)
                foreach (var actor in new[] { cpu.Actor, gpu.Actor }) actor.Animation!.SetExpression("surprise");
            cpu.Actor.Advance(1.0 / 60);
            gpu.Actor.Advance(1.0 / 60);
            if (frame % 20 == 0) await Compare("code-animation-" + frame);
        }
        if (cpu.Actor.Animation!.Motion is not null || cpu.Actor.Animation.Expression?.Name != "surprise")
            throw new Exception("One-shot completion released independent expression or did not finish.");

        using var profile = new AnimeRigProfile { SettingsJson = """
            {"Motion":{"Breath":{"PeriodSeconds":2.8},"SmoothingRate":9},
             "Physics":{"StiffHair":{"Stiffness":60,"Damping":8,"DisplacementScale":1.8}},
             "Deformation":{"HeadYawPixels":24,"GazeHorizontalPixels":18,"FringeSwayExponent":2.2,"ChestBreathExpansion":0.008},
             "Mesh":{"BaseCellPixels":55,"FringeTransitionPixels":50}}
            """ };
        cpu.Actor.ProfileOverride = gpu.Actor.ProfileOverride = profile;
        Reset();
        foreach (var actor in new[] { cpu.Actor, gpu.Actor })
        {
            actor.Simulation!.AutomaticMotion.Idle = actor.Simulation.AutomaticMotion.Physics = true;
            actor.SetParameter(Parameter.HeadYaw, 0.8, true);
            actor.SetParameter(Parameter.HeadPitch, -0.6, true);
            actor.SetParameter(Parameter.LeftFringeOffset, 0.7, true);
            actor.SetParameter(Parameter.GazeHorizontal, 0.9, true);
            for (int frame = 0; frame < 180; frame++)
                actor.Advance(1.0 / 60);
        }
        await Compare("custom-profile");
        cpu.Actor.ProfileOverride = gpu.Actor.ProfileOverride = null;

        var manifest = JsonNode.Parse(model.Manifest)!;
        int layerIndex = 0;
        foreach (var layer in manifest["layers"]!.AsArray())
            layer!["name"] = "unrelated label " + layerIndex++;
        using var renamed = new AnimeRigModel { Manifest = manifest.ToJsonString(), Textures = model.Textures };
        Reset();
        gpu.Actor.LoadModel(renamed, Seeded());
        gpu.Actor.Simulation!.AutomaticMotion.DisableAll();
        cpu.Actor.Advance(0);
        gpu.Actor.Advance(0);
        await Compare("renamed-parts");

        var alternateManifest = JsonNode.Parse(model.Manifest)!;
        var layers = alternateManifest["layers"]!.AsArray();
        foreach (var layer in layers.Where(layer => layer!["fade"]!.GetValue<string>() == "ClosedEye").ToArray())
        {
            var extra = layer!.DeepClone();
            extra["name"] = "alternative eyelid " + layers.Count;
            extra["role"] = "AlternateClosedEye";
            extra["fade"] = "AlternateClosedEye";
            extra["z"] = layers.Count;
            layers.Add(extra);
        }
        using var alternateModel = new AnimeRigModel { Manifest = alternateManifest.ToJsonString(), Textures = model.Textures };
        Reset(alternateModel);
        cpu.Actor.Simulation!.AutomaticMotion.Blink = gpu.Actor.Simulation!.AutomaticMotion.Blink = true;
        bool sawAlternateBlink = false;
        for (int frame = 1; frame <= 1800; frame++)
        {
            cpu.Actor.Advance(1.0 / 60);
            gpu.Actor.Advance(1.0 / 60);
            sawAlternateBlink |= cpu.Actor.Simulation.BlinkVariant == 2;
            if (frame % 120 == 0) await Compare("alternate-blink-" + frame);
        }
        if (!sawAlternateBlink) throw new Exception("Alternate blink path was not exercised.");

        // A second GPU character must keep its own pose texture, not merely its own CPU arrays.
        Reset();
        var independent = CreateActor(model, DeformationBackend.Gpu);
        independent.Actor.LoadModel(model, Seeded());
        independent.Actor.Simulation!.AutomaticMotion.DisableAll();
        independent.Actor.Advance(0);
        await DrawFrames();
        using var independentBefore = independent.Viewport.GetTexture().GetImage();
        gpu.Actor.SetParameter(Parameter.HeadYaw, 1, true);
        gpu.Actor.Advance(0);
        await DrawFrames();
        using var independentAfter = independent.Viewport.GetTexture().GetImage();
        if (!independentBefore.GetData().SequenceEqual(independentAfter.GetData()))
            throw new Exception("GPU characters share mutable pose resources.");
        independent.Viewport.Free();

        var originalSimulation = gpu.Actor.Simulation;
        using var invalidProfile = new AnimeRigProfile { SettingsJson = "{\"Motion\":null}" };
        gpu.Actor.ProfileOverride = invalidProfile;
        bool invalidRejected = false;
        try { gpu.Actor.LoadModel(model); } catch (ArgumentException) { invalidRejected = true; }
        if (!invalidRejected || !ReferenceEquals(originalSimulation, gpu.Actor.Simulation))
            throw new Exception("Invalid profile destroyed the active character.");
        gpu.Actor.ProfileOverride = null;

        var relaxed = GD.Load<AnimeRigProfile>("res://demo/profiles/Relaxed.tres");
        if (relaxed.ReadConfiguration().Motion.Breath.PeriodSeconds != 4.2)
            throw new Exception("Example Godot profile resource did not load.");

        // Desktop input is independent of actor transforms. Do not move the user's actual pointer.
        Reset();
        gpu.Actor.Position = new Vector2(-10000, -10000);
        gpu.Actor.Scale = Vector2.One * 0.1f;
        gpu.Actor.AutomaticProcessing = true;
        var tracker = new DesktopMouseTracking();
        gpu.Actor.AddChild(tracker);
        double sampledYaw = double.NaN, sampledPitch = double.NaN;
        int poseCallbacks = 0;
        void ObservePose(Anime25D.Core.ParameterSet parameters)
        {
            sampledYaw = parameters[nameof(Parameter.HeadYaw)];
            sampledPitch = parameters[nameof(Parameter.HeadPitch)];
            poseCallbacks++;
        }
        gpu.Actor.FinalizingPose += ObservePose;
        gpu.Actor.Animation!.PlayMotion("nod");
        gpu.Actor.Animation.SetExpression("smile");
        gpu.Actor._Process(1.0 / 60);
        int screen = GetWindow().CurrentScreen;
        Vector2I pointer = DisplayServer.MouseGetPosition();
        Vector2I origin = DisplayServer.ScreenGetPosition(screen);
        Vector2I screenSize = DisplayServer.ScreenGetSize(screen);
        double expectedYaw = Math.Clamp(((pointer.X - origin.X) / (double)screenSize.X * 2 - 1) * tracker.HeadYawGain, -1, 1);
        double expectedPitch = Math.Clamp(-((pointer.Y - origin.Y) / (double)screenSize.Y * 2 - 1) * tracker.HeadPitchGain, -1, 1);
        if (poseCallbacks != 1 || !double.IsFinite(sampledYaw) || Math.Abs(sampledYaw - expectedYaw) > 0.05 || Math.Abs(sampledPitch - expectedPitch) > 0.05)
            throw new Exception("Desktop pointer tracking depends on actor coordinates.");
        double frozenTime = gpu.Actor.Simulation!.TimeMilliseconds;
        gpu.Actor.Playing = false;
        gpu.Actor._Process(1.0 / 60);
        if (gpu.Actor.Simulation.TimeMilliseconds != frozenTime || poseCallbacks != 1) throw new Exception("Paused actor advanced simulation or sampled its input.");
        gpu.Actor.AutomaticProcessing = false;
        gpu.Actor.Playing = true;
        gpu.Actor.Position = Vector2.Zero;
        gpu.Actor.Scale = Vector2.One;

        tracker.Enabled = false;
        gpu.Actor.SetParameter(Parameter.HeadYaw, 0.25, true);
        gpu.Actor.SetPreset("smile");
        gpu.Actor.Advance(0);
        if (sampledYaw != 0.25 || gpu.Actor.Simulation.ActivePreset != "smile")
            throw new Exception("Disabled extension changed authored targets or expression lock.");

        // Character-node subscriptions survive reloads but must detach from the old simulation.
        var previousSimulation = gpu.Actor.Simulation;
        gpu.Actor.LoadModel(model, Seeded());
        int callbacksAfterReload = poseCallbacks;
        if (!previousSimulation.Instance.IsDisposed) throw new Exception("Previous model instance was not disposed.");
        if (poseCallbacks != callbacksAfterReload) throw new Exception("Previous simulation retained node pose subscriptions.");
        tracker.Enabled = true;
        gpu.Actor.Advance(0);
        if (poseCallbacks != callbacksAfterReload + 1) throw new Exception("Pose extension did not survive model reload.");

        tracker.Character = cpu.Actor;
        gpu.Actor.SetParameter(Parameter.HeadYaw, 0.25, true);
        gpu.Actor.Advance(0);
        if (sampledYaw != 0.25) throw new Exception("Retargeted extension remained attached to its previous character.");
        double retargetedYaw = double.NaN;
        void ObserveRetargeted(Anime25D.Core.ParameterSet parameters) => retargetedYaw = parameters[nameof(Parameter.HeadYaw)];
        cpu.Actor.FinalizingPose += ObserveRetargeted;
        cpu.Actor.Advance(0);
        if (!double.IsFinite(retargetedYaw) || Math.Abs(retargetedYaw - expectedYaw) > 0.05)
            throw new Exception("Extension did not bind its explicit character.");
        cpu.Actor.FinalizingPose -= ObserveRetargeted;
        tracker.Character = null;
        tracker.Free();
        gpu.Actor.Advance(0);
        if (sampledYaw != 0.25) throw new Exception("Removed extension still overrides the pose.");
        gpu.Actor.FinalizingPose -= ObservePose;
        GD.Print(name + ": optional mouse extension lifecycle passed.");

        Reset();
        foreach (var actor in new[] { cpu.Actor, gpu.Actor })
        {
            actor.Simulation!.AutomaticMotion.Idle = actor.Simulation.AutomaticMotion.Physics = true;
            for (int frame = 0; frame < 120; frame++)
                actor.Advance(1.0 / 60);
            var timings = new List<double>();
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            for (int repetition = 0; repetition < 5; repetition++)
            {
                var watch = Stopwatch.StartNew();
                for (int frame = 0; frame < 120; frame++)
                    actor.Advance(1.0 / 60);
                timings.Add(watch.Elapsed.TotalMilliseconds / 120);
            }
            double allocations = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / 600.0;
            benchmarks.Add(new
            {
                sample = name,
                backend = actor.ActiveBackend.ToString(),
                medianCpuSubmissionMilliseconds = timings.Order().ElementAt(2),
                managedBytesPerStep = allocations,
                vertexUploadBytesPerStep = actor.LastVertexUploadBytes
            });
        }
        cpu.Viewport.Free();
        gpu.Viewport.Free();
        GD.Print(name + ": animation, custom profile, renamed parts and CPU submission benchmarks passed.");
    }
}
