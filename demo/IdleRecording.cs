using System.Security.Cryptography;
using System.Text.Json;
using Anime25D;
using Godot;

// Capture only after the requested simulation frame has actually reached the GPU.
public partial class IdleRecording : Node
{
    public override async void _Ready()
    {
        try
        {
            const int fps = 30, frames = 9 * fps;
            string output = ProjectSettings.GlobalizePath("res://artifacts/idle-frames");
            System.IO.Directory.CreateDirectory(output);
            var viewport = new SubViewport
            {
                Size = new Vector2I(640, 640),
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            AddChild(viewport);
            viewport.AddChild(new ColorRect { Size = new Vector2(640, 640), Color = new Color(0.10f, 0.125f, 0.16f) });
            var actor = new AnimeRigNode
            {
                Model = GD.Load<AnimeRigModel>("res://demo/models/sample-a/model.tres"),
                AutomaticProcessing = false,
                Scale = Vector2.One * 0.48f,
                Position = new Vector2(12.8f, 12.8f)
            };
            viewport.AddChild(actor);
            var sim = actor.Simulation!;
            sim.AutomaticMotion.DisableAll();
            sim.AutomaticMotion.Idle = sim.AutomaticMotion.Blink = sim.AutomaticMotion.Random = sim.AutomaticMotion.Talk = sim.AutomaticMotion.Physics = true;
            // Let the springs settle before starting the clip.
            for (int i = 0; i < fps * 3; i++)
                actor.Advance(1.0 / fps);
            var hashes = new HashSet<string>();
            for (int frame = 0; frame < frames; frame++)
            {
                actor.Advance(1.0 / fps);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = viewport.GetTexture().GetImage();
                hashes.Add(Convert.ToHexString(SHA256.HashData(image.GetData())));
                if (image.SavePng(System.IO.Path.Combine(output, $"frame-{frame:D4}.png")) != Error.Ok)
                    throw new Exception("Could not save recording frame.");
                if ((frame + 1) % fps == 0)
                    GD.Print($"Runtime recording: {frame + 1}/{frames} frames");
            }
            if (hashes.Count != frames)
                throw new Exception($"Capture contains repeated frames: {hashes.Count}/{frames} unique.");
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "recording.json"), JsonSerializer.Serialize(new
            {
                fps,
                frames,
                durationSeconds = 9,
                uniqueFrames = hashes.Count,
                motion = "idle + blink + random + talk + physics"
            }));
            GD.Print("PASS: 270 unique rendered animation frames, 9 seconds.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
