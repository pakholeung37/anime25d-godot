using Anime25D.Runtime;
using Anime25D.Sample.Core;
using System.Text.Json;
internal static class SampleCompositionChecks
{
    public static int Run(string root)
    {
        int count = 0; double maximum = 0;
        Func<double> Seeded() { uint seed = 1234; return () => { seed = unchecked(seed * 1664525 + 1013904223); return seed / 4294967296.0; }; }
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts/reference.json")));
        foreach (var model in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            var rig = RigDefinition.Parse(model.GetProperty("rig").GetRawText());
            foreach (bool automatic in new[] {false, true})
            {
                using var current = new ModelInstance(SampleModelBuilder.Build(rig, random: Seeded()));
                var oracle = new SampleReferenceAdapter(rig, Seeded());
                if (!automatic) { oracle.AutomaticMotion.DisableAll(); foreach (var id in new[] {"Idle", "Random", "Talk", "Blink", "Physics"}) current.SetComponentEnabled(id, false); }
                foreach (var (key, value) in new[] {(Parameter.HeadYaw, .4), (Parameter.HeadPitch, -.3), (Parameter.MouthShape, .5)})
                { oracle.SetParameter(key, value, true); current.SetParameter(key.ToString(), value, true); }
                for (int step = 0; step < 600; step++)
                {
                    oracle.Step(1.0 / 60); current.Advance(1.0 / 60);
                    foreach (var parameter in SampleParameters.Specs)
                        if (Math.Abs(oracle.Frame[parameter.Key] - current.Frame.Pose[parameter.Key.ToString()]) > 1e-9) throw new Exception($"New plan parameter mismatch {step}/{parameter.Key}: {oracle.Frame[parameter.Key]} != {current.Frame.Pose[parameter.Key.ToString()]}");
                    if (step % 30 != 0) continue;
                    for (int layer = 0; layer < rig.Layers.Length; layer++)
                    {
                        if (Math.Abs(current.Frame.Layers[layer].Opacity - oracle.Parts[layer].Alpha) > 1e-9) throw new Exception("New plan layer alpha mismatch.");
                        if (!current.Frame.Layers[layer].Visible) continue;
                        var actual = current.Frame.Layers[layer].Positions;
                        for (int v = 0; v < actual.Length; v++) { double error = Math.Abs(actual[v] - oracle.Parts[layer].Positions[v]); maximum = Math.Max(maximum, error); if (error > .00025) throw new Exception($"New plan geometry mismatch {step}/{layer}/{v}: {error}"); count++; }
                    }
                }
                double time = current.Animation.Time; var data = current.Frame.Channels["spring-displacements"].ToArray();
                current.Refresh(); current.Refresh();
                if (current.Animation.Time != time || !data.SequenceEqual(current.Frame.Channels["spring-displacements"].ToArray())) throw new Exception("Sample refresh changed simulation.");
                oracle.Instance.Dispose();
            }
        }
        Console.WriteLine($"Sample composition passed: {count} vertex comparisons, max error {maximum}."); return count;
    }
}
