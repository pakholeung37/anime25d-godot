using System;
using System.Runtime.InteropServices;
using Anime25D.Sample.Core;
using Godot;

namespace Anime25D.Sample.Rendering;

/// <summary>One 256-byte texture update per actor, shared by all its visible and mask materials.</summary>
internal sealed class LegacyPoseBuffer : IDisposable
{
    private const int TextureWidth = 16;
    private readonly float[] values = new float[TextureWidth * 4];
    private readonly byte[] bytes = new byte[TextureWidth * 16];
    private readonly Image image;
    public ImageTexture Texture { get; }

    public LegacyPoseBuffer()
    {
        image = Image.CreateFromData(TextureWidth, 1, false, Image.Format.Rgbaf, bytes);
        Texture = ImageTexture.CreateFromImage(image);
    }

    public void Update(SampleBehavior simulation, Anime25D.Runtime.ReadOnlyPose pose)
    {
        for (int index = 0; index < Parameters.Specs.Count; index++)
            values[index] = (float)pose[((Parameter)index).ToString()];
        Set(PoseChannel.Breath, pose[SampleAnimations.Breath]);
        Set(PoseChannel.BreathHead, pose[SampleAnimations.BreathHead]);
        Set(PoseChannel.IrisBounceX, pose[SampleAnimations.IrisX]);
        Set(PoseChannel.IrisBounceY, pose[SampleAnimations.IrisY]);
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
