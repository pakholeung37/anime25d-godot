using Anime25D.Core;
using Godot;

namespace Anime25D.Rendering;

internal static class DesktopMouseInput
{
    public static void Update(RigSimulation simulation, Window window)
    {
        int screen = window.CurrentScreen;
        Vector2I origin = DisplayServer.ScreenGetPosition(screen);
        Vector2I size = DisplayServer.ScreenGetSize(screen);
        Vector2I pointer = DisplayServer.MouseGetPosition();
        simulation.Pointer.IsAvailable = size.X > 0 && size.Y > 0;
        if (!simulation.Pointer.IsAvailable)
            return;
        simulation.Pointer.Horizontal = (pointer.X - origin.X) / (double)size.X * 2 - 1;
        simulation.Pointer.Vertical = (pointer.Y - origin.Y) / (double)size.Y * 2 - 1;
    }
}
