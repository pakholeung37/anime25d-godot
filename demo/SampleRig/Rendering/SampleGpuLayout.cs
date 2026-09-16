using System.Linq;
using Anime25D;
using Anime25D.Runtime;
using Anime25D.Sample.Core;
namespace Anime25D.Sample.Rendering;
internal static class SampleGpuLayout
{
    public static FrameTextureBinding Create(ModelDefinition model)
    {
        int offset = SampleParameters.Specs.Count;
        var bindings = SampleParameters.Specs.Select((p, i) => new FrameValueBinding(i, p.Key.ToString())).ToList();
        bindings.AddRange(new[] {
            new FrameValueBinding(offset + (int)PoseChannel.Breath, SampleAnimations.Breath),
            new FrameValueBinding(offset + (int)PoseChannel.BreathHead, SampleAnimations.BreathHead),
            new FrameValueBinding(offset + (int)PoseChannel.IrisBounceX, SampleAnimations.IrisX),
            new FrameValueBinding(offset + (int)PoseChannel.IrisBounceY, SampleAnimations.IrisY),
            new FrameValueBinding(offset + (int)PoseChannel.ChestDisplacement, "spring-displacements", true, model.Plan.Channels.Single(c => c.Id == "spring-displacements").Length - 1),
            new FrameValueBinding(offset + (int)PoseChannel.PhysicsEnabled, "physics-enabled", true)
        });
        return new(model, 16, bindings);
    }
}
