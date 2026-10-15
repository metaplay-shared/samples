using Metaplay.Core.Math;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The part editor's add/remove space tool: everything at or past X moves with the space, a stretch across X
    /// is lengthened or shortened rather than split, and a removal that would cut something, swallow it or
    /// break a rule is refused with what is in the way.
    /// </summary>
    [TestFixture]
    public class CoursePartSpaceTests
    {
        /// <summary>
        /// Ground 0–20, a gap, 25–60 up a step at 40 (0.5 m) and back down at 50; spikes 30–32 and 52–54, a
        /// bridge 10–16, and lanterns at 5 and 45.
        /// </summary>
        static CoursePartInfo Sample()
            => new CoursePartInfo(
                id:            CoursePartId.FromString("space-sample"),
                ground:        new List<GroundSegment> { Ground(0, 20, 0), Ground(25, 40, 0), Ground(40, 50, 5), Ground(50, 60, 0) },
                hazards:       new List<HazardSpan> { Span(30, 32), Span(52, 54) },
                positions:     new List<CoursePartPosition> { CoursePartPosition.First, CoursePartPosition.Middle, CoursePartPosition.Last },
                isSetPiece: false,
                bridges:       new List<CoursePartBridge> { new CoursePartBridge(F32.FromInt(10), F32.FromInt(16)) },
                decorations:   new List<CoursePartDecoration>
                {
                    new CoursePartDecoration("stone-lantern", F32.FromInt(5), CourseDecorationLayer.Behind),
                    new CoursePartDecoration("stone-lantern", F32.FromInt(45), CourseDecorationLayer.Front, flip: true),
                });

        [Test]
        public void TheSample_IsSound()
        {
            Assert.That(Sample().Validate(), Is.Empty);
        }

        /// <summary>
        /// A part that already breaks a rule past the edit can still be edited: the problem moves with the space,
        /// so its message names new coordinates, but it is the same problem and not a new one.
        /// </summary>
        [Test]
        public void AddingSpaceBeforeAProblemThePartAlreadyHas_IsAccepted()
        {
            CoursePartInfo sample = Sample();
            CoursePartInfo broken = new CoursePartInfo(
                sample.Id,
                sample.Ground,
                new List<HazardSpan> { Span(30, 32), Span(38, 42), Span(52, 54) },
                sample.Positions,
                sample.IsSetPiece,
                sample.Bridges,
                sample.Decorations);
            Assert.That(broken.Validate(), Is.EqualTo(new[] { "The spike bed from 38.0 m to 42.0 m does not stand on one stretch of ground." }),
                "the bed across the step has to be the part's one problem for this to say anything");

            Assert.That(CoursePartSpace.TryAdd(broken, F32.FromInt(22), F32.FromInt(4), out CoursePartInfo result, out string refusal), Is.True, refusal);
            Assert.That(result.Validate(), Is.EqualTo(new[] { "The spike bed from 42.0 m to 46.0 m does not stand on one stretch of ground." }));

            Assert.That(CoursePartSpace.TryAdd(broken, F32.FromInt(31), F32.FromInt(2), out CoursePartInfo _, out string _), Is.True,
                "lengthening a sound bed is not refused for the broken one past it");
        }

        [Test]
        public void AddingSpaceInsideAStretch_LengthensIt_AndMovesEverythingPastIt()
        {
            Assert.That(CoursePartSpace.TryAdd(Sample(), F32.FromInt(35), F32.FromInt(4), out CoursePartInfo result, out string refusal), Is.True, refusal);

            AssertGround(result, (0, 20, 0), (25, 44, 0), (44, 54, 5), (54, 64, 0));
            AssertSpans(result.Hazards, (30, 32), (56, 58));
            Assert.That(result.Bridges[0].StartX, Is.EqualTo(F32.FromInt(10)));
            AssertDecorationsAt(result, 5, 49);
            Assert.That(result.Decorations[1].Layer, Is.EqualTo(CourseDecorationLayer.Front), "the layer comes with it");
            Assert.That(result.Decorations[1].Flip, Is.True, "the flip comes with it");
            Assert.That(result.Length, Is.EqualTo(F32.FromInt(64)), "the part is longer by the space");
        }

        [Test]
        public void AddingSpaceAcrossASpikeBed_LengthensTheBed()
        {
            Assert.That(CoursePartSpace.TryAdd(Sample(), F32.FromInt(31), F32.FromInt(2), out CoursePartInfo result, out string refusal), Is.True, refusal);
            AssertSpans(result.Hazards, (30, 34), (54, 56));
        }

        [Test]
        public void AddingSpaceWhereAStretchStarts_WidensTheGapBeforeIt()
        {
            Assert.That(CoursePartSpace.TryAdd(Sample(), F32.FromInt(25), F32.FromInt(3), out CoursePartInfo result, out string refusal), Is.True, refusal);
            AssertGround(result, (0, 20, 0), (28, 43, 0), (43, 53, 5), (53, 63, 0));
        }

        [Test]
        public void AddingSpaceAtTheEnd_LengthensTheLastStretch()
        {
            Assert.That(CoursePartSpace.TryAdd(Sample(), F32.FromInt(60), F32.FromInt(6), out CoursePartInfo result, out string refusal), Is.True, refusal);
            AssertGround(result, (0, 20, 0), (25, 40, 0), (40, 50, 5), (50, 66, 0));
        }

        [Test]
        public void AddingSpaceAtTheStart_IsRefused_BecauseAPartStartsAtZero()
        {
            Assert.That(CoursePartSpace.TryAdd(Sample(), F32.Zero, F32.FromInt(3), out CoursePartInfo result, out string refusal), Is.False);
            Assert.That(result, Is.Null);
            Assert.That(refusal, Does.Contain("Part-local X runs from 0"));
        }

        [Test]
        public void RemovingSpaceInsideAStretch_ShortensIt_AndMovesEverythingPastItBack()
        {
            Assert.That(CoursePartSpace.TryRemove(Sample(), F32.FromInt(34), F32.FromInt(3), out CoursePartInfo result, out string refusal), Is.True, refusal);

            AssertGround(result, (0, 20, 0), (25, 37, 0), (37, 47, 5), (47, 57, 0));
            AssertSpans(result.Hazards, (30, 32), (49, 51));
            AssertDecorationsAt(result, 5, 42);
            Assert.That(result.Length, Is.EqualTo(F32.FromInt(57)));
        }

        [Test]
        public void RemovingPartOfAGap_NarrowsIt()
        {
            Assert.That(CoursePartSpace.TryRemove(Sample(), F32.FromInt(18), F32.FromInt(4), out CoursePartInfo result, out string refusal), Is.True, refusal);
            // The first stretch ends at 18 now and the second starts at 25 - 4 = 21: a 3 m gap where there was 5.
            AssertGround(result, (0, 18, 0), (21, 36, 0), (36, 46, 5), (46, 56, 0));
        }

        [TestCase(29, 2, "would cut the spike bed from 30.0 m to 32.0 m")]
        [TestCase(12, 2, "would cut the bridge from 10.0 m to 16.0 m")]
        [TestCase(44, 2, "would take the stone-lantern at 45.0 m")]
        [TestCase(19, 7, "would close the whole gap from 20.0 m to 25.0 m")]
        [TestCase(58, 4, "is not all on the part")]
        public void ARemovalInTheWay_IsRefusedAndSaysWhy(int x, int amount, string expected)
        {
            CoursePartInfo sample = Sample();
            Assert.That(CoursePartSpace.TryRemove(sample, F32.FromInt(x), F32.FromInt(amount), out CoursePartInfo result, out string refusal), Is.False);
            Assert.That(result, Is.Null);
            Assert.That(refusal, Does.Contain(expected));
            AssertGround(sample, (0, 20, 0), (25, 40, 0), (40, 50, 5), (50, 60, 0));
        }

        [Test]
        public void ARemovalThatWouldSwallowAWholeStretch_IsRefused()
        {
            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("space-stretch"),
                ground:        new List<GroundSegment> { Ground(0, 20, 0), Ground(25, 30, 0), Ground(35, 60, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition> { CoursePartPosition.First },
                isSetPiece: false);
            Assert.That(CoursePartSpace.TryRemove(part, F32.FromInt(24), F32.FromInt(7), out CoursePartInfo result, out string refusal), Is.False);
            Assert.That(result, Is.Null);
            Assert.That(refusal, Does.Contain("would take the whole stretch of ground from 25.0 m to 30.0 m"));
        }

        [Test]
        public void ARemovalThatWouldBreakARule_IsRefused()
        {
            // Taking 55–58 m out pulls the part's end back to 57 m, which puts the spike bed ending at 54 m inside
            // the CoursePartInfo.ExitClearance the part hands over from.
            Assert.That(CoursePartSpace.TryRemove(Sample(), F32.FromInt(55), F32.FromInt(3), out CoursePartInfo result, out string refusal), Is.False);
            Assert.That(result, Is.Null);
            Assert.That(refusal, Does.StartWith("That would break a rule:"));
            Assert.That(refusal, Does.Contain("hands over from"));
        }

        [Test]
        public void ANonPositiveAmount_IsRefused()
        {
            Assert.That(CoursePartSpace.TryAdd(Sample(), F32.FromInt(10), F32.Zero, out CoursePartInfo _, out string addRefusal), Is.False);
            Assert.That(addRefusal, Does.Contain("more than 0 m"));
            Assert.That(CoursePartSpace.TryRemove(Sample(), F32.FromInt(10), F32.FromInt(-1), out CoursePartInfo _, out string removeRefusal), Is.False);
            Assert.That(removeRefusal, Does.Contain("more than 0 m"));
        }

        [Test]
        public void AddingThenRemovingTheSameSpace_GivesThePartBack()
        {
            CoursePartInfo sample = Sample();
            Assert.That(CoursePartSpace.TryAdd(sample, F32.FromInt(35), F32.FromInt(4), out CoursePartInfo grown, out string addRefusal), Is.True, addRefusal);
            Assert.That(CoursePartSpace.TryRemove(grown, F32.FromInt(35), F32.FromInt(4), out CoursePartInfo back, out string removeRefusal), Is.True, removeRefusal);
            Assert.That(CoursePartJson.Write(back), Is.EqualTo(CoursePartJson.Write(sample)));
        }

        /// <summary>A pit floor keeps its flag when the space tool moves or stretches it, and so do the stretches around it.</summary>
        [Test]
        public void MovingOrStretchingAPitFloor_KeepsItsFlag()
        {
            CoursePartInfo pitted = new CoursePartInfo(
                id:            CoursePartId.FromString("space-pit"),
                ground:        new List<GroundSegment> { Ground(0, 20, 0), new GroundSegment(F32.FromInt(20), F32.FromInt(26), F32.FromInt(-2), isPit: true), Ground(26, 40, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition> { CoursePartPosition.Middle },
                isSetPiece: false,
                bridges:       new List<CoursePartBridge>());

            Assert.That(pitted.Validate(), Is.Empty);

            Assert.That(CoursePartSpace.TryAdd(pitted, F32.FromInt(10), F32.FromInt(3), out CoursePartInfo moved, out string moveRefusal), Is.True, moveRefusal);
            Assert.That(CoursePartSpace.TryAdd(pitted, F32.FromInt(22), F32.FromInt(3), out CoursePartInfo stretched, out string stretchRefusal), Is.True, stretchRefusal);
            Assert.That(CoursePartSpace.TryRemove(pitted, F32.FromInt(21), F32.FromInt(2), out CoursePartInfo narrowed, out string narrowRefusal), Is.True, narrowRefusal);

            Assert.Multiple(() =>
            {
                foreach ((CoursePartInfo part, int start, int end) in new[] { (moved, 23, 29), (stretched, 20, 29), (narrowed, 20, 24) })
                {
                    Assert.That(part.Ground[1].IsPit, Is.True, "the pit floor lost its flag");
                    Assert.That(part.Ground[1].StartX, Is.EqualTo(F32.FromInt(start)));
                    Assert.That(part.Ground[1].EndX, Is.EqualTo(F32.FromInt(end)));
                    Assert.That(part.Ground[0].IsPit, Is.False);
                    Assert.That(part.Ground[2].IsPit, Is.False);
                }
            });
        }

        #region Helpers

        static void AssertGround(CoursePartInfo part, params (int startX, int endX, int topDecimetres)[] expected)
        {
            Assert.That(part.Ground, Has.Count.EqualTo(expected.Length));
            for (int ndx = 0; ndx < expected.Length; ndx++)
            {
                Assert.That(part.Ground[ndx].StartX, Is.EqualTo(F32.FromInt(expected[ndx].startX)), $"ground[{ndx}].startX");
                Assert.That(part.Ground[ndx].EndX,   Is.EqualTo(F32.FromInt(expected[ndx].endX)),   $"ground[{ndx}].endX");
                Assert.That(part.Ground[ndx].TopY,   Is.EqualTo(F32.Ratio10(expected[ndx].topDecimetres)), $"ground[{ndx}].topY");
            }
        }

        static void AssertSpans(List<HazardSpan> spans, params (int startX, int endX)[] expected)
        {
            Assert.That(spans, Has.Count.EqualTo(expected.Length));
            for (int ndx = 0; ndx < expected.Length; ndx++)
            {
                Assert.That(spans[ndx].StartX, Is.EqualTo(F32.FromInt(expected[ndx].startX)), $"[{ndx}].startX");
                Assert.That(spans[ndx].EndX,   Is.EqualTo(F32.FromInt(expected[ndx].endX)),   $"[{ndx}].endX");
            }
        }

        static void AssertDecorationsAt(CoursePartInfo part, params int[] xs)
        {
            Assert.That(part.Decorations, Has.Count.EqualTo(xs.Length));
            for (int ndx = 0; ndx < xs.Length; ndx++)
                Assert.That(part.Decorations[ndx].X, Is.EqualTo(F32.FromInt(xs[ndx])), $"decorations[{ndx}].x");
        }

        static GroundSegment Ground(int startX, int endX, int topDecimetres)
            => new GroundSegment(F32.FromInt(startX), F32.FromInt(endX), F32.Ratio10(topDecimetres));

        static HazardSpan Span(int startX, int endX)
            => new HazardSpan(F32.FromInt(startX), F32.FromInt(endX));

        #endregion // Helpers
    }
}
