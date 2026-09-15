using System.Linq;
using static Anime25D.Core.Parameter;
using static Anime25D.Core.RigMath;
using static System.Math;

namespace Anime25D.Core;

public static class RigDeformer
{
    public static double Fade(PartState part, RigSimulation sim)
    {
        var d = part.Definition; var e = sim.Frame;
        double open = d.Side == "L" ? e[eyeOpenL] : e[eyeOpenR];
        if (d.Fade == "eyeOpen") return Smooth((open - (0.10 + e[eyeEase] * 0.45)) / 0.15);
        if (d.Fade is "eyeClose" or "eyeClose2")
        {
            bool alternate = sim.BlinkVariant == 2 && sim.Parts.Any(p => p.Definition.Fade == "eyeClose2" && p.Definition.Side == d.Side && p.Visible && p.Opacity > 0);
            if ((d.Fade == "eyeClose2") != alternate) return 0;
            return 1 - Smooth((open - (0.10 + e[eyeEase] * 0.45)) / 0.15);
        }
        if (d.Fade == "mouthOpen") return Smooth((e[mouthOpen] - (0.05 + e[mouthEase] * 0.35)) / 0.12);
        if (d.Fade == "mouthClose") return 1 - Smooth((e[mouthOpen] - (0.05 + e[mouthEase] * 0.35)) / 0.12);
        return 1;
    }

