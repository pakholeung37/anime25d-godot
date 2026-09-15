using Anime25D.Core;
using Godot;

namespace Anime25D;

/// <summary>Optional Godot clock. Supply a code-created runtime and external output implementation.</summary>
[GlobalClass]
public partial class AnimeAnimationNode : Node2D
{
    public AnimationRuntime? Runtime { get; set; }
    [Export] public bool AutomaticProcessing { get; set; } = true;
    [Export] public bool Playing { get; set; } = true;
    public override void _Process(double delta)
    {
        if (AutomaticProcessing && Playing) Runtime?.Advance(delta);
    }
    public void Advance(double delta) => Runtime?.Advance(delta);
}
