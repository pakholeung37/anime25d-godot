using System;
using static Anime25D.Core.Parameter;
using static System.Math;

namespace Anime25D.Core;

/// <summary>Normalized input supplied by the host. The core has no desktop/window dependency.</summary>
public sealed class PointerInput
{
    public bool IsAvailable { get; set; }
    public double Horizontal { get; set; }
    public double Vertical { get; set; }
}
