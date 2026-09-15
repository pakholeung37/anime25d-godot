using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Anime25D.Core;

public static class RigMath
{
    public static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    public static (int Nx, int Ny) MeshSize(int w, int h, double cell)
    {
        int nx = Math.Max(2, (int)Math.Floor(w / cell + 0.5)), ny = Math.Max(2, (int)Math.Floor(h / cell + 0.5));
        while ((long)(nx + 1) * (ny + 1) > 65000)
        {
            if (nx >= ny) nx = Math.Max(2, (int)Math.Floor(nx * 0.9));
            else ny = Math.Max(2, (int)Math.Floor(ny * 0.9));
        }
        return (nx, ny);
    }
}

public sealed class Spring
{
    public double X, V, Displacement;
    public void Step(double target, double stiffness, double damping, double dt)
    {
        int count = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 120))); double h = dt / count;
        for (int i = 0; i < count; i++) { V += (-stiffness * (X - target) - damping * V) * h; X += V * h; }
    }
}
public sealed class StrandState(double phase)
{
    public Spring Stiff { get; } = new();
    public Spring Soft { get; } = new();
    public double Phase { get; } = phase;
}

public sealed class PartState
{
    public PartDefinition Definition { get; }
    public string BaseName { get; }
    public string Id => $"{Definition.Z}:{Definition.Name}";
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; }
    public double Depth { get; set; }
    public int DrawOrder { get; set; }
    public float[] Base { get; }
    public float[] Positions { get; }
    public float[] Uv { get; }
    public int[] Indices { get; }
    public float[] StrandWeights { get; }
    public float[] StrandU { get; }
    public float[] BangWeights { get; }
    public StrandState[] Springs { get; }
    public double Alpha { get; internal set; }
    public int VertexCount => Base.Length / 2;

    public PartState(PartDefinition part, RigDefinition rig)
    {
        Definition = part; Depth = part.Depth; Opacity = part.Opacity; DrawOrder = part.Z;
        var name = Regex.Replace(part.Name, "_(l|r)$", "");
        BaseName = name == "eye_close2" ? name : Regex.Replace(name, "_\\d+$", "");
        double cell = (part.Phys is not null ? 30 : 42) * Math.Max(0.6, rig.Canvas.W / 768.0);
        var (nx, ny) = RigMath.MeshSize(part.W, part.H, cell);
        int nv = (nx + 1) * (ny + 1);
        Base = new float[nv * 2]; Positions = new float[nv * 2]; Uv = new float[nv * 2];
        int k = 0;
        for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
        {
            Base[k] = (float)(part.X + part.W * (double)i / nx); Base[k + 1] = (float)(part.Y + part.H * (double)j / ny);
            Uv[k] = (float)((double)i / nx); Uv[k + 1] = (float)((double)j / ny); k += 2;
        }
        Base.CopyTo(Positions, 0); Indices = new int[nx * ny * 6]; k = 0;
        for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
        {
            int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
            Indices[k++] = a; Indices[k++] = b; Indices[k++] = c; Indices[k++] = b; Indices[k++] = d; Indices[k++] = c;
        }
        var strands = part.Strands ?? []; int ns = strands.Length;
        StrandWeights = new float[nv * ns]; StrandU = ns > 0 ? new float[nv] : [];
        Springs = Enumerable.Range(0, ns).Select(i => new StrandState(i * 1.37 + part.Z)).ToArray();
        BangWeights = ns > 0 && BaseName == "front hair" ? new float[nv * 3] : [];
        if (ns == 0) return;
        double spacing = 120;
        if (ns > 1)
        {
            var distances = Enumerable.Range(1, ns - 1).Select(i => strands[i].X - strands[i - 1].X).Order().ToArray();
            spacing = distances[distances.Length >> 1];
        }
        double sigma = Math.Max(1, spacing * 0.6);
        for (int v = 0; v < nv; v++)
        {
            double x = Base[v * 2], y = Base[v * 2 + 1], total = 0;
            for (int s = 0; s < ns; s++) { double w = Math.Exp(-Math.Pow((x - strands[s].X) / sigma, 2)); StrandWeights[v * ns + s] = (float)w; total += w; }
            double rootY = 0, tipY = 0;
            if (total > 1e-6)
            {
                for (int s = 0; s < ns; s++)
                {
                    int index = v * ns + s; StrandWeights[index] = (float)(StrandWeights[index] / total);
                    rootY += StrandWeights[index] * strands[s].RootY; tipY += StrandWeights[index] * strands[s].TipY;
                }
            }
            else { StrandWeights[v * ns] = 1; rootY = strands[0].RootY; tipY = strands[0].TipY; }
            StrandU[v] = (float)Math.Clamp((y - rootY) / Math.Max(1, tipY - rootY), 0, 1);
            if (BangWeights.Length == 0) continue;
            var face = rig.Anchors.Face; double fw = face.X1 - face.X0;
            double s1 = RigMath.Smooth((x - (face.Cx - fw * 0.22)) / 36 + 0.5), s2 = RigMath.Smooth((x - (face.Cx + fw * 0.22)) / 36 + 0.5);
            BangWeights[v * 3] = (float)(1 - s1); BangWeights[v * 3 + 1] = (float)(s1 * (1 - s2)); BangWeights[v * 3 + 2] = (float)s2;
        }
    }
}
