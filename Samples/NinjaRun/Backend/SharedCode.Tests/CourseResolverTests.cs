using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="CourseResolver"/>: every id the game hands out resolves to its course, and any other
    /// id resolves to null.
    /// </summary>
    [TestFixture]
    public class CourseResolverTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        /// <summary>
        /// Every legal map id resolves to a course named by that id, with one climb between each pair of parts.
        /// </summary>
        [Test]
        public void EveryLegalMapIdResolvesToItsComposition()
        {
            IReadOnlyList<CourseId> mapIds = CourseComposer.LegalMapIds(_gameConfig);
            Assert.That(mapIds, Is.Not.Empty, "the published parts compose into no map at all");

            foreach (CourseId mapId in mapIds)
            {
                CourseDefinition course = CourseResolver.TryResolve(mapId, _gameConfig);

                Assert.That(course, Is.Not.Null, $"{mapId}: a legal map did not resolve");
                Assert.That(course.Id, Is.EqualTo(mapId), $"{mapId}: the composed course is not named by the map that asked for it");
                Assert.That(course.Climbs.Count, Is.EqualTo(CourseComposer.PartsPerMap - 1), $"{mapId}: a map's parts are joined by one climb fewer than there are parts");
            }
        }

        /// <summary>Without a config, a map id resolves to null.</summary>
        [Test]
        public void AMapIdResolvesToNothingWithoutAConfig()
        {
            foreach (CourseId mapId in CourseComposer.LegalMapIds(_gameConfig))
                Assert.That(CourseResolver.TryResolve(mapId, null), Is.Null, $"{mapId}: a map was composed with no config to compose it from");
        }

        /// <summary>An id that names no map, and no id at all, resolve to null rather than throwing.</summary>
        [Test]
        public void AnIdThatNamesNoMapResolvesToNothing()
        {
            Assert.Multiple(() =>
            {
                foreach ((string what, CourseId courseId) in UnresolvableIds())
                    Assert.That(CourseResolver.TryResolve(courseId, _gameConfig), Is.Null, $"{what} ('{courseId}') resolved to a course");

                Assert.That(CourseResolver.TryResolve((CourseId)null, _gameConfig), Is.Null, "no id at all resolved to a course");
            });
        }

        /// <summary>
        /// <see cref="CourseComposer.Compose(CourseId, SharedGameConfig)"/> throws for an id that names no map.
        /// </summary>
        [Test]
        public void ComposeThrowsForAnIdThatNamesNoMap()
        {
            foreach ((string what, CourseId courseId) in UnresolvableIds())
                Assert.Throws<ArgumentException>(() => CourseComposer.Compose(courseId, _gameConfig), $"{what} ('{courseId}') did not throw out of the throwing form");
        }

        /// <summary>
        /// A ticket that carries no draft resolves exactly as its id does, for every map and for every id that
        /// names nothing.
        /// </summary>
        [Test]
        public void ATicketWithoutADraftResolvesAsItsIdDoes()
        {
            foreach (CourseId mapId in CourseComposer.LegalMapIds(_gameConfig))
            {
                TimeTrialTicket  ticket   = new TimeTrialTicket(1, mapId, TimeTrialConstants.RulesetVersion, MetaTime.Epoch);
                CourseDefinition resolved = CourseResolver.TryResolve(ticket, _gameConfig);
                Assert.That(resolved, Is.Not.Null, $"{mapId}: a ticket on a legal map did not resolve");
                Assert.That(Describe(resolved), Is.EqualTo(Describe(CourseResolver.TryResolve(mapId, _gameConfig))), $"{mapId}: the ticket resolved to a different course than its id");
            }

            foreach ((string what, CourseId courseId) in UnresolvableIds())
                Assert.That(CourseResolver.TryResolve(new TimeTrialTicket(1, courseId, TimeTrialConstants.RulesetVersion, MetaTime.Epoch), _gameConfig), Is.Null, $"{what} ('{courseId}') resolved through a ticket");

            Assert.That(CourseResolver.TryResolve((TimeTrialTicket)null, _gameConfig), Is.Null, "no ticket at all resolved to a course");
        }

        #region Helpers

        /// <summary>
        /// The ids that name no course, one of each kind the resolver has to answer for: one that is not shaped
        /// like a map at all, one shaped like a map but short a part, one naming a part the archive does not hold,
        /// and one repeating a part where a map takes three different ones.
        /// </summary>
        static IEnumerable<(string What, CourseId CourseId)> UnresolvableIds()
        {
            CourseId sample = CourseComposer.LegalMapIds(_gameConfig).First();
            Assert.That(CourseComposer.TryParseMapId(sample, out CoursePartId[] parts), Is.True);

            yield return ("an id that is not a map", CourseId.FromString("no-such-course"));
            yield return ("a map id short a part", CourseId.FromString($"a{CourseComposer.PartSeparator}b"));
            yield return ("a map naming a part the archive does not hold",
                CourseComposer.MapIdFor(parts[0], CoursePartId.FromString("no-such-part"), parts[2]));
            yield return ("a map made of one part three times", CourseComposer.MapIdFor(parts[0], parts[0], parts[0]));
        }

        /// <summary>A composed course as comparable text: everything the composer decided, and nothing else.</summary>
        static string Describe(CourseDefinition course)
        {
            List<string> lines = new List<string> { $"start {course.StartX} finish {course.FinishX}" };
            foreach (GroundSegment segment in course.Ground)
                lines.Add($"ground {segment.StartX} {segment.EndX} {segment.TopY}");
            foreach (HazardSpan hazard in course.Hazards)
                lines.Add($"spikes {hazard.StartX} {hazard.EndX}");
            foreach (ClimbDefinition climb in course.Climbs)
                lines.Add($"climb {climb.Id} {climb.BottomX} {climb.TopX} {climb.TopY} {climb.GateX}");
            return string.Join("\n", lines);
        }

        #endregion // Helpers
    }
}
