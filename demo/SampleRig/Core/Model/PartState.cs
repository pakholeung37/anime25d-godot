using System.Linq;

namespace Anime25D.Sample.Core;

/// <summary>Mutable instance state, separate from authored metadata and generated rest geometry.</summary>
public sealed class PartState
{
    public PartDefinition Definition { get; }
    public RigMeshGeometry Geometry { get; }
    private float[]? referencePositions;
    public float[] Positions => referencePositions ??= Geometry.RestPositions.ToArray();
    public StrandState[] Springs { get; }
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; }
    public double Depth { get; set; }
    public int DrawOrder { get; set; }
    public double Alpha
    {
        get; internal set;
    }

    public PartState(PartDefinition definition, RigDefinition rig, RigProfile profile, RigMeshGeometry? geometry = null)
    {
        Definition = definition;
        Depth = definition.Depth;
        Opacity = definition.Opacity;
        DrawOrder = definition.InitialDrawOrder;
        Geometry = geometry ?? new(definition, rig, profile);
        Springs = Enumerable.Range(0, definition.Strands?.Length ?? 0)
            .Select(index => new StrandState(index * profile.Physics.StrandPhaseSpacing + definition.InitialDrawOrder)).ToArray();
    }
}
