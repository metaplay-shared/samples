using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Course decorations: the cosmetic props a part places. They are read from and written to the part's JSON
    /// with their kind, X, layer and flip, a bad one is refused by part and field, they are carried into a map
    /// at the place and height of the ground their part was laid down at, and nothing a run does changes with
    /// them.
    /// </summary>
    [TestFixture]
    public class CourseDecorationTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        [Test]
        public void APartWithoutDecorations_ReadsAsNone_AndIsWrittenWithoutThem()
        {
            CoursePartInfo part = CoursePartJson.ParseAll("[" + PartJson("bare") + "]")[0];
            Assert.That(part.Decorations, Is.Empty);
            Assert.That(CoursePartJson.Write(part), Does.Not.Contain("decorations"), "an undecorated part is written without a decorations property");
        }

        [Test]
        public void Decorations_RoundTripThroughTheWriter()
        {
            CoursePartInfo part = FlatPart("round-trip", new List<CoursePartDecoration>
            {
                new CoursePartDecoration("stone-lantern", F32.Ratio10(55), CourseDecorationLayer.Behind),
                new CoursePartDecoration("stone-lantern", F32.FromInt(24), CourseDecorationLayer.Front, flip: true),
            });

            string written = CoursePartJson.Write(part);
            Assert.That(written, Does.Contain("\"decorations\": [\n    { \"kind\": \"stone-lantern\", \"x\": 5.5, \"layer\": \"Behind\" },\n    { \"kind\": \"stone-lantern\", \"x\": 24, \"layer\": \"Front\", \"flip\": true }\n  ]\n}"));

            CoursePartInfo reread = CoursePartJson.ParseAll("[" + written + "]")[0];
            Assert.That(reread.Decorations, Has.Count.EqualTo(2));
            for (int ndx = 0; ndx < 2; ndx++)
            {
                Assert.That(reread.Decorations[ndx].Kind,  Is.EqualTo(part.Decorations[ndx].Kind));
                Assert.That(reread.Decorations[ndx].X.Raw, Is.EqualTo(part.Decorations[ndx].X.Raw));
                Assert.That(reread.Decorations[ndx].Layer, Is.EqualTo(part.Decorations[ndx].Layer));
                Assert.That(reread.Decorations[ndx].Flip,  Is.EqualTo(part.Decorations[ndx].Flip));
            }
        }

        [TestCase("\"decorations\": {}",                                                              "decorations must be an array, not an object")]
        [TestCase("\"decorations\": [ 3 ]",                                                           "decorations[0] must be an object, not a number")]
        [TestCase("\"decorations\": [ { \"x\": 5, \"layer\": \"Behind\" } ]",                           "decorations[0].kind is missing")]
        [TestCase("\"decorations\": [ { \"kind\": 7, \"x\": 5, \"layer\": \"Behind\" } ]",              "decorations[0].kind must be a kind id string, not a number")]
        [TestCase("\"decorations\": [ { \"kind\": \"\", \"x\": 5, \"layer\": \"Behind\" } ]",           "decorations[0].kind must be a kind id string, not an empty one")]
        [TestCase("\"decorations\": [ { \"kind\": \"stone-lantern\", \"layer\": \"Behind\" } ]",        "decorations[0].x is missing")]
        [TestCase("\"decorations\": [ { \"kind\": \"stone-lantern\", \"x\": \"5\", \"layer\": \"Behind\" } ]", "decorations[0].x must be a number of metres, not a string")]
        [TestCase("\"decorations\": [ { \"kind\": \"stone-lantern\", \"x\": 5 } ]",                     "decorations[0].layer is missing")]
        [TestCase("\"decorations\": [ { \"kind\": \"stone-lantern\", \"x\": 5, \"layer\": \"behind\" } ]", "decorations[0].layer is \"behind\", which is not a layer. Expected one of Behind, Front.")]
        [TestCase("\"decorations\": [ { \"kind\": \"stone-lantern\", \"x\": 5, \"layer\": 1 } ]",       "decorations[0].layer is 1, which is not a layer")]
        [TestCase("\"decorations\": [ { \"kind\": \"stone-lantern\", \"x\": 5, \"layer\": \"Front\", \"flip\": 1 } ]", "decorations[0].flip must be true or false, not a number")]
        [TestCase("\"decorations\": [ { \"kind\": \"stone-lantern\", \"x\": 5, \"layer\": \"Front\", \"y\": 1 } ]",    "decorations[0].y is not a property the format has")]
        public void ABadDecoration_IsRefusedByPartAndField(string decorations, string expectedMessage)
        {
            string json = "[" + PartJson("broken", decorations: decorations) + "]";
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(json));
            Assert.That(refused.Message, Does.StartWith("CourseParts.json: part [0] ('broken'): "));
            Assert.That(refused.Message, Does.Contain(expectedMessage));
        }

        [TestCase("Stone-Lantern", "lower-case letters and digits")]
        [TestCase("stone--lantern", "lower-case letters and digits")]
        [TestCase("stone_lantern", "lower-case letters and digits")]
        [TestCase("-lantern", "lower-case letters and digits")]
        public void ADecorationKindThatIsNotAPublishableId_BreaksARule(string kind, string expected)
        {
            CoursePartInfo part = FlatPart("kinded", new List<CoursePartDecoration> { new CoursePartDecoration(kind, F32.FromInt(10), CourseDecorationLayer.Behind) });
            Assert.That(string.Join("\n", part.Validate()), Does.Contain(expected));
        }

        [Test]
        public void ADecorationOffTheGround_OrOnABridge_BreaksARule()
        {
            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("placed"),
                ground:        new List<GroundSegment> { Ground(0, 20, 0), Ground(24, 50, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     AllPositions(),
                isSetPiece: false,
                bridges:       new List<CoursePartBridge> { new CoursePartBridge(F32.FromInt(30), F32.FromInt(38)) },
                decorations:   new List<CoursePartDecoration>
                {
                    new CoursePartDecoration("stone-lantern", F32.FromInt(22), CourseDecorationLayer.Behind),  // over the pit
                    new CoursePartDecoration("stone-lantern", F32.FromInt(34), CourseDecorationLayer.Front),   // on the deck
                    new CoursePartDecoration("stone-lantern", F32.FromInt(20), CourseDecorationLayer.Behind),  // on the near lip
                    new CoursePartDecoration("stone-lantern", F32.FromInt(50), CourseDecorationLayer.Behind),  // at the hand-over
                    new CoursePartDecoration("stone-lantern", F32.FromInt(30), CourseDecorationLayer.Behind),  // by the bridge's anchor
                });

            IReadOnlyList<string> problems = part.Validate();
            Assert.That(problems, Has.Count.EqualTo(2), string.Join("\n", problems));
            Assert.That(problems[0], Does.Contain("at 22.0 m does not stand on the part's ground"));
            Assert.That(problems[1], Does.Contain("at 34.0 m stands on the bridge"));
        }

        [Test]
        public void Composition_CarriesEachDecorationToItsPartsPlaceAndHeight()
        {
            CoursePartInfo first = FlatPart("deco-first", new List<CoursePartDecoration>
            {
                new CoursePartDecoration("stone-lantern", F32.FromInt(10), CourseDecorationLayer.Behind),
            });
            CoursePartInfo middle = new CoursePartInfo(
                id:            CoursePartId.FromString("deco-middle"),
                ground:        new List<GroundSegment> { Ground(0, 10, 0), Ground(10, 20, 5), Ground(20, 30, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     AllPositions(),
                isSetPiece: false,
                bridges:       new List<CoursePartBridge>(),
                decorations:   new List<CoursePartDecoration>
                {
                    new CoursePartDecoration("stone-lantern", F32.FromInt(15), CourseDecorationLayer.Front, flip: true),
                    new CoursePartDecoration("stone-lantern", F32.FromInt(10), CourseDecorationLayer.Behind),
                });
            CoursePartInfo last = FlatPart("deco-last", new List<CoursePartDecoration>
            {
                new CoursePartDecoration("stone-lantern", F32.FromInt(4), CourseDecorationLayer.Behind),
            });

            CourseDefinition course = CourseComposer.Compose(CourseId.FromString("deco-map"), new[] { first, middle, last }, _gameConfig.Global);
            Assert.That(course.Climbs, Has.Count.EqualTo(2));

            // Each part's local X=0 is the gate of the climb before it, and its local Y=0 that climb's top.
            F32 middleX = course.Climbs[0].GateX;
            F32 middleY = course.Climbs[0].TopY;
            F32 lastX   = course.Climbs[1].GateX;
            F32 lastY   = course.Climbs[1].TopY;

            Assert.That(course.Decorations, Has.Count.EqualTo(4));
            AssertDecoration(course.Decorations[0], "part-1-decoration-1", F32.FromInt(10),           F32.Zero,                CourseDecorationLayer.Behind, false);
            AssertDecoration(course.Decorations[1], "part-2-decoration-1", middleX + F32.FromInt(15), middleY + F32.Ratio10(5), CourseDecorationLayer.Front,  true);
            // At a step up the decoration stands on the ground that begins there.
            AssertDecoration(course.Decorations[2], "part-2-decoration-2", middleX + F32.FromInt(10), middleY + F32.Ratio10(5), CourseDecorationLayer.Behind, false);
            AssertDecoration(course.Decorations[3], "part-3-decoration-1", lastX + F32.FromInt(4),    lastY,                   CourseDecorationLayer.Behind, false);

            foreach (CourseDecoration decoration in course.Decorations)
                Assert.That(course.FindGroundIndexAt(decoration.X), Is.GreaterThanOrEqualTo(0), $"{decoration.Id} stands over no ground on the map");
        }

        [Test]
        public void Decorations_ChangeNothingARunDoes()
        {
            // Every shipped part, decorated all along its ground in both layers, runs exactly as it does bare:
            // the run with no input and every scripted runner finish, gate and climb at the same step.
            Assert.That(_gameConfig.CourseParts, Is.Not.Empty);
            foreach ((CoursePartId id, CoursePartInfo part) in _gameConfig.CourseParts)
            {
                CoursePartInfo decorated = WithDecorationsEveryFewMetres(part);
                Assert.That(decorated.Validate(), Is.Empty, $"{id}: the decorated copy breaks a rule");
                Assert.That(decorated.Decorations, Is.Not.Empty, $"{id}: nothing was placed on it");

                CourseDefinition bare     = CourseInspection.PartAlone(part, _gameConfig.Global);
                CourseDefinition dressed  = CourseInspection.PartAlone(decorated, _gameConfig.Global);
                Assert.That(dressed.Decorations.Count, Is.EqualTo(decorated.Decorations.Count));

                List<CourseRunnerTime> bareTimes    = CourseInspection.RunnerTimes(bare, _gameConfig.TimeTrialRules);
                List<CourseRunnerTime> dressedTimes = CourseInspection.RunnerTimes(dressed, _gameConfig.TimeTrialRules);
                Assert.That(dressedTimes.Count, Is.EqualTo(bareTimes.Count));
                for (int ndx = 0; ndx < bareTimes.Count; ndx++)
                {
                    TimeTrialResult expected = bareTimes[ndx].Result;
                    TimeTrialResult actual   = dressedTimes[ndx].Result;
                    string who = $"{id}, {bareTimes[ndx].Skill?.ToString() ?? "no input"}";
                    Assert.That(actual == null, Is.EqualTo(expected == null), $"{who}: finished on one course and not the other");
                    if (expected == null || actual == null)
                        continue;
                    Assert.That(actual.Steps,             Is.EqualTo(expected.Steps),             $"{who}: steps");
                    Assert.That(actual.FinishTimeMs,      Is.EqualTo(expected.FinishTimeMs),      $"{who}: finish time");
                    Assert.That(actual.GateTimesMs,       Is.EqualTo(expected.GateTimesMs),       $"{who}: gate times");
                    Assert.That(actual.ClimbEntryTimesMs, Is.EqualTo(expected.ClimbEntryTimesMs), $"{who}: climb entry times");
                }

                // A recording made on the bare part is accepted against the decorated one with the same result.
                TimeTrialRecording recording = TimeTrialBotDriver.Drive(bare, _gameConfig.TimeTrialRules, TimeTrialBotSkill.Competent);
                TimeTrialResult onBare    = TimeTrialSimulation.Replay(bare, _gameConfig.TimeTrialRules, recording);
                TimeTrialResult onDressed = TimeTrialSimulation.Replay(dressed, _gameConfig.TimeTrialRules, recording);
                Assert.That(onDressed?.Steps, Is.EqualTo(onBare?.Steps), $"{id}: a bare recording replays differently on the decorated part");
                Assert.That(onDressed?.FinishTimeMs, Is.EqualTo(onBare?.FinishTimeMs));
            }
        }

        [Test]
        public void ADraftCarriesItsDecorations_ByValue()
        {
            CoursePartInfo part = FlatPart("carried", new List<CoursePartDecoration>
            {
                new CoursePartDecoration("stone-lantern", F32.FromInt(12), CourseDecorationLayer.Front, flip: true),
            });

            DraftCoursePart draft = new DraftCoursePart(part);

            // A later edit to the part's own list does not reach the draft.
            part.Decorations.Add(new CoursePartDecoration("stone-lantern", F32.FromInt(30), CourseDecorationLayer.Behind, flip: false));

            CoursePartInfo back = draft.ToPart();
            Assert.That(back.Decorations, Has.Count.EqualTo(1));
            Assert.That(back.Decorations[0].Kind, Is.EqualTo("stone-lantern"));
            Assert.That(back.Decorations[0].X.Raw, Is.EqualTo(F32.FromInt(12).Raw));
            Assert.That(back.Decorations[0].Layer, Is.EqualTo(CourseDecorationLayer.Front));
            Assert.That(back.Decorations[0].Flip, Is.True);
        }

        #region Helpers

        /// <summary><paramref name="part"/> with a lantern every 7 m of its ground, alternating layers, off every bridge deck.</summary>
        static CoursePartInfo WithDecorationsEveryFewMetres(CoursePartInfo part)
        {
            List<CoursePartDecoration> decorations = new List<CoursePartDecoration>();
            bool front = false;
            foreach (GroundSegment segment in part.Ground)
            {
                for (F32 x = segment.StartX; x < segment.EndX; x += F32.FromInt(7))
                {
                    bool onBridge = false;
                    foreach (CoursePartBridge bridge in part.Bridges)
                        onBridge |= x > bridge.StartX && x < bridge.EndX;
                    if (onBridge)
                        continue;

                    decorations.Add(new CoursePartDecoration("stone-lantern", x, front ? CourseDecorationLayer.Front : CourseDecorationLayer.Behind, flip: front));
                    front = !front;
                }
            }

            return new CoursePartInfo(part.Id, part.Ground, part.Hazards, part.Positions, part.IsSetPiece, part.Bridges, decorations, part.Vines);
        }

        static void AssertDecoration(CourseDecoration decoration, string id, F32 x, F32 y, CourseDecorationLayer layer, bool flip)
        {
            Assert.That(decoration.Id,    Is.EqualTo(id));
            Assert.That(decoration.Kind,  Is.EqualTo("stone-lantern"), $"{id}: kind");
            Assert.That(decoration.X.Raw, Is.EqualTo(x.Raw), $"{id}: x is {decoration.X}, expected {x}");
            Assert.That(decoration.Y.Raw, Is.EqualTo(y.Raw), $"{id}: y is {decoration.Y}, expected {y}");
            Assert.That(decoration.Layer, Is.EqualTo(layer), $"{id}: layer");
            Assert.That(decoration.Flip,  Is.EqualTo(flip), $"{id}: flip");
        }

        /// <summary>A sound 30 m flat part carrying <paramref name="decorations"/>.</summary>
        static CoursePartInfo FlatPart(string id, List<CoursePartDecoration> decorations)
            => new CoursePartInfo(
                id:            CoursePartId.FromString(id),
                ground:        new List<GroundSegment> { Ground(0, 30, 0) },
                hazards:       new List<HazardSpan>(),
                positions:     AllPositions(),
                isSetPiece: false,
                bridges:       new List<CoursePartBridge>(),
                decorations:   decorations);

        static List<CoursePartPosition> AllPositions()
            => new List<CoursePartPosition> { CoursePartPosition.First, CoursePartPosition.Middle, CoursePartPosition.Last };

        static GroundSegment Ground(int startX, int endX, int topDecimetres)
            => new GroundSegment(F32.FromInt(startX), F32.FromInt(endX), F32.Ratio10(topDecimetres));

        /// <summary>A sound 30 m part object, with its decorations property swappable for a test to fill or break.</summary>
        static string PartJson(string id, string decorations = null)
            => "{ \"id\": \"" + id + "\", \"positions\": [\"First\", \"Middle\", \"Last\"], \"isSetPiece\": false,"
             + " \"ground\": [ { \"startX\": 0, \"endX\": 30, \"topY\": 0 } ], \"hazards\": [], \"bridges\": []"
             + (decorations != null ? ", " + decorations : "") + " }";

        #endregion // Helpers
    }
}
