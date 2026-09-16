using System.Text.Json;
using Anime25D.Sample.Core;

internal static class ArchitectureChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (Exception error) when (error is ArgumentException or JsonException) { rejected = true; }
            Require(rejected, message);
        }
        Func<double> Seeded()
        {
            uint seed = 1234;
            return () => { seed = unchecked(seed * 1664525 + 1013904223); return seed / 4294967296.0; };
        }

        foreach (string sample in new[] { "sample-a", "sample-b" })
        {
            var definition = RigDefinition.Parse(File.ReadAllText(Path.Combine(root, "demo/models", sample, "model.rig.json")));
            Require(definition.Version == 2, "Samples must use semantic v2 rigs.");
            var renamed = definition with { Name = "Unrelated model", Layers = definition.Layers.Select((part, index) => part with { Name = "任意名称 " + index }).ToArray() };
            var original = new SampleReferenceAdapter(definition, Seeded());
            var renamedSimulation = new SampleReferenceAdapter(renamed, Seeded());
            for (int frame = 0; frame < 600; frame++) { original.Step(1.0 / 60); renamedSimulation.Step(1.0 / 60); }
            for (int part = 0; part < original.Parts.Length; part++)
            {
                Require(original.Parts[part].Positions.SequenceEqual(renamedSimulation.Parts[part].Positions), "Names affected deformation.");
                Require(original.Parts[part].Alpha == renamedSimulation.Parts[part].Alpha, "Names affected visibility.");
            }

            var profile = RigProfile.Parse("""
                {"Parameters":{"HeadYaw":{"Default":0.4,"Minimum":-0.6,"Maximum":0.6}},
                 "Motion":{"Idle":{"YawPrimary":{"Amplitude":0,"AngularFrequency":0},"YawSecondary":{"Amplitude":0,"AngularFrequency":0}}},
                 "Deformation":{"HeadYawPixels":28}, "Mesh":{"BaseCellPixels":60},
                 "Expressions":{"custom":{"LeftEyeOpenness":0.3,"RightEyeOpenness":0.8,"EyebrowHeight":0.2,"MouthOpenness":0.1,"MouthShape":0.4,"IrisScale":1}}}
                """);
            var custom = new SampleReferenceAdapter(definition, Seeded(), profile);
            Require(custom.Target[Parameter.HeadYaw] == 0.4, "Profile parameter default ignored.");
            custom.SetParameter(Parameter.HeadYaw, 1, true);
            Require(custom.Target[Parameter.HeadYaw] == 0.6, "Profile parameter range ignored.");
            custom.ResetParameters();
            Require(custom.Target[Parameter.HeadYaw] == 0.4, "Reset did not use profile default.");
            custom.SetPreset("custom", true);
            Require(custom.Frame[Parameter.LeftEyeOpenness] == 0.3, "Configured expression ignored.");
            Require(custom.Parts.Zip(original.Parts).Any(pair => pair.First.Geometry.VertexCount != pair.Second.Geometry.VertexCount), "Mesh profile ignored.");

            var authored = new Dictionary<Parameter, ParameterRange> { [Parameter.HeadYaw] = new(0.2, -1, 1) };
            var isolated = new SampleReferenceAdapter(definition, Seeded(), new RigProfile { Parameters = authored });
            authored[Parameter.HeadYaw] = new(0.9, -1, 1);
            Require(isolated.Profile.Parameters[Parameter.HeadYaw].Default == 0.2, "Authoring dictionary leaked into live instance.");

            var cpu = new SampleReferenceAdapter(definition, Seeded());
            var gpu = new SampleReferenceAdapter(definition, Seeded()) { EvaluateCpuGeometry = false };
            var rest = gpu.Parts[0].Positions.ToArray();
            for (int frame = 0; frame < 240; frame++) { cpu.Step(1.0 / 60); gpu.Step(1.0 / 60); }
            Require(gpu.Parts[0].Positions.SequenceEqual(rest), "GPU mode evaluated CPU vertices.");
            Require(cpu.Frame.Values.Snapshot().SequenceEqual(gpu.Frame.Values.Snapshot()), "Render backend altered motion state.");
            gpu.EvaluateCpuGeometry = true;
            gpu.UpdateGeometry();
            Require(cpu.Parts.Zip(gpu.Parts).All(pair => pair.First.Alpha < LayerVisibility.RenderThreshold || pair.First.Positions.SequenceEqual(pair.Second.Positions)), "CPU readback evaluator differs after GPU simulation.");
            Reject(() => custom.Step(double.NaN), "Invalid delta accepted.");

            var animatedSample = new SampleReferenceAdapter(definition, Seeded());
            animatedSample.AutomaticMotion.DisableAll();
            animatedSample.Runtime.PlayMotion("nod");
            animatedSample.Runtime.SetExpression("smile");
            for (int frame = 0; frame < 5; frame++) animatedSample.Step(0.05);
            Require(animatedSample.Frame[Parameter.HeadPitch] > 0.1 && animatedSample.Current[Parameter.HeadPitch] == 0,
                "Code-authored motion was not applied after sample baseline smoothing.");
            Require(animatedSample.Frame[Parameter.LeftEyeOpenness] == 0 && animatedSample.ActivePreset is null,
                "Generic expression did not reach sample output or enabled legacy preset lock.");
            Require(animatedSample.Runtime.Pose[nameof(Parameter.HeadPitch)] == animatedSample.Frame[Parameter.HeadPitch],
                "Sample output does not consume generic pose channels.");

            var extended = new SampleReferenceAdapter(definition, Seeded());
            extended.AutomaticMotion.DisableAll();
            extended.SetParameter(Parameter.HeadYaw, 0.2, true);
            extended.SetPreset("smile", true);
            void OverridePose(Parameters parameters) => parameters.Set(Parameter.HeadYaw, 0.6);
            extended.PreparingPose += OverridePose;
            extended.Step(0.05);
            double expectedYaw = 0.2 + (0.6 - 0.2) * (1 - Math.Exp(-0.05 * 14));
            Require(Math.Abs(extended.Frame[Parameter.HeadYaw] - expectedYaw) < 1e-12, "Generic pose hook did not run before smoothing.");
            Require(extended.Target[Parameter.HeadYaw] == 0.2 && extended.ActivePreset == "smile", "Frame modifier changed persistent targets or expression lock.");
            extended.PreparingPose -= OverridePose;
            extended.Step(0.05);
            Require(extended.Frame[Parameter.HeadYaw] < expectedYaw, "Detached modifier left a persistent override.");
        }
        foreach (var spec in Parameters.Specs)
        {
            Require(ParameterNames.Parse(spec.LegacyName) == spec.Key, "Legacy parameter mapping drift.");
            Require(ParameterNames.Parse(spec.Key.ToString()) == spec.Key, "Readable parameter mapping drift.");
        }
        Reject(() => RigProfile.Parse("{\"Motion\":{\"Breath\":{\"PeriodSeconds\":0}}}"), "Zero breath period accepted.");
        Reject(() => RigProfile.Parse("{\"Deformation\":{\"EyeFadeWidth\":0}}"), "Zero fade width accepted.");
        Reject(() => RigProfile.Parse("{\"Physics\":{\"IntegrationStepSeconds\":0}}"), "Zero integration step accepted.");
        Reject(() => RigProfile.Parse("{\"Motion\":null}"), "Null settings accepted.");
        Reject(() => RigProfile.Parse("{\"Motion\":{\"SmothingRate\":10}}"), "Misspelled profile setting silently accepted.");
        Reject(() => RigProfile.Parse("{\"Parameters\":{\"HeadYaw\":{\"Default\":2,\"Minimum\":-1,\"Maximum\":1}}}"), "Invalid parameter default accepted.");
        Reject(() => ParameterNames.Parse("999"), "Invalid parameter index accepted.");
        string runtimeSources = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "addons/anime25d"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Require(!System.Text.RegularExpressions.Regex.IsMatch(runtimeSources, @"\b(Mouse|PointerInput|GazeSettings|GazeMotion|DesktopMouseTracking|DesktopMouseInput|MouseTrackingResponse|DisplayServer)\b"), "Runtime depends on mouse input implementation.");
        Require(!System.Text.RegularExpressions.Regex.IsMatch(runtimeSources,
            @"\b(HeadYaw|HeadPitch|BlinkVariant|RigDeformer|PhysicsSolver|AutomaticMotion|ExpressionPose|PartRole|JsonSerializer|File|Directory)\b"),
            "Generic addon contains sample semantics or IO.");
        Require(!runtimeSources.Contains("Anime25D.Sample") && !runtimeSources.Contains("demo/SampleRig"), "Addon depends on sample implementation.");
        string coreOnly = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "addons/anime25d/Runtime"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Require(!coreOnly.Contains("using Godot") && !coreOnly.Contains("System.IO"), "Core depends on engine or IO.");
        Require(!System.IO.File.Exists(Path.Combine(root, "demo/SampleRig/Rendering/RigRenderer.cs")), "Sample retains a parallel renderer.");
        Require(!runtimeSources.Contains("IModelBehavior") && !runtimeSources.Contains("CreateBehavior"), "Runtime retains the monolithic behavior API.");
        string sampleSources = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "demo/SampleRig"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Require(!sampleSources.Contains("SampleReferenceAdapter") && !sampleSources.Contains("SampleBehavior") && !sampleSources.Contains("MotionPipeline"), "Production sample depends on legacy execution.");
        Require(!sampleSources.Contains("class PhysicsSolver") && !sampleSources.Contains("class FrameParameters"), "Sample duplicates runtime state or physics scheduling.");
        Console.WriteLine($"Architecture checks passed: {checks} assertions.");
        return checks;
    }
}
