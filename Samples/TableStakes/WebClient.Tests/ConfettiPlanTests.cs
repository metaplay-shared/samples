using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="ConfettiPlan"/>, which decides every property of the confetti shown after a won game. The
/// stylesheet only animates the plan. The tests check that the piece count is fixed, that the same seed gives the
/// same plan, that every property stays within its range, and that every piece finishes falling in time.
/// </summary>
[TestFixture]
public class ConfettiPlanTests
{
    [TestCase(1UL)]
    [TestCase(ulong.MaxValue)]
    public void EveryThrowIsTheSameSize(ulong seed)
    {
        Assert.That(ConfettiPlan.For(seed: seed).Count, Is.EqualTo(ConfettiPlan.Count));
    }

    [Test]
    public void TheSameSeedThrowsTheSameConfetti()
    {
        IReadOnlyList<ConfettiPiece> first  = ConfettiPlan.For(seed: 20260907);
        IReadOnlyList<ConfettiPiece> second = ConfettiPlan.For(seed: 20260907);

        Assert.That(second, Is.EqualTo(first));

        // A different seed gives a different plan, so different matches show different confetti.
        Assert.That(ConfettiPlan.For(seed: 20260908), Is.Not.EqualTo(first));
    }

    [Test]
    public void EveryPieceLandsInsideItsRange()
    {
        foreach (ConfettiPiece piece in ConfettiPlan.For(seed: 5))
        {
            Assert.That(piece.X, Is.InRange(2.0, 98.0),            $"piece {piece.Index} x");
            Assert.That(piece.DelayMs, Is.InRange(0, 500),         $"piece {piece.Index} delay");
            Assert.That(piece.FallMs, Is.InRange(1800, 2600),      $"piece {piece.Index} fall");
            Assert.That(piece.DriftRem, Is.InRange(-4.0, 4.0),     $"piece {piece.Index} drift");
            Assert.That(piece.Spin, Is.InRange(-720, 720),         $"piece {piece.Index} spin");
            Assert.That(piece.ColorNdx, Is.InRange(0, 4),          $"piece {piece.Index} hue");
            Assert.That(piece.Scale, Is.InRange(0.7, 1.3),         $"piece {piece.Index} scale");
        }
    }

    [Test]
    public void TheWholeFallIsOverInAboutThreeSeconds()
    {
        foreach (ConfettiPiece piece in ConfettiPlan.For(seed: 5))
            Assert.That(piece.DelayMs + piece.FallMs, Is.LessThanOrEqualTo(3100),
                $"piece {piece.Index} is still in the air at {piece.DelayMs + piece.FallMs} ms");
    }

    [Test]
    public void TheThrowSpreadsAcrossTheScreenAndTheClock()
    {
        // Catches a plan that gives every piece the same x position or the same delay.
        IReadOnlyList<ConfettiPiece> pieces = ConfettiPlan.For(seed: 5);

        int distinctX     = pieces.Select(piece => piece.X).Distinct().Count();
        int distinctDelay = pieces.Select(piece => piece.DelayMs).Distinct().Count();

        Assert.That(distinctX, Is.GreaterThan(24), "the pieces share too few starting positions");
        Assert.That(distinctDelay, Is.GreaterThan(24), "the pieces start too close together");
    }
}
