namespace AepSharp.Tests;

/// <summary>The public evaluator over caller-built keyframes.</summary>
public class KeyframeEvaluateTests
{
    private static readonly AepKeyframeEase EaseZeroSixtyFive = new(0, 65);

    private static AepKeyframe[] Fade() =>
    [
        AepKeyframe.Create(1.0, [0.0], KeyframeInterpolation.Bezier, EaseZeroSixtyFive, EaseZeroSixtyFive),
        AepKeyframe.Create(1.6, [1.0], KeyframeInterpolation.Bezier, EaseZeroSixtyFive, EaseZeroSixtyFive),
    ];

    [Fact]
    public void EaseZeroSixtyFiveIsSymmetricAndMonotonic()
    {
        var keys = Fade();
        Assert.Equal([0.0], Keyframes.Evaluate(1.0, keys));
        Assert.Equal([1.0], Keyframes.Evaluate(1.6, keys));
        Assert.Equal(0.5, Keyframes.Evaluate(1.3, keys)![0], 6);
        var previous = -1.0;
        for (var t = 1.0; t <= 1.6 + 1e-9; t += 0.02)
        {
            var value = Keyframes.Evaluate(t, keys)![0];
            Assert.True(value >= previous, $"not monotonic at {t}: {value} < {previous}");
            previous = value;
        }
        // Speed 0 at both keys: the curve leaves 0 slowly and arrives at 1 slowly.
        Assert.True(Keyframes.Evaluate(1.06, keys)![0] < 0.1);
        Assert.True(Keyframes.Evaluate(1.54, keys)![0] > 0.9);
    }

    [Fact]
    public void OutsideTheKeysHoldsTheNearestValue()
    {
        var keys = Fade();
        Assert.Equal([0.0], Keyframes.Evaluate(0.0, keys));
        Assert.Equal([1.0], Keyframes.Evaluate(5.0, keys));
        Assert.Null(Keyframes.Evaluate(1.0, []));
    }

    [Fact]
    public void LinearKeysInterpolateLinearly()
    {
        var keys = new[]
        {
            AepKeyframe.Create(2.1, [0.0, 1.0], KeyframeInterpolation.Linear),
            AepKeyframe.Create(4.1, [1.0, 1.0], KeyframeInterpolation.Linear),
        };
        Assert.Equal([0.25, 1.0], Keyframes.Evaluate(2.6, keys)!.Select(v => Math.Round(v, 9)));
    }

    [Fact]
    public void CreateStampsMicrosecondTime()
    {
        var key = AepKeyframe.Create(1.234567, [3.0]);
        Assert.Equal(1.234567, key.Time, 6);
        Assert.Equal(KeyframeInterpolation.Bezier, key.InInterpolation);
        Assert.Equal(100.0 / 6, key.OutEase[0].Influence, 9);
    }
}
