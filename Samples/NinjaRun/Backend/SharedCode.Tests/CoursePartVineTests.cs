using Metaplay.Core.Math;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Hanging vines authored in a course part: the parts file's optional <c>vines</c> array, the rules a vine
    /// keeps, the composer's offsetting of a part's vines into a course (with the tuned rope, so a vine in a
    /// part swings exactly like one built with that rope directly) the part editor's add/remove space tool
    /// moving and guarding them, and the draft carried by value on a ticket.
    /// </summary>
    [TestFixture]
    public class CoursePartVineTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        static readonly CoursePartPosition[] EveryPosition =
        {
            CoursePartPosition.First,
            CoursePartPosition.Middle,
            CoursePartPosition.Last,
        };

        static CourseDefinition VineCourse => TestCourses.Vine;

        #region The parts file

        [Test]
        public void APartWithVines_RoundTripsThroughTheWriter()
        {
            CoursePartInfo part = Flat("vine-round-trip", 80, Vine(20, 80, 24, 39, 30), Vine(50, 95, 54, 70, 35));

            string                        json   = CoursePartJson.Write(part);
            IReadOnlyList<CoursePartInfo> reread = CoursePartJson.ParseAll("[" + json + "]");

            Assert.That(reread, Has.Count.EqualTo(1));
            AssertSameVines(reread[0], part);
            Assert.That(json, Does.Contain(
                "\"vines\": [\n" +
                "    { \"anchorX\": 20, \"anchorY\": 8, \"platformStartX\": 24, \"platformEndX\": 39, \"platformTopY\": 3 },\n" +
                "    { \"anchorX\": 50, \"anchorY\": 9.5, \"platformStartX\": 54, \"platformEndX\": 70, \"platformTopY\": 3.5 }\n" +
                "  ]\n"));
        }

        [Test]
        public void Vines_AreWrittenAfterTheDecorations()
        {
            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("vine-after-decorations"),
                ground:        new List<GroundSegment> { Ground(0, 60, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition>(EveryPosition),
                isSetPiece: false,
                bridges:       new List<CoursePartBridge>(),
                decorations:   new List<CoursePartDecoration> { new CoursePartDecoration("stone-lantern", F32.FromInt(8), CourseDecorationLayer.Behind) },
                vines:         new List<CoursePartVine> { Vine(20, 80, 24, 39, 30) });

            string json = CoursePartJson.Write(part);

            int decorations = json.IndexOf("\"decorations\"", StringComparison.Ordinal);
            int vines       = json.IndexOf("\"vines\"", StringComparison.Ordinal);
            Assert.That(decorations, Is.GreaterThan(0));
            Assert.That(vines, Is.GreaterThan(decorations), "the vines are written before the decorations");
            Assert.That(json, Does.Contain("\"layer\": \"Behind\" }\n  ],\n  \"vines\": ["), "the decorations array is not followed by the vines");
            AssertSameVines(CoursePartJson.ParseAll("[" + json + "]")[0], part);
        }

        [Test]
        public void APartWithoutVines_WritesNoVinesProperty_AndReadsBackWithNone()
        {
            CoursePartInfo part = Flat("bare-part", 60);

            string json = CoursePartJson.Write(part);

            Assert.That(json, Does.Not.Contain("\"vines\""));
            Assert.That(json, Does.EndWith("  \"bridges\": []\n}"), "the bridges are no longer the last property of a part without vines or decorations");
            Assert.That(CoursePartJson.ParseAll("[" + json + "]")[0].Vines, Is.Empty);
        }

        [Test]
        public void AVineMissingAField_IsRefusedNamingIt()
        {
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(
                PartJson("\"vines\": [ { \"anchorX\": 20, \"anchorY\": 8, \"platformStartX\": 24, \"platformEndX\": 39 } ]")));

            Assert.That(refused.Message, Does.Contain("('vine-json')"));
            Assert.That(refused.Message, Does.Contain("vines[0].platformTopY is missing"));
        }

        [Test]
        public void AVineWithAnUnknownField_IsRefusedNamingIt()
        {
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(
                PartJson("\"vines\": [ { \"anchorX\": 20, \"anchorY\": 8, \"platformStartX\": 24, \"platformEndX\": 39, \"platformTopY\": 3, \"length\": 7 } ]")));

            Assert.That(refused.Message, Does.Contain("vines[0].length is not a property the format has"));
            Assert.That(refused.Message, Does.Contain("anchorX, anchorY, platformStartX, platformEndX, platformTopY"));
        }

        [Test]
        public void AVineWithANonNumber_IsRefusedNamingIt()
        {
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(
                PartJson("\"vines\": [ { \"anchorX\": \"20\", \"anchorY\": 8, \"platformStartX\": 24, \"platformEndX\": 39, \"platformTopY\": 3 } ]")));

            Assert.That(refused.Message, Does.Contain("vines[0].anchorX must be a number of metres, not a string"));
        }

        [Test]
        public void VinesThatAreNotAnArray_AreRefused()
        {
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(PartJson("\"vines\": {}")));

            Assert.That(refused.Message, Does.Contain("vines must be an array, not an object"));
        }

        [Test]
        public void AnUnknownPartProperty_NamesVinesAmongTheKnownOnes()
        {
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(PartJson("\"vine\": []")));

            Assert.That(refused.Message, Does.Contain("vine is not a property the format has"));
            Assert.That(refused.Message, Does.Contain("decorations, vines"));
        }

        #endregion

        #region The rules

        [Test]
        public void ThePartShapedLikeTheVineCourse_IsSound()
        {
            Assert.That(VineCourseShaped().Validate(), Is.Empty);
        }

        [Test]
        public void ADraftWithAnEmptyVine_DoesNotCompose()
        {
            DraftCoursePart draft = new DraftCoursePart(Flat("empty-vine", 60, new CoursePartVine[] { null }));
            Assert.That(draft.HasNoEmptyRows(), Is.False);
            Assert.That(draft.TryComposeAlone(_gameConfig.Global), Is.Null);
        }

        [Test]
        public void AnAnchorInTheEntryClearance_IsReported()
        {
            IReadOnlyList<string> problems = Flat("entry-vine", 60, Vine(3, 80, 24, 39, 30)).Validate();
            Assert.That(problems, Has.Some.EqualTo("The vine hanging at 3.0 m is inside the 4.0 m the part is entered on."));
        }

        [Test]
        public void AnAnchorInTheExitClearance_IsReported()
        {
            IReadOnlyList<string> problems = Flat("exit-vine", 60, Vine(57, 80, 58, 59, 30)).Validate();
            Assert.That(problems, Has.Some.EqualTo("The vine hanging at 57.0 m is inside the 4.0 m the part hands over from."));
        }

        [Test]
        public void ASwingOffTheStartOfThePart_IsReported()
        {
            IReadOnlyList<string> problems = Flat("swing-back", 60, Vine(5, 80, 9, 20, 30)).Validate();
            Assert.That(problems, Has.Some.StartsWith("The vine hanging at 5.0 m swings back to ").And.EndsWith(" m, off the start of the part."));
        }

        [Test]
        public void ASwingPastTheHandOver_IsReported()
        {
            IReadOnlyList<string> problems = Flat("swing-out", 60, Vine(55, 80, 55, 56, 30)).Validate();
            Assert.That(problems, Has.Some.StartsWith("The vine hanging at 55.0 m swings out to ").And.EndsWith(" m, past where the part hands over at 60.0 m."));
        }

        [Test]
        public void ARopeThatSwingsItsRunnerIntoTheGround_IsReported()
        {
            IReadOnlyList<string> problems = Flat("low-vine", 60, Vine(20, 60, 24, 39, 30)).Validate();
            Assert.That(problems, Has.Some.StartsWith("The vine hanging at 20.0 m swings a runner's feet down to ")
                .And.Contains("below the ground from 0.0 m to 60.0 m at 0.0 m. A vine hangs high enough that the bottom of its swing clears the ground."));
        }

        [Test]
        public void TheRopeRule_ReadsTheGroundUnderTheWholeSwing()
        {
            // A 6 m stretch of raised ground under the swing's far reach, clear of the anchor itself.
            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("raised-under-swing"),
                ground:        new List<GroundSegment> { Ground(0, 25, 0), Ground(25, 31, 10), Ground(31, 60, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition>(EveryPosition),
                isSetPiece: false,
                vines:         new List<CoursePartVine> { Vine(20, 80, 34, 49, 30) });

            Assert.That(part.Validate(), Has.Some.Contains("below the ground from 25.0 m to 31.0 m at 1.0 m"));
        }

        [Test]
        public void APlatformWithNoWidth_IsReported()
        {
            IReadOnlyList<string> problems = Flat("thin-platform", 60, Vine(20, 80, 30, 30, 30)).Validate();
            Assert.That(problems, Has.Some.EqualTo("The platform of the vine hanging at 20.0 m starts at 30.0 m and ends at 30.0 m, so it has no width."));
        }

        [Test]
        public void APlatformBehindTheAnchor_IsReported()
        {
            IReadOnlyList<string> problems = Flat("behind-platform", 60, Vine(30, 80, 12, 28, 30)).Validate();
            Assert.That(problems, Has.Some.EqualTo("The platform of the vine hanging at 30.0 m starts at 12.0 m, behind the anchor. A vine's platform lies ahead of it, where the swing carries a runner."));
        }

        [Test]
        public void APlatformOffThePart_IsReported()
        {
            IReadOnlyList<string> problems = Flat("off-platform", 60, Vine(40, 80, 44, 70, 30)).Validate();
            Assert.That(problems, Has.Some.EqualTo("The platform of the vine hanging at 40.0 m runs from 44.0 m to 70.0 m, off the part, which runs from 0 m to 60.0 m."));
            Assert.That(problems, Has.None.Contains("inside the 4.0 m the part hands over from"), "a platform off the part is reported once");
        }

        [Test]
        public void APlatformInTheExitClearance_IsReported()
        {
            IReadOnlyList<string> problems = Flat("late-platform", 60, Vine(40, 80, 44, 58, 30)).Validate();
            Assert.That(problems, Is.EqualTo(new[] { "The platform of the vine hanging at 40.0 m ends at 58.0 m, inside the 4.0 m the part hands over from." }));
        }

        [Test]
        public void APlatformNotAboveTheGround_IsReported()
        {
            IReadOnlyList<string> problems = Flat("sunk-platform", 60, Vine(20, 80, 24, 39, 0)).Validate();
            Assert.That(problems, Is.EqualTo(new[] { "The platform of the vine hanging at 20.0 m stands at 0.0 m, not above the ground from 0.0 m to 60.0 m at 0.0 m." }));
        }

        [Test]
        public void ASpikeBedUnderThePlatform_IsReported()
        {
            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("spiked-platform"),
                ground:        new List<GroundSegment> { Ground(0, 60, 0) },
                hazards:       new List<HazardSpan> { new HazardSpan(F32.FromInt(30), F32.FromInt(32)) },
                positions:     new List<CoursePartPosition>(EveryPosition),
                isSetPiece: false,
                vines:         new List<CoursePartVine> { Vine(20, 80, 24, 39, 30) });

            Assert.That(part.Validate(), Is.EqualTo(new[] { "The spike bed at 30.0 m lies under the platform of the vine hanging at 20.0 m." }));
        }

        [Test]
        public void ABridgeUnderThePlatform_IsReported()
        {
            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("bridged-platform"),
                ground:        new List<GroundSegment> { Ground(0, 60, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition>(EveryPosition),
                isSetPiece: false,
                bridges:       new List<CoursePartBridge> { new CoursePartBridge(F32.FromInt(36), F32.FromInt(44)) },
                vines:         new List<CoursePartVine> { Vine(20, 80, 24, 39, 30) });

            Assert.That(part.Validate(), Is.EqualTo(new[] { "The bridge from 36.0 m to 44.0 m lies under the platform of the vine hanging at 20.0 m." }));
        }

        [Test]
        public void OverlappingVines_AreReported()
        {
            CoursePartInfo part = Flat("crowded-vines", 90, Vine(20, 80, 24, 39, 30), Vine(42, 80, 46, 60, 30));
            Assert.That(part.Validate(), Has.Some.StartsWith("The vine hanging at 42.0 m swings back to ")
                .And.EndsWith(" m, before the reach of the one before it ends, at 39.0 m."));
        }

        [Test]
        public void VinesOutOfOrder_AreReported()
        {
            CoursePartInfo part = Flat("unsorted-vines", 100, Vine(60, 80, 64, 79, 30), Vine(20, 80, 24, 39, 30));
            Assert.That(part.Validate(), Has.Some.StartsWith("The vine hanging at 20.0 m swings back to "));
        }

        [Test]
        public void VinesClearOfEachOther_AreSound()
        {
            Assert.That(Flat("two-vines", 100, Vine(20, 80, 24, 39, 30), Vine(50, 80, 54, 69, 30)).Validate(), Is.Empty);
        }

        #endregion

        #region The composer

        [Test]
        public void APartsVine_IsOffsetWithItsPart()
        {
            CoursePartInfo vined = Flat("vined-second", 60, Vine(20, 80, 24, 39, 30), Vine(46, 90, 50, 55, 35));
            CourseDefinition course = CourseComposer.Compose(
                CourseId.FromString("test-vine-offset"), new[] { Flat("flat-first", 30), vined }, _gameConfig.Global);

            ClimbDefinition         climb  = course.Climbs[0];
            VineEncounterDefinition first  = FindVine(course, "part-2-vine-1");
            VineEncounterDefinition second = FindVine(course, "part-2-vine-2");

            Assert.Multiple(() =>
            {
                Assert.That(first.AnchorX, Is.EqualTo(climb.GateX + F32.FromInt(20)), "the anchor is laid down from the part's local X=0, the gate");
                Assert.That(first.AnchorY, Is.EqualTo(climb.TopY + F32.FromInt(8)), "the anchor is raised with the part");
                Assert.That(first.PlatformStartX, Is.EqualTo(climb.GateX + F32.FromInt(24)));
                Assert.That(first.PlatformEndX, Is.EqualTo(climb.GateX + F32.FromInt(39)));
                Assert.That(first.PlatformTopY, Is.EqualTo(climb.TopY + F32.FromInt(3)));
                Assert.That(second.AnchorX, Is.EqualTo(climb.GateX + F32.FromInt(46)));
                Assert.That(second.AnchorY, Is.EqualTo(climb.TopY + F32.Ratio10(90)));
                Assert.That(second.PlatformTopY, Is.EqualTo(climb.TopY + F32.Ratio10(35)));
            });
        }

        [Test]
        public void ComposedVines_HaveUniqueIds()
        {
            CourseDefinition course = CourseComposer.Compose(
                CourseId.FromString("test-vine-ids"),
                new[] { Flat("vines-a", 60, Vine(20, 80, 24, 39, 30)), Flat("vines-b", 60, Vine(20, 80, 24, 39, 30)), Flat("vines-c", 100, Vine(20, 80, 24, 39, 30), Vine(50, 80, 54, 69, 30)) },
                _gameConfig.Global);

            HashSet<string> ids   = new HashSet<string>();
            int             vines = 0;
            foreach (CourseEncounterDefinition encounter in course.Encounters)
            {
                Assert.That(ids.Add(encounter.Id), Is.True, $"the encounter id '{encounter.Id}' is used twice");
                if (encounter is VineEncounterDefinition)
                    vines++;
            }
            Assert.That(vines, Is.EqualTo(4));
        }

        /// <summary>
        /// The same jump the vine tests catch the fixture's vine with (<see cref="TestCourses.Vine"/>) catches the
        /// vine of a part built like that course, and the two runs are the same run, step for step, through the
        /// swing.
        /// </summary>
        [Test]
        public void APartsVine_SwingsExactlyLikeTheVineCourses()
        {
            CourseDefinition composed = CourseComposer.Compose(CourseId.FromString("test-vine-swing"), new[] { VineCourseShaped() }, _gameConfig.Global);
            TimeTrialRules   rules    = new TimeTrialRules();

            TimeTrialSimulation onVineCourse = new TimeTrialSimulation(VineCourse, rules);
            TimeTrialSimulation onPart       = new TimeTrialSimulation(composed, rules);

            bool caught = false;
            int  pressedAt = -1;
            for (int step = 0; step < 600 && onVineCourse.X < F32.FromInt(44) && !onVineCourse.IsOver; step++)
            {
                if (pressedAt < 0 && onVineCourse.X >= F32.FromInt(19))
                {
                    pressedAt = onVineCourse.StepIndex;
                    onVineCourse.ApplyInput(new TimeTrialInput(pressedAt, true));
                    onPart.ApplyInput(new TimeTrialInput(pressedAt, true));
                }
                else if (pressedAt >= 0 && onVineCourse.StepIndex == pressedAt + 45)
                {
                    onVineCourse.ApplyInput(new TimeTrialInput(onVineCourse.StepIndex, false));
                    onPart.ApplyInput(new TimeTrialInput(onPart.StepIndex, false));
                }

                onVineCourse.Step();
                onPart.Step();

                Assert.That(onPart.X, Is.EqualTo(onVineCourse.X), $"the runs parted along X at step {onVineCourse.StepIndex}");
                Assert.That(onPart.Y, Is.EqualTo(onVineCourse.Y), $"the runs parted in height at step {onVineCourse.StepIndex}");
                Assert.That(onPart.Mode, Is.EqualTo(onVineCourse.Mode), $"the runs parted in mode at step {onVineCourse.StepIndex}");
                caught |= onPart.EncounterPhase == "tap-to-release";
            }

            Assert.That(caught, Is.True, "the jump never caught the part's vine");
        }

        #endregion

        #region The space tool

        [Test]
        public void AddingSpaceBeforeAVine_MovesItWhole()
        {
            Assert.That(CoursePartSpace.TryAdd(SpaceSample(), F32.FromInt(10), F32.FromInt(5), out CoursePartInfo result, out string refusal), Is.True, refusal);
            AssertVine(result.Vines[0], 25, 80, 29, 44, 30);
        }

        [Test]
        public void AddingSpaceBetweenTheAnchorAndThePlatform_CarriesThePlatformOn()
        {
            Assert.That(CoursePartSpace.TryAdd(SpaceSample(), F32.FromInt(22), F32.FromInt(3), out CoursePartInfo result, out string refusal), Is.True, refusal);
            AssertVine(result.Vines[0], 20, 80, 27, 42, 30);
        }

        [Test]
        public void AddingSpacePastAVine_LeavesItWhereItIs()
        {
            Assert.That(CoursePartSpace.TryAdd(SpaceSample(), F32.FromInt(50), F32.FromInt(5), out CoursePartInfo result, out string refusal), Is.True, refusal);
            AssertVine(result.Vines[0], 20, 80, 24, 39, 30);
        }

        [Test]
        public void RemovingSpaceBeforeAVine_MovesItBack()
        {
            Assert.That(CoursePartSpace.TryRemove(SpaceSample(), F32.FromInt(5), F32.FromInt(3), out CoursePartInfo result, out string refusal), Is.True, refusal);
            AssertVine(result.Vines[0], 17, 80, 21, 36, 30);
        }

        [Test]
        public void RemovingSpaceThatWouldCutAVine_IsRefused()
        {
            foreach (int at in new[] { 19, 22, 30, 38 })
            {
                bool accepted = CoursePartSpace.TryRemove(SpaceSample(), F32.FromInt(at), F32.FromInt(2), out CoursePartInfo result, out string refusal);
                Assert.That(accepted, Is.False, $"a removal at {at} m cut the vine");
                Assert.That(result, Is.Null);
                Assert.That(refusal, Is.EqualTo($"The span from {at}.0 m to {at + 2}.0 m would cut the vine hanging at 20.0 m, whose platform runs from 24.0 m to 39.0 m. Move or remove it first."));
            }
        }

        [Test]
        public void RemovingSpaceThatWouldBreakAVineRule_IsRefused()
        {
            // Taking 14 m out before the vine would swing it back off the start of the part.
            bool accepted = CoursePartSpace.TryRemove(SpaceSample(), F32.FromInt(4), F32.FromInt(14), out CoursePartInfo _, out string refusal);
            Assert.That(accepted, Is.False);
            Assert.That(refusal, Does.StartWith("That would break a rule: The vine hanging at 6.0 m"));
        }

        #endregion

        #region The draft

        [Test]
        public void ADraft_CarriesItsVinesByValueThroughSerialization()
        {
            DraftCoursePart draft = new DraftCoursePart(Flat("draft-vine", 60, Vine(20, 80, 24, 39, 30)));

            byte[]          bytes    = MetaSerialization.SerializeTagged(draft, MetaSerializationFlags.IncludeAll, logicVersion: null);
            DraftCoursePart restored = MetaSerialization.DeserializeTagged<DraftCoursePart>(bytes, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            Assert.That(restored.Vines, Has.Count.EqualTo(1));
            AssertVine(restored.Vines[0], 20, 80, 24, 39, 30);

            CourseDefinition course = restored.TryComposeAlone(_gameConfig.Global);
            Assert.That(course, Is.Not.Null, "the draft with a vine did not compose");
            AssertVine(FindVine(course, "part-1-vine-1"), 20, 80, 24, 39, 30);
        }

        [Test]
        public void ADraftWithTooManyVines_IsOutOfBounds()
        {
            List<CoursePartVine> vines = new List<CoursePartVine>();
            for (int ndx = 0; ndx <= DraftCoursePart.MaxRowsPerList; ndx++)
                vines.Add(Vine(20, 80, 24, 39, 30));

            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("crowded-draft"),
                ground:        new List<GroundSegment> { Ground(0, 60, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition>(EveryPosition),
                isSetPiece: false,
                vines:         vines);

            Assert.That(new DraftCoursePart(part).IsWithinBounds(), Is.False);
        }

        [Test]
        public void ADraftWithABrokenVine_DoesNotCompose()
        {
            DraftCoursePart draft = new DraftCoursePart(Flat("broken-draft-vine", 60, Vine(20, 60, 24, 39, 30)));
            Assert.That(draft.TryComposeAlone(_gameConfig.Global), Is.Null);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// <see cref="TestCourses.Vine"/> as a part: the take-off ground dropping into the spike basin at 20 m,
        /// the vine hanging over the basin with its platform beyond the spikes, and the basin climbing back to the
        /// part's level so the part is left at local Y=0.
        /// </summary>
        static CoursePartInfo VineCourseShaped()
            => new CoursePartInfo(
                id:            CoursePartId.FromString("vine-course-part"),
                ground:        new List<GroundSegment> { Ground(0, 20, 0), Ground(20, 50, -30), Ground(50, 70, 0) },
                hazards:       new List<HazardSpan> { Spikes(21, 23), Spikes(24, 26), Spikes(27, 29), Spikes(30, 31) },
                positions:     new List<CoursePartPosition>(EveryPosition),
                isSetPiece: false,
                vines:         new List<CoursePartVine> { Vine(27, 80, 31, 46, 30) });

        /// <summary>Flat ground 0–60 with a vine hung at 20 m.</summary>
        static CoursePartInfo SpaceSample() => Flat("vine-space-sample", 60, Vine(20, 80, 24, 39, 30));

        /// <summary>One flat stretch of <paramref name="length"/> metres at local Y=0, with the given vines.</summary>
        static CoursePartInfo Flat(string id, int length, params CoursePartVine[] vines)
            => new CoursePartInfo(
                id:            CoursePartId.FromString(id),
                ground:        new List<GroundSegment> { Ground(0, length, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition>(EveryPosition),
                isSetPiece: false,
                vines:         new List<CoursePartVine>(vines));

        /// <summary>A vine authored in whole metres along the part and decimetres of height.</summary>
        static CoursePartVine Vine(int anchorX, int anchorDecimetres, int platformStartX, int platformEndX, int platformDecimetres)
            => new CoursePartVine(F32.FromInt(anchorX), F32.Ratio10(anchorDecimetres), F32.FromInt(platformStartX), F32.FromInt(platformEndX), F32.Ratio10(platformDecimetres));

        static GroundSegment Ground(int startX, int endX, int topDecimetres)
            => new GroundSegment(F32.FromInt(startX), F32.FromInt(endX), F32.Ratio10(topDecimetres));

        static HazardSpan Spikes(int startX, int endX) => new HazardSpan(F32.FromInt(startX), F32.FromInt(endX));

        /// <summary>A one-part file whose part carries <paramref name="extra"/> after its bridges.</summary>
        static string PartJson(string extra)
            => "[ { \"id\": \"vine-json\", \"positions\": [\"First\"], \"isSetPiece\": false, "
             + "\"ground\": [ { \"startX\": 0, \"endX\": 60, \"topY\": 0 } ], \"hazards\": [], \"bridges\": [], "
             + extra + " } ]";

        static VineEncounterDefinition FindVine(CourseDefinition course, string id)
        {
            foreach (CourseEncounterDefinition encounter in course.Encounters)
            {
                if (encounter is VineEncounterDefinition vine && vine.Id == id)
                    return vine;
            }
            throw new AssertionException($"the course has no vine '{id}'");
        }

        static void AssertVine(CoursePartVine vine, int anchorX, int anchorDecimetres, int platformStartX, int platformEndX, int platformDecimetres)
        {
            Assert.Multiple(() =>
            {
                Assert.That(vine.AnchorX, Is.EqualTo(F32.FromInt(anchorX)), "anchor X");
                Assert.That(vine.AnchorY, Is.EqualTo(F32.Ratio10(anchorDecimetres)), "anchor height");
                Assert.That(vine.PlatformStartX, Is.EqualTo(F32.FromInt(platformStartX)), "platform start");
                Assert.That(vine.PlatformEndX, Is.EqualTo(F32.FromInt(platformEndX)), "platform end");
                Assert.That(vine.PlatformTopY, Is.EqualTo(F32.Ratio10(platformDecimetres)), "platform height");
            });
        }

        static void AssertVine(VineEncounterDefinition vine, int anchorX, int anchorDecimetres, int platformStartX, int platformEndX, int platformDecimetres)
        {
            Assert.Multiple(() =>
            {
                Assert.That(vine.AnchorX, Is.EqualTo(F32.FromInt(anchorX)), "anchor X");
                Assert.That(vine.AnchorY, Is.EqualTo(F32.Ratio10(anchorDecimetres)), "anchor height");
                Assert.That(vine.PlatformStartX, Is.EqualTo(F32.FromInt(platformStartX)), "platform start");
                Assert.That(vine.PlatformEndX, Is.EqualTo(F32.FromInt(platformEndX)), "platform end");
                Assert.That(vine.PlatformTopY, Is.EqualTo(F32.Ratio10(platformDecimetres)), "platform height");
            });
        }

        static void AssertSameVines(CoursePartInfo actual, CoursePartInfo expected)
        {
            Assert.That(actual.Vines, Has.Count.EqualTo(expected.Vines.Count), "vine count");
            for (int ndx = 0; ndx < expected.Vines.Count; ndx++)
            {
                Assert.That(actual.Vines[ndx].AnchorX.Raw, Is.EqualTo(expected.Vines[ndx].AnchorX.Raw), $"vines[{ndx}].anchorX");
                Assert.That(actual.Vines[ndx].AnchorY.Raw, Is.EqualTo(expected.Vines[ndx].AnchorY.Raw), $"vines[{ndx}].anchorY");
                Assert.That(actual.Vines[ndx].PlatformStartX.Raw, Is.EqualTo(expected.Vines[ndx].PlatformStartX.Raw), $"vines[{ndx}].platformStartX");
                Assert.That(actual.Vines[ndx].PlatformEndX.Raw, Is.EqualTo(expected.Vines[ndx].PlatformEndX.Raw), $"vines[{ndx}].platformEndX");
                Assert.That(actual.Vines[ndx].PlatformTopY.Raw, Is.EqualTo(expected.Vines[ndx].PlatformTopY.Raw), $"vines[{ndx}].platformTopY");
            }
        }

        #endregion
    }
}
