using System;
using System.Collections.Generic;
using System.Linq;
using Anime25D.Runtime;
using Godot;
namespace Anime25D;
public sealed record FrameValueBinding(int Slot, string Id, bool Channel = false, int Element = 0);
/// <summary>Compiles a numeric frame layout once; uploads only published frame data.</summary>
public sealed class FrameTextureBinding : IDisposable
{
    private readonly FloatTexture storage;
    private readonly float[] values;
    private readonly (FrameValueBinding Binding, int Parameter)[] bindings;
    public ImageTexture Texture => storage.Texture;
    public FrameTextureBinding(ModelDefinition model, int width, IEnumerable<FrameValueBinding> bindings)
    {
        var copy = bindings.ToArray(); var slots = new HashSet<int>();
        foreach (var b in copy)
        {
            if (b.Slot < 0 || b.Slot >= width * 4 || !slots.Add(b.Slot)) throw new ArgumentException("Invalid frame texture slot.");
            if (b.Channel && !model.Plan.Channels.Any(c => c.Id == b.Id && b.Element >= 0 && b.Element < c.Length)) throw new ArgumentException("Unknown frame channel element.");
        }
        this.bindings = copy.Select(b => (b, b.Channel ? -1 : model.Animation.Parameters.IndexOf(b.Id))).ToArray();
        values = new float[checked(width * 4)]; storage = new(width);
    }
    public void Upload(ModelFrame frame)
    {
        foreach (var (b, parameter) in bindings) values[b.Slot] = (float)(b.Channel ? frame.Channels[b.Id][b.Element] : frame.Pose[parameter]);
        storage.Upload(values);
    }
    public void Dispose() => storage.Dispose();
}
