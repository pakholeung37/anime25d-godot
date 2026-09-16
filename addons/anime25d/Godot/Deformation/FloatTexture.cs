using System;
using System.Runtime.InteropServices;
using Godot;
namespace Anime25D;
/// <summary>Owned RGBA float texture; reusable staging storage, no sample-specific layout.</summary>
public sealed class FloatTexture : IDisposable
{
    private readonly Image image;
    private readonly byte[] bytes;
    public ImageTexture Texture { get; }
    public int Width { get; }
    public int Height { get; }
    public FloatTexture(int width, int height = 1)
    {
        Width = width; Height = height; bytes = new byte[checked(width * height * 16)];
        image = Image.CreateFromData(width, height, false, Image.Format.Rgbaf, bytes); Texture = ImageTexture.CreateFromImage(image);
    }
    public void Upload(ReadOnlySpan<float> values)
    {
        if (values.Length * 4 != bytes.Length) throw new ArgumentException("Float texture layout mismatch.");
        MemoryMarshal.AsBytes(values).CopyTo(bytes); image.SetData(Width, Height, false, Image.Format.Rgbaf, bytes); Texture.Update(image);
    }
    public void Dispose() { Texture.Dispose(); image.Dispose(); }
}
