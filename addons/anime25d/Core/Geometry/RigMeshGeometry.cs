using System;
using System.Linq;

namespace Anime25D.Core;

/// <summary>Rest vertices, topology and baked strand weights. Constructed only when a model is loaded.</summary>
public sealed class RigMeshGeometry
{
    public float[] RestPositions { get; }
    public float[] TextureCoordinates { get; }
    public int[] TriangleIndices { get; }
    public float[] StrandWeights { get; }
    public float[] StrandPositions { get; }
    public float[] FringeWeights { get; }
    public int VertexCount => RestPositions.Length / 2;

    public RigMeshGeometry(PartDefinition part, RigDefinition rig, RigProfile profile)
    {
        var settings = profile.Mesh;
        double cell = (part.PhysicsMesh ? settings.PhysicsCellPixels : settings.BaseCellPixels) * Math.Max(settings.MinimumScale, rig.Canvas.Width / settings.ReferenceCanvasWidth);
        var (columns, rows) = RigMath.MeshSize(part.Width, part.Height, cell);
        int vertexCount = (columns + 1) * (rows + 1);
        RestPositions = new float[vertexCount * 2];
        TextureCoordinates = new float[vertexCount * 2];
        int k = 0;
        for (int j = 0; j <= rows; j++)
            for (int i = 0; i <= columns; i++)
            {
                RestPositions[k] = (float)(part.X + part.Width * (double)i / columns);
                RestPositions[k + 1] = (float)(part.Y + part.Height * (double)j / rows);
                TextureCoordinates[k] = (float)((double)i / columns);
                TextureCoordinates[k + 1] = (float)((double)j / rows);
                k += 2;
            }
        TriangleIndices = new int[columns * rows * 6];
        k = 0;
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                int a = j * (columns + 1) + i, b = a + 1, c = a + columns + 1, d = c + 1;
                TriangleIndices[k++] = a;
                TriangleIndices[k++] = b;
                TriangleIndices[k++] = c;
                TriangleIndices[k++] = b;
                TriangleIndices[k++] = d;
                TriangleIndices[k++] = c;
            }
        var strands = part.Strands ?? [];
        int strandCount = strands.Length;
        StrandWeights = new float[vertexCount * strandCount];
        StrandPositions = strandCount > 0 ? new float[vertexCount] : [];
        FringeWeights = strandCount > 0 && part.Role == PartRole.Fringe ? new float[vertexCount * 3] : [];
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
            double x = RestPositions[v * 2], y = RestPositions[v * 2 + 1], total = 0;
            for (int s = 0; s < strandCount; s++)
            {
                double w = Math.Exp(-Math.Pow((x - strands[s].X) / influenceRadius, 2));
                StrandWeights[v * strandCount + s] = (float)w;
                total += w;
            }
            double rootY = 0, tipY = 0;
            if (total > 1e-6)
            {
                for (int s = 0; s < strandCount; s++)
                {
                    int index = v * strandCount + s;
                    StrandWeights[index] = (float)(StrandWeights[index] / total);
                    rootY += StrandWeights[index] * strands[s].RootY;
                    tipY += StrandWeights[index] * strands[s].TipY;
                }
            }
            else
            {
                StrandWeights[v * strandCount] = 1;
                rootY = strands[0].RootY;
                tipY = strands[0].TipY;
            }
            StrandPositions[v] = (float)Math.Clamp((y - rootY) / Math.Max(1, tipY - rootY), 0, 1);
            if (FringeWeights.Length == 0)
                continue;
            var face = rig.Anchors.Face;
            double faceWidth = face.MaximumX - face.MinimumX;
            double leftBoundary = RigMath.Smooth((x - (face.CenterX - faceWidth * settings.FringeBoundaryFaceRatio)) / settings.FringeTransitionPixels + 0.5);
            double rightBoundary = RigMath.Smooth((x - (face.CenterX + faceWidth * settings.FringeBoundaryFaceRatio)) / settings.FringeTransitionPixels + 0.5);
            FringeWeights[v * 3] = (float)(1 - leftBoundary);
            FringeWeights[v * 3 + 1] = (float)(leftBoundary * (1 - rightBoundary));
            FringeWeights[v * 3 + 2] = (float)rightBoundary;
        }
    }
}
