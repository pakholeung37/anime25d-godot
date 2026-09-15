using System.Linq;

namespace Anime25D.Core;

/// <summary>Mutable instance state, separate from authored metadata and generated rest geometry.</summary>
public sealed class PartState
{
    public PartDefinition Definition { get; }
    public RigMeshGeometry Geometry { get; }
    public float[] Positions { get; }
    public StrandState[] Springs { get; }
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; }
    public double Depth { get; set; }
    public int DrawOrder { get; set; }
    public double Alpha
    {
        get; internal set;
    }

    public PartState(PartDefinition definition, RigDefinition rig, RigProfile profile)
    {
        Definition = definition;
        Depth = definition.Depth;
        Opacity = definition.Opacity;
        DrawOrder = definition.InitialDrawOrder;
        Geometry = new(definition, rig, profile);
        Positions = Geometry.RestPositions.ToArray();
        Springs = Enumerable.Range(0, definition.Strands?.Length ?? 0)
            .Select(index => new StrandState(index * profile.Physics.StrandPhaseSpacing + definition.InitialDrawOrder)).ToArray();
    }
}
