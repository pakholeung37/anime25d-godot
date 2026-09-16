using System.Numerics;
namespace Anime25D.Runtime;
public enum LayerProperty { TranslationX, TranslationY, Rotation, ScaleX, ScaleY, Opacity }
public sealed record LayerParameterBinding(string Parameter, string Layer, LayerProperty Property, double Scale = 1, double Offset = 0, Vector2 Pivot = default);

