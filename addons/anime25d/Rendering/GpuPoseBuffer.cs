using System;
using System.Runtime.InteropServices;
using Anime25D.Core;
using Godot;

namespace Anime25D.Rendering;

/// <summary>One 256-byte texture update per actor, shared by all its visible and mask materials.</summary>
internal sealed class GpuPoseBuffer : IDisposable
{
    private const int TextureWidth = 16;
    private readonly float[] values = new float[TextureWidth * 4];
    private readonly byte[] bytes = new byte[TextureWidth * 16];
    private readonly Image image;
    public ImageTexture Texture { get; }

    public GpuPoseBuffer()
    {
        image = Image.CreateFromData(TextureWidth, 1, false, Image.Format.Rgbaf, bytes);
        Texture = ImageTexture.CreateFromImage(image);
    }

    public void Update(RigSimulation simulation)
    {
        for (int index = 0; index < Parameters.Specs.Count; index++)
            values[index] = (float)simulation.Frame[(Parameter)index];
        Set(PoseChannel.Breath, simulation.Frame.Breath);
        Set(PoseChannel.BreathHead, simulation.Frame.BreathHead);
        Set(PoseChannel.IrisBounceX, simulation.Frame.IrisBounceX);
        Set(PoseChannel.IrisBounceY, simulation.Frame.IrisBounceY);
        Set(PoseChannel.ChestDisplacement, simulation.Physics.Chest.Displacement);
        Set(PoseChannel.PhysicsEnabled, simulation.AutomaticMotion.Physics ? 1 : 0);
        MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(bytes);
        image.SetData(TextureWidth, 1, false, Image.Format.Rgbaf, bytes);
        Texture.Update(image);
    }

    private void Set(PoseChannel channel, double value) => values[Parameters.Specs.Count + (int)channel] = (float)value;

    public void Dispose()
    {
        Texture.Dispose();
        image.Dispose();
    }
}
