using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Anime25D.Runtime;

public delegate void ChannelEvaluator(ComponentContext context, Span<double> output);
public sealed class ChannelWriter : ModelComponent
{
    private readonly string output;
    private readonly ChannelEvaluator evaluate;
    public ChannelWriter(string output, ChannelEvaluator evaluate) { this.output = output; this.evaluate = evaluate; }
    public ChannelWriter(string output, Func<ComponentContext, double[]> evaluate)
        : this(output, (c, destination) => { var values = evaluate(c); if (values.Length != destination.Length) throw new InvalidOperationException("Channel length mismatch."); values.CopyTo(destination); }) { }
    public override void EvaluateOutput(ComponentContext c) => evaluate(c, c.Output(output));
}
