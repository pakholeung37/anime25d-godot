using System;
using System.Collections.Generic;
using System.Linq;
using Anime25D.Core;

namespace Anime25D.Sample.Core;

/// <summary>Compiles semantic sample data into generic meshes, layer IDs and explicit masks.</summary>
public static class SampleModelBuilder
{
    public static ModelDefinition Build(RigDefinition source, RigProfile? profile = null, AnimationModel? animations = null, Func<double>? random = null)
    {
        var rig = source with { Layers = source.Layers.Select(l => l with { Strands = l.Strands?.ToArray() }).ToArray(), Warnings = source.Warnings.ToArray() };
        var config = (profile ?? new RigProfile()).CreateSnapshot();
        var catalog = animations ?? SampleAnimations.Create(config);
        foreach (var spec in config.CreateCatalog()) catalog.Parameters.IndexOf(spec.Key.ToString());
        foreach (var id in new[] { SampleAnimations.Breath, SampleAnimations.BreathHead, SampleAnimations.IrisX, SampleAnimations.IrisY, SampleAnimations.EyeVariant }) catalog.Parameters.IndexOf(id);
        var masks = new List<MaskDefinition>();
        foreach (var side in new[] { PartSide.Left, PartSide.Right })
            if (rig.Layers.Any(l => l.Role == PartRole.Iris && l.Side == side))
                masks.Add(new(side.ToString(), rig.Layers.Select((l, i) => (l, i)).Where(p => p.l.Role == PartRole.EyeWhite && p.l.Side == side).Select(p => "layer-" + p.i)));
        var geometries = rig.Layers.Select(part => new RigMeshGeometry(part, rig, config)).ToArray();
        var layers = rig.Layers.Select((part, i) => {
            var geometry = geometries[i];
            return new LayerDefinition("layer-" + i, new MeshDefinition(geometry.RestPositions, geometry.TextureCoordinates, geometry.TriangleIndices),
                part.TextureIndex, part.InitialDrawOrder, 1, part.Role == PartRole.Iris && part.Side != PartSide.None ? part.Side.ToString() : null);
        });
        return new(catalog, rig.Canvas.Width, rig.Canvas.Height, layers, masks,
            _ => new SampleBehavior(rig, random ?? new Random().NextDouble, config, geometries));
    }
}
