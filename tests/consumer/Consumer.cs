using System;
using Anime25D;
using Anime25D.Core;
using Godot;

public partial class Consumer : Node
{
    public override void _Ready()
    {
        try
        {
            var model = GD.Load<AnimeRigModel>("res://models/sample-a/model.tres");
            var actor = new AnimeRigNode { Model = model, AutomaticProcessing = false };
            AddChild(actor);
            actor.SetParameter("angleX", 0.75, true);
            for (int i = 0; i < 120; i++) actor.Advance(1.0 / 60);
            if (actor.Simulation?.Parts.Length != 20) throw new Exception("Missing model parts.");
            var other = new AnimeRigNode { Model = model }; AddChild(other);
            if (other.Simulation?.Target[Parameter.HeadYaw] != 0) throw new Exception("Shared parameter state.");
            GD.Print("PASS: addon builds and loads in an independent Godot project without demo, tools, implicit usings, or editor plugin enablement.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
