using System.Text.Json;
using Anime25D.Core;
using Anime25D.Examples;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts/reference.json")));
var reference = document.RootElement;
long comparisons = 0; double maxPositionError = 0, maxParameterError = 0; int cases = 0; bool sawLongBlink = false;
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
void Near(double actual, double expected, double tolerance, string label)
{
    comparisons++;
    Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{label}: actual={actual:R}, expected={expected:R}, delta={actual - expected:R}");
}
Func<double> Seeded()
{
    uint seed = 1234;
    return () => { seed = unchecked(seed * 1664525 + 1013904223); return seed / 4294967296.0; };
}
foreach (var spec in Parameters.Specs)
{
    Near(spec.Default, reference.GetProperty("defaults").GetProperty(spec.LegacyName).GetDouble(), 0, "default " + spec.Key);
    var range = reference.GetProperty("ranges").GetProperty(spec.LegacyName);
    Near(spec.Min, range[0].GetDouble(), 0, "min " + spec.Key); Near(spec.Max, range[1].GetDouble(), 0, "max " + spec.Key);
}
foreach (var model in reference.GetProperty("models").EnumerateArray())
{
    var definition = RigDefinition.Parse(model.GetProperty("rig").GetRawText());
    foreach (var test in model.GetProperty("cases").EnumerateArray())
    {
        cases++; var sim = new RigSimulation(definition, Seeded());
        var label = model.GetProperty("name").GetString() + "/" + test.GetProperty("name").GetString();
        foreach (var p in test.GetProperty("pose").EnumerateObject()) sim.SetParameter(ParameterNames.Parse(p.Name), p.Value.GetDouble(), true);
        if (!test.TryGetProperty("animated", out var animated) || !animated.GetBoolean()) sim.AutomaticMotion.DisableAll();
        if (test.TryGetProperty("physics", out var physics)) sim.AutomaticMotion.Physics = physics.GetBoolean();
        if (test.TryGetProperty("preset", out var preset)) sim.SetPreset(preset.GetString());
        if (test.TryGetProperty("mouse", out var mouse))
        {
            var response = new MouseTrackingResponse();
            double horizontal = mouse.GetProperty("x").GetDouble(), vertical = mouse.GetProperty("y").GetDouble();
            sim.PreparingPose += parameters => response.Apply(parameters, horizontal, vertical);
        }
        var byName = sim.Parts.ToDictionary(p => p.Definition.Name);
        int previous = 0; double fps = test.TryGetProperty("fps", out var fp) ? fp.GetDouble() : 60;
        foreach (var expected in test.GetProperty("checkpoints").EnumerateArray())
        {
            int frame = expected.GetProperty("frame").GetInt32();
            if (frame == 0) sim.Step(0);
            for (; previous < frame; previous++) sim.Step(1 / fps);
            var values = expected.GetProperty("values");
            foreach (var spec in Parameters.Specs)
            {
                double ev = values.GetProperty(spec.LegacyName).GetDouble();
                maxParameterError = Math.Max(maxParameterError, Math.Abs(sim.Frame[spec.Key] - ev));
                Near(sim.Frame[spec.Key], ev, 1e-9, label + "/" + spec.Key);
            }
            Near(sim.Frame.Breath, values.GetProperty("breath").GetDouble(), 1e-10, label + "/breath");
            Near(sim.Frame.BreathHead, values.GetProperty("breathHead").GetDouble(), 1e-10, label + "/breathHead");
            Near(sim.Frame.IrisBounceX, values.GetProperty("irisBounceX").GetDouble(), 1e-9, label + "/irisBounceX");
            Near(sim.Frame.IrisBounceY, values.GetProperty("irisBounceY").GetDouble(), 1e-9, label + "/irisBounceY");
            Near(sim.Bounce.Displacement, expected.GetProperty("bounce").GetDouble(), 1e-9, label + "/bounce");
            Require(sim.BlinkVariant == expected.GetProperty("blinkVariant").GetInt32(), label + "/blinkVariant");
            sawLongBlink |= sim.BlinkVariant == 2;
            foreach (var ep in expected.GetProperty("parts").EnumerateArray())
            {
                var part = byName[ep.GetProperty("name").GetString()!];
                Near(part.Alpha, ep.GetProperty("alpha").GetDouble(), 1e-9, label + "/alpha");
                int i = 0;
                foreach (var value in ep.GetProperty("positions").EnumerateArray())
                {
                    double ev = value.GetDouble(); maxPositionError = Math.Max(maxPositionError, Math.Abs(part.Positions[i] - ev));
                    Near(part.Positions[i], ev, 0.00025, label + "/" + part.Definition.Name + "/vertex " + i); i++;
                }
                Require(i == part.Positions.Length, "Mesh topology differs.");
            }
        }
    }
    // Resource sharing must not share mutable positions, parameters or springs.
    var first = new RigSimulation(definition, Seeded()); var second = new RigSimulation(definition, Seeded());
    first.SetParameter(Parameter.HeadYaw, 1, true); first.Step(1.0 / 60);
    Require(second.Target[Parameter.HeadYaw] == 0 && second.TimeMilliseconds == 0, "Instance state leaked.");
    Require(!ReferenceEquals(first.Parts[0].Positions, second.Parts[0].Positions), "Shared mutable vertex buffer.");
    var stable = new RigSimulation(definition, Seeded());
    for (int i = 0; i < 300; i++) stable.Step(i % 2 == 0 ? 0.5 : 1.0 / 144);
    Require(stable.Parts.All(p => p.Positions.All(float.IsFinite)), "Variable frame rate produced non-finite vertices.");
}
Require(sawLongBlink, "Long-blink reference coverage was not exercised.");
foreach (var fps in new[] { 10, 30, 60, 144 })
{
    var spring = new Spring(); for (int i = 0; i < fps * 10; i++) spring.Step(20, 140, 4.2, 1.0 / fps);
    Near(spring.Position, 20, 0.01, "spring convergence");
}
int architectureChecks = ArchitectureChecks.Run(root);
var report = new { cases, comparisons, maxPositionError, maxParameterError, sawLongBlink, architectureChecks, status = "passed" };
File.WriteAllText(Path.Combine(root, "artifacts/core-tests.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));
