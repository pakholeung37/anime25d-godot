using System;
using System.Linq;
using System.Numerics;

namespace Anime25D.Core;

public readonly record struct Bounds2D(float X, float Y, float Width, float Height)
{
    public bool IsValid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Width) && float.IsFinite(Height) && Width > 0 && Height > 0;
}

/// <summary>Immutable model-space rest geometry. Read-only spans never expose the backing arrays.</summary>
public sealed class MeshDefinition
{
    private readonly float[] positions, uv;
    private readonly int[] triangles;
    public ReadOnlySpan<float> RestPositions => positions;
    public ReadOnlySpan<float> UV => uv;
    public ReadOnlySpan<int> Triangles => triangles;
    public int VertexCount => positions.Length / 2;
    public MeshDefinition(ReadOnlySpan<float> restPositions, ReadOnlySpan<float> textureCoordinates, ReadOnlySpan<int> indices)
    {
        positions = restPositions.ToArray(); uv = textureCoordinates.ToArray(); triangles = indices.ToArray();
        if (positions.Length < 6 || positions.Length % 2 != 0 || uv.Length != positions.Length || triangles.Length == 0 || triangles.Length % 3 != 0 ||
            positions.Any(v => !float.IsFinite(v)) || uv.Any(v => !float.IsFinite(v)) || triangles.Any(i => i < 0 || i >= VertexCount))
            throw new ArgumentException("Invalid mesh vertices, UVs or triangle indices.");
    }
}

public static class GridMeshBuilder
{
    public static MeshDefinition Create(float x, float y, float width, float height, int columns = 2, int rows = 2)
    {
        if (!new Bounds2D(x, y, width, height).IsValid || columns < 1 || rows < 1 || (long)(columns + 1) * (rows + 1) > 65000)
            throw new ArgumentException("Invalid grid dimensions.");
        var xy = new float[(columns + 1) * (rows + 1) * 2]; var uv = new float[xy.Length];
        int k = 0;
        for (int j = 0; j <= rows; j++) for (int i = 0; i <= columns; i++)
        {
            xy[k] = (float)(x + width * (double)i / columns); xy[k + 1] = (float)(y + height * (double)j / rows);
            uv[k] = (float)((double)i / columns); uv[k + 1] = (float)((double)j / rows); k += 2;
        }
        var indices = new int[columns * rows * 6]; k = 0;
        for (int j = 0; j < rows; j++) for (int i = 0; i < columns; i++)
        {
            int a = j * (columns + 1) + i, b = a + 1, c = a + columns + 1, d = c + 1;
            indices[k++] = a; indices[k++] = b; indices[k++] = c;
            indices[k++] = b; indices[k++] = d; indices[k++] = c;
        }
        return new(xy, uv, indices);
    }
}
