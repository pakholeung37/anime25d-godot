namespace Anime25D.Sample.Rendering;

// Additional channels follow Parameter in the shared GPU pose texture.
// tools/shader-bindings.cjs generates matching shader indices.
internal enum PoseChannel
{
    Breath, BreathHead, IrisBounceX, IrisBounceY, ChestDisplacement, PhysicsEnabled
}
