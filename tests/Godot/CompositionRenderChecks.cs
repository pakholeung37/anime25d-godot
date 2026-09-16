using Anime25D;
using Anime25D.Runtime;
using Anime25D.Sample;
using Anime25D.Sample.Core;
using Anime25D.Sample.Rendering;
using Godot;
using System.Text.Json;

// Tests the production composition path, rather than the legacy test-only adapter.
public partial class CompositionRenderChecks : Node
{
    private static Func<double> Seeded() { uint seed = 1234; return () => { seed = unchecked(seed * 1664525 + 1013904223); return seed / 4294967296.0; }; }
    private async Task DrawFrames() { for (int i = 0; i < 3; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); RenderingServer.ForceDraw(); } }
    private (SubViewport View, AnimeModelNode Actor) Create(AnimeRigModel asset, GeometryBackend backend)
    {
        var rig = asset.ReadDefinition();
        var viewport = new SubViewport { Size = new(rig.Canvas.Width, rig.Canvas.Height), TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport); var actor = new AnimeModelNode { AutomaticProcessing = false }; viewport.AddChild(actor);
        actor.Load(new ModelView(SampleModelBuilder.Build(rig, asset.Profile?.ReadConfiguration(), asset.Animations, Seeded()), asset.Textures, new SampleGpuFactory()), backend);
        return (viewport, actor);
    }
    public override async void _Ready()
    {
        try
        {
            GD.Print("Starting production composition render checks.");
            int comparisons = 0; double worstMean = 0, worstBad = 0;
            foreach (string sample in new[] { "sample-a", "sample-b" })
            {
                GD.Print("Checking " + sample);
                var asset = GD.Load<AnimeRigModel>($"res://demo/models/{sample}/model.tres");
                var cpu = Create(asset, GeometryBackend.Cpu); var gpu = Create(asset, GeometryBackend.Gpu);
                void Disable() { foreach (var a in new[] {cpu.Actor, gpu.Actor}) foreach (string id in new[] {"Idle", "Random", "Talk", "Blink", "Physics"}) a.Instance!.SetComponentEnabled(id, false); }
                async Task Compare(string label)
                {
                    cpu.Actor.RefreshPose(); gpu.Actor.RefreshPose(); await DrawFrames();
                    using var a = cpu.View.GetTexture().GetImage(); using var b = gpu.View.GetTexture().GetImage();
                    byte[] av = a.GetData(), bv = b.GetData(); double total = 0; int bad = 0;
                    for (int i = 0; i < av.Length; i++) { int error = Math.Abs(av[i] - bv[i]); total += error; if (error > 8) bad++; }
                    double mean = total / av.Length, fraction = bad / (double)av.Length;
                    worstMean = Math.Max(worstMean, mean); worstBad = Math.Max(worstBad, fraction); comparisons++;
                    if (mean > .03 || fraction > .0001) throw new Exception($"Production CPU/GPU mismatch {sample}/{label}: {mean}/{fraction}");
                    if (gpu.Actor.LastVertexUploadBytes != 0) throw new Exception("GPU uploaded CPU vertices.");
                }
                Disable();
                foreach (var parameter in SampleParameters.Specs)
                    foreach (double value in new[] { parameter.Min, parameter.Max })
                    {
                        foreach (var a in new[] {cpu.Actor, gpu.Actor}) { a.Instance!.ResetInputs(); a.Instance.SetParameter(parameter.Key.ToString(), value, true); }
                        await Compare(parameter.Key.ToString());
                    }
                foreach (var a in new[] {cpu.Actor, gpu.Actor}) { a.Instance!.ResetInputs(); foreach (string id in new[] {"Idle", "Random", "Talk", "Blink", "Physics"}) a.Instance.SetComponentEnabled(id, true); }
                for (int i = 0; i < 300; i++) { cpu.Actor.Advance(1.0 / 60); gpu.Actor.Advance(1.0 / 60); if (i % 60 == 0) await Compare("automatic"); }
                foreach (string expression in ExpressionPose.Defaults.Keys)
                { cpu.Actor.Instance!.Animation.SetExpression(expression); gpu.Actor.Instance!.Animation.SetExpression(expression); cpu.Actor.Advance(.3); gpu.Actor.Advance(.3); await Compare(expression); }
                cpu.Actor.Instance!.Layers[0].Visible = gpu.Actor.Instance!.Layers[0].Visible = false; await Compare("hidden");
                cpu.Actor.ClearModel(); gpu.Actor.ClearModel(); cpu.View.Free(); gpu.View.Free(); await DrawFrames();
            }
            var report = new { comparisons, worstMean, worstBad, status = "passed" };
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://artifacts/composition-render.json"), JsonSerializer.Serialize(report));
            GD.Print(JsonSerializer.Serialize(report)); GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
