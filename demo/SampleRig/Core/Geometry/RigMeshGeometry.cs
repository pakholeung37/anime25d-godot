using System;
using System.Linq;

namespace Anime25D.Sample.Core;

/// <summary>Rest vertices, topology and baked strand weights. Constructed only when a model is loaded.</summary>
public sealed class RigMeshGeometry
{
    private readonly float[] restPositions;
    public ReadOnlySpan<float> RestPositions => restPositions;
    private readonly float[] textureCoordinates;
    public ReadOnlySpan<float> TextureCoordinates => textureCoordinates;
    private readonly int[] triangleIndices;
    public ReadOnlySpan<int> TriangleIndices => triangleIndices;
    private readonly float[] strandWeights;
    public ReadOnlySpan<float> StrandWeights => strandWeights;
    private readonly float[] strandPositions;
    public ReadOnlySpan<float> StrandPositions => strandPositions;
    private readonly float[] fringeWeights;
    public ReadOnlySpan<float> FringeWeights => fringeWeights;
    public int VertexCount => RestPositions.Length / 2;

    public RigMeshGeometry(PartDefinition part, RigDefinition rig, RigProfile profile)
    {
        var settings = profile.Mesh;
        double cell = (part.PhysicsMesh ? settings.PhysicsCellPixels : settings.BaseCellPixels) * Math.Max(settings.MinimumScale, rig.Canvas.Width / settings.ReferenceCanvasWidth);
        var (columns, rows) = RigMath.MeshSize(part.Width, part.Height, cell);
        int vertexCount = (columns + 1) * (rows + 1);
        var mesh = Anime25D.Runtime.GridMeshBuilder.Create(part.X, part.Y, part.Width, part.Height, columns, rows);
        restPositions = mesh.RestPositions.ToArray();
        textureCoordinates = mesh.UV.ToArray();
        triangleIndices = mesh.Triangles.ToArray();
        var strands = part.Strands ?? [];
        int strandCount = strands.Length;
        strandWeights = new float[vertexCount * strandCount];
        strandPositions = strandCount > 0 ? new float[vertexCount] : [];
        fringeWeights = strandCount > 0 && part.Role == PartRole.Fringe ? new float[vertexCount * 3] : [];
        if (strandCount == 0)
            return;
        double spacing = settings.SingleStrandSpacingPixels;
        if (strandCount > 1)
        {
            var distances = Enumerable.Range(1, strandCount - 1).Select(i => strands[i].X - strands[i - 1].X).Order().ToArray();
            spacing = distances[distances.Length >> 1];
        }
        double influenceRadius = Math.Max(1, spacing * settings.StrandInfluenceWidth);
        for (int v = 0; v < vertexCount; v++)
        {
            double x = restPositions[v * 2], y = restPositions[v * 2 + 1], total = 0;
            for (int s = 0; s < strandCount; s++)
            {
                double w = Math.Exp(-Math.Pow((x - strands[s].X) / influenceRadius, 2));
                strandWeights[v * strandCount + s] = (float)w;
                total += w;
            }
            double rootY = 0, tipY = 0;
            if (total > 1e-6)
            {
                for (int s = 0; s < strandCount; s++)
                {
                    int index = v * strandCount + s;
                    strandWeights[index] = (float)(strandWeights[index] / total);
                    rootY += strandWeights[index] * strands[s].RootY;
                    tipY += strandWeights[index] * strands[s].TipY;
                }
            }
            else
            {
                strandWeights[v * strandCount] = 1;
                rootY = strands[0].RootY;
                tipY = strands[0].TipY;
            }
            strandPositions[v] = (float)Math.Clamp((y - rootY) / Math.Max(1, tipY - rootY), 0, 1);
            if (fringeWeights.Length == 0)
                continue;
            var face = rig.Anchors.Face;
            double faceWidth = face.MaximumX - face.MinimumX;
            double leftBoundary = RigMath.Smooth((x - (face.CenterX - faceWidth * settings.FringeBoundaryFaceRatio)) / settings.FringeTransitionPixels + 0.5);
            double rightBoundary = RigMath.Smooth((x - (face.CenterX + faceWidth * settings.FringeBoundaryFaceRatio)) / settings.FringeTransitionPixels + 0.5);
            fringeWeights[v * 3] = (float)(1 - leftBoundary);
            fringeWeights[v * 3 + 1] = (float)(leftBoundary * (1 - rightBoundary));
            fringeWeights[v * 3 + 2] = (float)rightBoundary;
        }
    }
}
