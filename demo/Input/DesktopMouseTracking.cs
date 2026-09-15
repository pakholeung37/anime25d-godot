using System;
using Anime25D.Core;
using Godot;

namespace Anime25D.Examples;

/// <summary>Demo-only desktop adapter. Attach under an AnimeRigNode or assign Character explicitly.</summary>
public partial class DesktopMouseTracking : Node
{
    [Export] public bool Enabled { get; set; } = true;
    [Export]
    public AnimeRigNode? Character
    {
        get => character;
        set
        {
            if (character == value) return;
            Unbind();
            character = value;
            if (IsInsideTree()) Bind();
        }
    }

    [Export] public double HeadYawGain { get => response.HeadYawGain; set => response = response with { HeadYawGain = ValidGain(value) }; }
    [Export] public double HeadPitchGain { get => response.HeadPitchGain; set => response = response with { HeadPitchGain = ValidGain(value) }; }
    [Export] public double HorizontalGazeGain { get => response.HorizontalGazeGain; set => response = response with { HorizontalGazeGain = ValidGain(value) }; }
    [Export] public double VerticalGazeGain { get => response.VerticalGazeGain; set => response = response with { VerticalGazeGain = ValidGain(value) }; }

    private AnimeRigNode? character;
    private AnimeRigNode? boundCharacter;
    private MouseTrackingResponse response = new();

    public override void _EnterTree() => Bind();
    public override void _ExitTree() => Unbind();

    private void Bind()
    {
        var target = character ?? GetParent() as AnimeRigNode;
        if (target is null || !GodotObject.IsInstanceValid(target)) return;
        boundCharacter = target;
        target.PreparingPose += Apply;
    }

    private void Unbind()
    {
        if (boundCharacter is not null && GodotObject.IsInstanceValid(boundCharacter))
            boundCharacter.PreparingPose -= Apply;
        boundCharacter = null;
    }

    private void Apply(Parameters parameters)
    {
        if (!Enabled || boundCharacter is null || !GodotObject.IsInstanceValid(boundCharacter) || !boundCharacter.IsInsideTree()) return;
        int screen = boundCharacter.GetWindow().CurrentScreen;
        Vector2I size = DisplayServer.ScreenGetSize(screen);
        if (size.X <= 0 || size.Y <= 0) return;
        Vector2I origin = DisplayServer.ScreenGetPosition(screen);
        Vector2I pointer = DisplayServer.MouseGetPosition();
        response.Apply(parameters,
            (pointer.X - origin.X) / (double)size.X * 2 - 1,
            (pointer.Y - origin.Y) / (double)size.Y * 2 - 1);
    }

    private static double ValidGain(double value) => double.IsFinite(value)
        ? value : throw new ArgumentOutOfRangeException(nameof(value), "Tracking gains must be finite.");
}