    public static void Deform(PartState part, RigSimulation sim)
    {
        var d = part.Definition; var a = sim.Definition.Anchors; var e = sim.Frame;
        var b = part.Base; var output = part.Positions; var np = a.NeckPivot; var bp = a.BodyPivot;
        double fs = a.FaceScale, az = e[angleZ] * 0.07, cz = Cos(az), sz = Sin(az), ab = e[body] * 0.028, cb = Cos(ab), sb = Sin(ab);
        string bn = part.BaseName; bool head = d.Group == "head", frontHair = bn == "front hair";
        var eye = d.Side == "L" ? a.EyeL : d.Side == "R" ? a.EyeR : null;
        double open = d.Side == "L" ? e[eyeOpenL] : e[eyeOpenR], mo = e[mouthOpen], halfMouth = (a.Mouth.X1 - a.Mouth.X0) / 2;
        double centerX = d.X + d.W / 2.0, centerY = d.Y + d.H / 2.0;
        double chestX = np.Cx, chestY = a.NeckBottom + (a.Face.Y1 - a.Face.Y0) * 0.60;
        double chestRx = Max(1, (a.Face.X1 - a.Face.X0) * 0.60), chestRy = Max(1, (a.Face.Y1 - a.Face.Y0) * 0.45);
        int ns = part.Springs.Length;
        for (int k = 0; k < b.Length; k += 2)
        {
            double x = b[k], y = b[k + 1]; int vi = k >> 1;
            if (eye is not null && bn is "eye_close" or "eye_close2")
            {
                double scale = d.Side == "L" ? e[eyeScaleL] : e[eyeScaleR];
                if (scale != 1) { double cx = (eye.X0 + eye.X1) / 2, cy = (eye.Y0 + eye.Y1) / 2; x = cx + (x - cx) * scale; y = cy + (y - cy) * scale; }
            }
            if (bn is "mouth_open" or "mouth_close")
            {
                double scale = e[mouthScale];
                if (scale != 1) { x = a.Mouth.Cx + (x - a.Mouth.Cx) * scale; y = a.Mouth.Cy + (y - a.Mouth.Cy) * scale; }
            }
            if (d.Fade == "eyeOpen" && eye is not null)
            {
                if (bn == "irides")
                {
                    x = eye.Icx + (x - eye.Icx) * e[irisScale] * e.IrisBounceX;
                    y = eye.Icy + (y - eye.Icy) * e[irisScale] * e.IrisBounceY;
                    x += e[eyeX] * 11 * fs; y += e[eyeY] * 6 * fs;
                    y = eye.CloseY + (y - eye.CloseY) * (1 - 0.80 * Smooth((0.32 - open) / 0.32));
                }
                else y = eye.CloseY + (y - eye.CloseY) * (1 - 0.85 * (1 - open));
            }
            if (d.Fade is "eyeClose" or "eyeClose2" && eye is not null)
            {
                y -= open * 3; y += e[eyeCY] * 14 * fs;
                Rotate(ref x, ref y, centerX, centerY, e[eyeCAng] * 0.3 * (d.Side == "L" ? 1 : -1));
            }
            if (bn == "eyebrow")
            {
                y += (-e[brow] * 9 + (1 - open) * 3.5) * fs;
                double angle = (d.Side == "L" ? e[browAngL] + e[browAngSym] : e[browAngR] - e[browAngSym]) * 0.30;
                Rotate(ref x, ref y, centerX, centerY, angle);
            }
            if (d.Fade == "mouthOpen")
            {
                y = a.Mouth.Y0 + (y - a.Mouth.Y0) * (0.5 + 0.5 * mo);
                double q = Pow(Abs(x - a.Mouth.Cx) / (halfMouth + 4), 1.5);
                y -= e[mouthForm] * 6 * fs * (q - 0.35);
            }
            if (d.Fade == "mouthClose")
            {
                y += e[mouthCY] * 14 * fs;
                Rotate(ref x, ref y, a.Mouth.Cx, a.Mouth.Cy, e[mouthCAng] * 0.35);
            }
            if (bn == "face" && y > a.Mouth.Cy) y += mo * 6 * fs * Smooth((y - a.Mouth.Cy) / (a.Face.Y1 - a.Mouth.Cy));
            double hw = head ? 1 : d.Group == "body" ? 0.16 : 0;
            if (bn == "neck") hw = 0.55 * Smooth((a.NeckBottom - y) / Max(1, a.NeckBottom - a.NeckTop));
            if (hw > 0)
            {
                double rx = x - np.Cx, ry = y - np.Cy, rx2 = rx * cz - ry * sz, ry2 = rx * sz + ry * cz;
                x += (rx2 - rx) * hw; y += (ry2 - ry) * hw;
                double depth = part.Depth;
                x += hw * fs * (e[angleX] * (14 + 40 * (depth - 1)) + e[angleX] * (np.Cy - y) * 0.028);
                y += hw * fs * (-e[angleY] * (9 + 30 * (depth - 1)) - e[angleY] * (depth - 1) * (y - a.Face.Cy) * 0.05);
            }
            y -= (d.Group == "body" ? e.Breath * 2 : e.BreathHead * 1.6) * fs;
            if (bn == "topwear" && y < chestY) y -= e.Breath * 2.2 * fs * Smooth((chestY - y) / (chestRy * 2));
            if (bn == "topwear")
            {
                x = np.Cx + (x - np.Cx) * (1 + e.Breath * 0.003);
                double gx = (x - chestX) / chestRx, gy = (y - (chestY + e[bustY] * 70 * fs)) / chestRy;
                y += sim.Bounce.Displacement * e[bust] * Exp(-gx * gx - gy * gy);
            }
            if (bn == "handwear")
            {
                double w = Smooth((y - d.Y) / d.H * 1.15);
                y -= e[armY] * 30 * fs * w; y += e[armPos] * 40 * fs;
                x += e[armY] * 6 * fs * w * (x < np.Cx ? 1 : -1);
            }
            if (part.BangWeights.Length > 0)
            {
                double m = Pow(part.StrandU[vi], 1.4) * 22 * fs;
                x += (e[bangL] * part.BangWeights[vi * 3] + e[bangC] * part.BangWeights[vi * 3 + 1] + e[bangR] * part.BangWeights[vi * 3 + 2]) * m;
            }
            if (ns > 0 && sim.Auto.Physics)
            {
                double u = frontHair ? Min(1, part.StrandU[vi] * 1.6) : part.StrandU[vi];
                double amp = Pow(u, frontHair ? 1.8 : 2.1) * (frontHair ? e[fhAmp] : e[physAmp]);
                double softMix = Pow(u, 1.2) * (frontHair ? e[fhSoft] : e[soft]), dx = 0;
                for (int s = 0; s < ns; s++)
                {
                    double w = part.StrandWeights[vi * ns + s]; if (w < 0.001) continue;
                    var sp = part.Springs[s]; dx += w * (sp.Stiff.Displacement * (1 - softMix) + sp.Soft.Displacement * softMix);
                }
                x += dx * amp; y += Abs(dx) * amp * 0.12;
            }
            output[k] = (float)x; output[k + 1] = (float)y;
        }
        // The original rounds to Float32 before the second rotation pass. Preserve that boundary.
        if (Abs(ab) > 1e-4) for (int k = 0; k < output.Length; k += 2)
        {
            double rx = output[k] - bp.Cx, ry = output[k + 1] - bp.Cy;
            output[k] = (float)(bp.Cx + rx * cb - ry * sb); output[k + 1] = (float)(bp.Cy + rx * sb + ry * cb);
        }
    }
    private static void Rotate(ref double x, ref double y, double cx, double cy, double angle)
    {
        if (angle == 0) return;
        double ct = Cos(angle), st = Sin(angle), rx = x - cx, ry = y - cy;
        x = cx + rx * ct - ry * st; y = cy + rx * st + ry * ct;
    }
}
