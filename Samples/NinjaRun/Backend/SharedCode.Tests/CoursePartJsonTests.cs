using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The course parts' authoring format, <c>GameConfigSource/CourseParts.json</c>: what the part editor writes
    /// parses back to the same part, the committed file is what the archive holds, and a file with a mistake in
    /// it is refused with the part and the field named rather than read as something else.
    /// </summary>
    [TestFixture]
    public class CoursePartJsonTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        [Test]
        public void EveryShippedPart_RoundTripsThroughTheWriter()
        {
            List<CoursePartInfo> shipped = new List<CoursePartInfo>();
            foreach ((CoursePartId _, CoursePartInfo part) in _gameConfig.CourseParts)
                shipped.Add(part);

            Assert.That(shipped, Is.Not.Empty, "the archive holds no course parts to round-trip");

            IReadOnlyList<CoursePartInfo> reread = CoursePartJson.ParseAll(WriteAll(shipped));
            Assert.That(reread.Count, Is.EqualTo(shipped.Count), "the whole file written back does not hold every part");

            for (int ndx = 0; ndx < shipped.Count; ndx++)
            {
                AssertSamePart(reread[ndx], shipped[ndx]);

                // One object on its own, as the part editor hands it over, pasted into an array.
                IReadOnlyList<CoursePartInfo> pasted = CoursePartJson.ParseAll("[" + CoursePartJson.Write(shipped[ndx]) + "]");
                Assert.That(pasted.Count, Is.EqualTo(1));
                AssertSamePart(pasted[0], shipped[ndx]);
            }
        }

        /// <summary>The whole file for <paramref name="parts"/>: one array holding every part in order.</summary>
        static string WriteAll(IEnumerable<CoursePartInfo> parts)
        {
            List<string> written = new List<string>();
            foreach (CoursePartInfo part in parts)
                written.Add(CoursePartJson.Write(part));
            return "[\n" + string.Join(",\n", written) + "\n]\n";
        }

        [Test]
        public void ASmallPart_IsWrittenInTheFilesShape()
        {
            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("test-ledge"),
                ground:        new List<GroundSegment> { new GroundSegment(F32.Zero, F32.FromInt(20), F32.Zero), new GroundSegment(F32.FromInt(25), F32.FromInt(40), F32.Ratio10(-5)), new GroundSegment(F32.FromInt(44), F32.Ratio100(5025), F32.Zero) },
                hazards:       new List<HazardSpan> { new HazardSpan(F32.FromInt(10), F32.FromInt(12)) },
                positions:     new List<CoursePartPosition> { CoursePartPosition.Middle, CoursePartPosition.Last },
                isSetPiece: true,
                bridges:       new List<CoursePartBridge>());

            string expected =
                "{\n" +
                "  \"id\": \"test-ledge\",\n" +
                "  \"notes\": \"A \\\"quoted\\\" note.\",\n" +
                "  \"positions\": [\"Middle\", \"Last\"],\n" +
                "  \"isSetPiece\": true,\n" +
                "  \"ground\": [\n" +
                "    { \"startX\": 0, \"endX\": 20, \"topY\": 0 },\n" +
                "    { \"startX\": 25, \"endX\": 40, \"topY\": -0.5 },\n" +
                "    { \"startX\": 44, \"endX\": 50.25, \"topY\": 0 }\n" +
                "  ],\n" +
                "  \"hazards\": [\n" +
                "    { \"startX\": 10, \"endX\": 12 }\n" +
                "  ],\n" +
                "  \"bridges\": []\n" +
                "}";

            Assert.That(CoursePartJson.Write(part, "A \"quoted\" note."), Is.EqualTo(expected));
            Assert.That(CoursePartJson.Write(part), Does.Not.Contain("notes"), "a part written without notes carries none");

            IReadOnlyList<CoursePartInfo> reread = CoursePartJson.ParseAll("[" + expected + "]");
            AssertSamePart(reread[0], part);
        }

        [Test]
        public void TheCommittedFile_IsWhatTheArchiveHolds()
        {
            // The archive tests compare the committed archive with a fresh build; this closes the loop from the
            // other end, so the file a developer edits is shown to be exactly the parts a client is served.
            string path = Path.Combine(TestGameConfig.RepoRoot(), "GameConfigSource", GameConfigBuild.JsonSourceFileName(nameof(SharedGameConfig.CourseParts)));
            IReadOnlyList<CoursePartInfo> fromFile = CoursePartJson.ParseAll(File.ReadAllBytes(path));

            Assert.That(fromFile.Count, Is.EqualTo(_gameConfig.CourseParts.Count), "the file and the archive hold different numbers of parts");

            Assert.Multiple(() =>
            {
                foreach (CoursePartInfo part in fromFile)
                {
                    CoursePartInfo archived = _gameConfig.CourseParts.GetValueOrDefault(part.Id);
                    Assert.That(archived, Is.Not.Null, $"{part.Id} is in the file but not in the archive. Run: dotnet run --project tools/GameConfigGen");
                    if (archived != null)
                        AssertSamePart(part, archived);
                }
            });
        }

        /// <summary>Every list property is optional, and one left out reads as an empty list.</summary>
        [Test]
        public void AnAbsentListProperty_ReadsAsEmpty()
        {
            CoursePartInfo part = CoursePartJson.ParseAll("[ { \"id\": \"sparse\", \"isSetPiece\": false } ]")[0];
            Assert.Multiple(() =>
            {
                Assert.That(part.Positions,   Is.Empty);
                Assert.That(part.Ground,      Is.Empty);
                Assert.That(part.Hazards,     Is.Empty);
                Assert.That(part.Bridges,     Is.Empty);
                Assert.That(part.Decorations, Is.Empty);
                Assert.That(part.Vines,       Is.Empty);
            });
        }

        [Test]
        public void AUtf8ByteOrderMark_IsAccepted()
        {
            byte[] withBom = System.Text.Encoding.UTF8.GetPreamble();
            byte[] body    = System.Text.Encoding.UTF8.GetBytes("[" + MinimalPart("bom") + "]");
            byte[] file    = new byte[withBom.Length + body.Length];
            withBom.CopyTo(file, 0);
            body.CopyTo(file, withBom.Length);

            Assert.That(CoursePartJson.ParseAll(file)[0].Id.Value, Is.EqualTo("bom"));
        }

        [TestCase("{ \"startX\": 0, \"topY\": 0 }",                    "part [0] ('broken'): ground[0].endX is missing")]
        [TestCase("{ \"startX\": 0, \"endX\": \"30\", \"topY\": 0 }",  "part [0] ('broken'): ground[0].endX must be a number of metres, not a string")]
        [TestCase("{ \"startX\": 0, \"endX\": 3e1, \"topY\": 0 }",     "part [0] ('broken'): ground[0].endX is 3e1, which is not a plain decimal number")]
        [TestCase("{ \"startX\": 0, \"endX\": 30, \"topY\": 0, \"topy\": 1 }", "part [0] ('broken'): ground[0].topy is not a property the format has")]
        public void ABadGroundSegment_IsRefusedByPartAndField(string segment, string expectedMessage)
        {
            string json = "[" + MinimalPart("broken", ground: segment) + "]";
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(json));
            Assert.That(refused.Message, Does.StartWith("CourseParts.json: "));
            Assert.That(refused.Message, Does.Contain(expectedMessage));
        }

        [Test]
        public void APitFloor_IsWrittenWithItsFlagAndReadBack()
        {
            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("test-pit-floor"),
                ground:        new List<GroundSegment>
                {
                    new GroundSegment(F32.Zero, F32.FromInt(20), F32.Zero),
                    new GroundSegment(F32.FromInt(20), F32.FromInt(26), F32.FromInt(-2), isPit: true),
                    new GroundSegment(F32.FromInt(26), F32.FromInt(40), F32.Zero),
                },
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition> { CoursePartPosition.Middle },
                isSetPiece: false,
                bridges:       new List<CoursePartBridge>());

            string written = CoursePartJson.Write(part);

            Assert.That(written, Does.Contain("    { \"startX\": 20, \"endX\": 26, \"topY\": -2, \"pit\": true },\n"));
            Assert.That(written, Does.Contain("    { \"startX\": 0, \"endX\": 20, \"topY\": 0 },\n"), "a segment that is no pit floor is written without the flag");
            Assert.That(written, Does.Not.Contain("\"pit\": false"));

            IReadOnlyList<CoursePartInfo> reread = CoursePartJson.ParseAll("[" + written + "]");
            AssertSamePart(reread[0], part);
            Assert.That(reread[0].Ground[1].IsPit, Is.True);
        }

        [Test]
        public void AGroundSegmentWithoutThePitFlag_IsNoPitFloor()
        {
            string ground = "{ \"startX\": 0, \"endX\": 10, \"topY\": 0 }, { \"startX\": 10, \"endX\": 14, \"topY\": -2, \"pit\": false }, { \"startX\": 14, \"endX\": 30, \"topY\": 0 }";
            IReadOnlyList<CoursePartInfo> parts = CoursePartJson.ParseAll("[" + MinimalPart("unflagged", ground: ground) + "]");

            Assert.That(parts[0].Ground[0].IsPit, Is.False, "an absent flag reads as no pit floor");
            Assert.That(parts[0].Ground[1].IsPit, Is.False, "a false flag reads as no pit floor");
            Assert.That(CoursePartJson.Write(parts[0]), Does.Not.Contain("\"pit\""), "a false flag is not written back");
        }

        [TestCase("\"yes\"", "a string")]
        [TestCase("1",       "a number")]
        [TestCase("null",    "null")]
        public void ANonBooleanPitFlag_IsRefused(string value, string described)
        {
            string ground = "{ \"startX\": 0, \"endX\": 30, \"topY\": 0, \"pit\": " + value + " }";
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll("[" + MinimalPart("flagged", ground: ground) + "]"));
            Assert.That(refused.Message, Is.EqualTo($"CourseParts.json: part [0] ('flagged'): ground[0].pit must be true or false, not {described}."));
        }

        [Test]
        public void AMissingTopLevelField_IsRefused()
        {
            string json = "[" + MinimalPart("first") + ", { \"id\": \"no-set-piece\", \"positions\": [\"First\"], \"ground\": [], \"hazards\": [], \"bridges\": [] } ]";
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(json));
            Assert.That(refused.Message, Is.EqualTo("CourseParts.json: part [1] ('no-set-piece'): isSetPiece is missing."));
        }

        [Test]
        public void AMissingId_IsRefusedByIndex()
        {
            string json = "[ { \"isSetPiece\": false } ]";
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(json));
            Assert.That(refused.Message, Is.EqualTo("CourseParts.json: part [0]: id is missing."));
        }

        [Test]
        public void AnUnknownProperty_IsRefused()
        {
            string json = "[" + MinimalPart("typo", extra: "\"isSetPeice\": true,") + "]";
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(json));
            Assert.That(refused.Message, Does.Contain("part [0] ('typo'): isSetPeice is not a property the format has"));
        }

        [Test]
        public void ABadPositionName_IsRefused()
        {
            string json = "[" + MinimalPart("lost", positions: "[\"First\", \"middle\"]") + "]";
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(json));
            Assert.That(refused.Message, Does.Contain("part [0] ('lost'): positions[1] is \"middle\", which is not a position. Expected one of First, Middle, Last."));
        }

        [Test]
        public void ANumericPosition_IsRefused()
        {
            string json = "[" + MinimalPart("numbered", positions: "[1]") + "]";
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(json));
            Assert.That(refused.Message, Does.Contain("part [0] ('numbered'): positions[0] is 1, which is not a position"));
        }

        [Test]
        public void ADuplicateId_IsRefused()
        {
            string json = "[" + MinimalPart("twice") + ", " + MinimalPart("other") + ", " + MinimalPart("twice") + "]";
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(json));
            Assert.That(refused.Message, Is.EqualTo("CourseParts.json: part [2] ('twice'): the id is already used by part [0]. A part id names one part."));
        }

        [Test]
        public void TextThatIsNotJson_IsRefused()
        {
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll("[ { \"id\": \"cut-off\" "));
            Assert.That(refused.Message, Does.StartWith("CourseParts.json: the file is not valid JSON"));
        }

        [Test]
        public void ARootThatIsNotAnArray_IsRefused()
        {
            FormatException refused = Assert.Throws<FormatException>(() => CoursePartJson.ParseAll(MinimalPart("alone")));
            Assert.That(refused.Message, Is.EqualTo("CourseParts.json: the file must hold one JSON array of part objects, not an object."));
        }

        #region Helpers

        /// <summary>A sound part object, with one piece swappable for a test to break.</summary>
        static string MinimalPart(string id, string ground = null, string positions = null, string extra = "")
            => "{ \"id\": \"" + id + "\", " + extra
             + " \"positions\": " + (positions ?? "[\"First\", \"Middle\", \"Last\"]")
             + ", \"isSetPiece\": false, \"ground\": [ " + (ground ?? "{ \"startX\": 0, \"endX\": 30, \"topY\": 0 }")
             + " ], \"hazards\": [], \"bridges\": [] }";

        /// <summary>
        /// The id, set-piece flag, positions, ground, hazards, bridges, decorations and vines of two parts, the geometry compared by its raw
        /// fixed-point value.
        /// </summary>
        static void AssertSamePart(CoursePartInfo actual, CoursePartInfo expected)
        {
            string id = expected.Id.Value;
            Assert.That(actual.Id,            Is.EqualTo(expected.Id),            $"{id}: id");
            Assert.That(actual.IsSetPiece, Is.EqualTo(expected.IsSetPiece), $"{id}: isSetPiece");
            Assert.That(actual.Positions,     Is.EqualTo(expected.Positions),     $"{id}: positions");

            Assert.That(actual.Ground.Count, Is.EqualTo(expected.Ground.Count), $"{id}: ground count");
            for (int ndx = 0; ndx < Math.Min(actual.Ground.Count, expected.Ground.Count); ndx++)
            {
                Assert.That(actual.Ground[ndx].StartX.Raw, Is.EqualTo(expected.Ground[ndx].StartX.Raw), $"{id}: ground[{ndx}].startX");
                Assert.That(actual.Ground[ndx].EndX.Raw,   Is.EqualTo(expected.Ground[ndx].EndX.Raw),   $"{id}: ground[{ndx}].endX");
                Assert.That(actual.Ground[ndx].TopY.Raw,   Is.EqualTo(expected.Ground[ndx].TopY.Raw),   $"{id}: ground[{ndx}].topY");
                Assert.That(actual.Ground[ndx].IsPit,      Is.EqualTo(expected.Ground[ndx].IsPit),      $"{id}: ground[{ndx}].pit");
            }

            Assert.That(actual.Hazards.Count, Is.EqualTo(expected.Hazards.Count), $"{id}: hazard count");
            for (int ndx = 0; ndx < Math.Min(actual.Hazards.Count, expected.Hazards.Count); ndx++)
            {
                Assert.That(actual.Hazards[ndx].StartX.Raw, Is.EqualTo(expected.Hazards[ndx].StartX.Raw), $"{id}: hazards[{ndx}].startX");
                Assert.That(actual.Hazards[ndx].EndX.Raw,   Is.EqualTo(expected.Hazards[ndx].EndX.Raw),   $"{id}: hazards[{ndx}].endX");
            }

            Assert.That(actual.Bridges.Count, Is.EqualTo(expected.Bridges.Count), $"{id}: bridge count");
            for (int ndx = 0; ndx < Math.Min(actual.Bridges.Count, expected.Bridges.Count); ndx++)
            {
                Assert.That(actual.Bridges[ndx].StartX.Raw, Is.EqualTo(expected.Bridges[ndx].StartX.Raw), $"{id}: bridges[{ndx}].startX");
                Assert.That(actual.Bridges[ndx].EndX.Raw,   Is.EqualTo(expected.Bridges[ndx].EndX.Raw),   $"{id}: bridges[{ndx}].endX");
            }

            Assert.That(actual.Decorations.Count, Is.EqualTo(expected.Decorations.Count), $"{id}: decoration count");
            for (int ndx = 0; ndx < Math.Min(actual.Decorations.Count, expected.Decorations.Count); ndx++)
            {
                Assert.That(actual.Decorations[ndx].Kind,  Is.EqualTo(expected.Decorations[ndx].Kind),  $"{id}: decorations[{ndx}].kind");
                Assert.That(actual.Decorations[ndx].X.Raw, Is.EqualTo(expected.Decorations[ndx].X.Raw), $"{id}: decorations[{ndx}].x");
                Assert.That(actual.Decorations[ndx].Layer, Is.EqualTo(expected.Decorations[ndx].Layer), $"{id}: decorations[{ndx}].layer");
                Assert.That(actual.Decorations[ndx].Flip,  Is.EqualTo(expected.Decorations[ndx].Flip),  $"{id}: decorations[{ndx}].flip");
            }

            Assert.That(actual.Vines.Count, Is.EqualTo(expected.Vines.Count), $"{id}: vine count");
            for (int ndx = 0; ndx < Math.Min(actual.Vines.Count, expected.Vines.Count); ndx++)
            {
                Assert.That(actual.Vines[ndx].AnchorX.Raw,        Is.EqualTo(expected.Vines[ndx].AnchorX.Raw),        $"{id}: vines[{ndx}].anchorX");
                Assert.That(actual.Vines[ndx].AnchorY.Raw,        Is.EqualTo(expected.Vines[ndx].AnchorY.Raw),        $"{id}: vines[{ndx}].anchorY");
                Assert.That(actual.Vines[ndx].PlatformStartX.Raw, Is.EqualTo(expected.Vines[ndx].PlatformStartX.Raw), $"{id}: vines[{ndx}].platformStartX");
                Assert.That(actual.Vines[ndx].PlatformEndX.Raw,   Is.EqualTo(expected.Vines[ndx].PlatformEndX.Raw),   $"{id}: vines[{ndx}].platformEndX");
                Assert.That(actual.Vines[ndx].PlatformTopY.Raw,   Is.EqualTo(expected.Vines[ndx].PlatformTopY.Raw),   $"{id}: vines[{ndx}].platformTopY");
            }
        }

        #endregion // Helpers
    }
}
