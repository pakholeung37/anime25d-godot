using System;

namespace Anime25D.Core;

public static class RigMath
{
    public static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
    public static (int Nx, int Ny) MeshSize(int w, int h, double cell)
    {
        int nx = Math.Max(2, (int)Math.Floor(w / cell + 0.5)), ny = Math.Max(2, (int)Math.Floor(h / cell + 0.5));
        while ((long)(nx + 1) * (ny + 1) > 65000)
        {
            if (nx >= ny)
                nx = Math.Max(2, (int)Math.Floor(nx * 0.9));
            else
                ny = Math.Max(2, (int)Math.Floor(ny * 0.9));
        }
        return (nx, ny);
    }
}
