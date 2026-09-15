using System.Text.Json;
using Anime25D;
using Anime25D.Core;
using Godot;

// Executed in a real GPU renderer, separately from the browser-free numerical suite.
public partial class RenderChecks : Node
{
    private static Func<double> Seeded()
    {
        uint seed = 1234;
        return () => { seed = unchecked(seed * 1664525 + 1013904223); return seed / 4294967296.0; };
    }
    private void Require(bool condition, string label)
    {
        if (!condition)
            throw new Exception(label);
        GD.Print("PASS " + label);
    }
    private async Task DrawFrames(int count = 3)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }
    public override async void _Ready()
    {
        try
        {
            await Run();
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    private async Task Run()
    {
        var artifactRoot = ProjectSettings.GlobalizePath("res://artifacts/godot");
        System.IO.Directory.CreateDirectory(artifactRoot);
        using var document = JsonDocument.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://artifacts/reference.json")));
        var models = document.RootElement.GetProperty("models").EnumerateArray().Where(m => m.GetProperty("name").GetString() != "long-blink");
        int captures = 0;
        foreach (var entry in models)
        {
            string name = entry.GetProperty("name").GetString()!;
            var model = GD.Load<AnimeRigModel>($"res://demo/models/{name}/model.tres");
            var definition = model.ReadDefinition();
            var viewport = new SubViewport { Size = new Vector2I(definition.Canvas.Width, definition.Canvas.Height), TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            var actor = new AnimeRigNode { AutomaticProcessing = false };
            viewport.AddChild(actor);
            foreach (var test in entry.GetProperty("cases").EnumerateArray())
            {
                string testName = test.GetProperty("name").GetString()!;
                if (!new[] { "neutral", "turn", "closed", "wink", "crossfade", "reordered", "animation60" }.Contains(testName))
                    continue;
                actor.LoadModel(model, Seeded());
                var sim = actor.Simulation!;
                foreach (var p in test.GetProperty("pose").EnumerateObject())
                    sim.SetParameter(ParameterNames.Parse(p.Name), p.Value.GetDouble(), true);
                bool animated = test.TryGetProperty("animated", out var value) && value.GetBoolean();
                if (!animated)
                    sim.AutomaticMotion.DisableAll();
                if (testName == "reordered")
                    foreach (var part in sim.Parts)
                        part.DrawOrder = sim.Parts.Length - part.DrawOrder;
                int frames = test.GetProperty("frames").GetInt32();
                if (frames == 0)
                    actor.Advance(0);
                else
                    for (int i = 0; i < frames; i++)
                        actor.Advance(1.0 / 60);
                await DrawFrames();
                using var image = viewport.GetTexture().GetImage();
                var result = image.SavePng(System.IO.Path.Combine(artifactRoot, $"{name}-{testName}.png"));
                Require(result == Error.Ok, name + "/" + testName + " rendered");
                captures++;
                Require(image.GetPixel(0, 0).A == 0, name + "/" + testName + " transparent corner");
                int visiblePixels = 0;
                var data = image.GetData();
                for (int i = 3; i < data.Length; i += 4)
                    if (data[i] > 0)
                        visiblePixels++;
                Require(visiblePixels > 10000, name + "/" + testName + " nonempty image");
            }
            // Reordering must not accidentally remove eye masks; hiding an eye white must clip its iris away.
            actor.LoadModel(model, Seeded());
            actor.Simulation!.AutomaticMotion.DisableAll();
            actor.Advance(0);
            foreach (var p in actor.Simulation.Parts)
                if (p.Definition.Role == PartRole.EyeWhite)
                    p.Visible = false;
            actor.RefreshPose();
            await DrawFrames();
            using var hiddenWhite = viewport.GetTexture().GetImage();
            foreach (var p in actor.Simulation.Parts)
                if (p.Definition.Role == PartRole.Iris)
                    p.Visible = false;
            actor.RefreshPose();
            await DrawFrames();
            using var hiddenIris = viewport.GetTexture().GetImage();
            Require(hiddenWhite.GetData().SequenceEqual(hiddenIris.GetData()), name + " iris disappears when eye mask is absent");
            // Clear/reload repeatedly, and verify a second node can share resources without sharing mutable state.
            var other = new AnimeRigNode { AutomaticProcessing = false };
            viewport.AddChild(other);
            other.LoadModel(model, Seeded());
            var frozenPositions = other.Simulation!.Parts[0].Positions.ToArray();
            actor.Simulation.SetParameter(Parameter.HeadYaw, 1, true);
            actor.Advance(0.05);
            Require(other.Simulation.Parts[0].Positions.SequenceEqual(frozenPositions), name + " independent instance geometry");
            for (int i = 0; i < 8; i++)
                actor.LoadModel(model, Seeded());
            other.Free();
            actor.ClearModel();
            await DrawFrames();
            using var empty = viewport.GetTexture().GetImage();
            Require(empty.GetData().Where((_, i) => i % 4 == 3).All(a => a == 0), name + " clearing frees visible content");
            viewport.Free();
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(artifactRoot, "render-tests.json"), JsonSerializer.Serialize(new
        {
            status = "passed",
            captures,
            renderer = RenderingServer.GetCurrentRenderingMethod()
        }));
        GD.Print("GPU rendering checks passed: " + captures + " captures.");
    }
}
