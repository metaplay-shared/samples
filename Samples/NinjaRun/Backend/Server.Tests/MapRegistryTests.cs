using Game.Logic;
using Game.Logic.Tests;
using Game.Server.MapRegistry;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Server.Tests
{
    /// <summary>
    /// The registry of published maps and the handful of them a run may be started on, driven through the
    /// state the registry entity hosts: the entity only supplies the clock and the database.
    /// <para>
    /// What is being held to here is what a player would notice: the active set is always full and always
    /// distinct, a map that has been played is never thrown away, and nothing enters the set that the scripted
    /// runners cannot finish. These run against the shipped parts, because whether a map is finishable is a
    /// question about the shipped geometry and the shipped movement values.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MapRegistryTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        static readonly MetaTime Now = MetaTime.FromDateTime(new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc));

        /// <summary>The period the entity rotates on. Passed in rather than held by the state.</summary>
        static readonly MetaDuration Period = MetaDuration.FromHours(4);

        MapRegistryState Seeded()
        {
            MapRegistryState state = new MapRegistryState();
            state.Initialize(Now, _gameConfig);
            return state;
        }

        /// <summary>
        /// Which active map the next rotation should send away: the one that has been in the set longest, and
        /// on a tie the one listed first in <see cref="MapRegistryState.ActiveMapIds"/>.
        /// </summary>
        static CourseId ExpectedOutgoing(MapRegistryState state)
        {
            CourseId oldest   = null;
            MetaTime oldestAt = MetaTime.Epoch;

            foreach (CourseId mapId in state.ActiveMapIds)
            {
                MapRegistryEntry entry = state.TryGetEntry(mapId);
                if (oldest == null || entry.LastActivatedAt < oldestAt)
                {
                    oldest   = mapId;
                    oldestAt = entry.LastActivatedAt;
                }
            }

            return oldest;
        }

        [Test]
        public void SeedingPublishesFiveMapsAndActivatesFour()
        {
            MapRegistryState state = Seeded();

            Assert.That(state.IsInitialized, Is.True);
            Assert.That(state.Maps.Count, Is.EqualTo(MapRegistryState.InitialMapCount), "the registry opens with its first maps published");
            Assert.That(state.ActiveMapIds.Count, Is.EqualTo(MapRegistryState.ActiveSetSize), "and with a full active set");
            Assert.That(state.ActiveMapIds.Distinct().Count(), Is.EqualTo(state.ActiveMapIds.Count), "the same map must not fill two slots");
            Assert.That(state.LastRotatedAt, Is.EqualTo(Now));
            Assert.That(state.RulesetVersion, Is.EqualTo(TimeTrialConstants.RulesetVersion));

            IReadOnlyList<CourseId> legal = CourseComposer.LegalMapIds(_gameConfig);
            foreach (MapRegistryEntry entry in state.Maps)
            {
                Assert.That(legal, Does.Contain(entry.MapId), $"{entry.MapId} is not a map these parts compose");
                Assert.That(MapRegistryState.PassesPublicationGate(entry.MapId, _gameConfig), Is.True, $"{entry.MapId} was published without being finishable");
                Assert.That(entry.PublishedAt, Is.EqualTo(Now));
            }

            foreach (CourseId mapId in state.ActiveMapIds)
            {
                Assert.That(state.IsActive(mapId), Is.True);
                MapRegistryEntry entry = state.TryGetEntry(mapId);
                Assert.That(entry, Is.Not.Null, "an active map must be one the registry has published");
                Assert.That(entry.TimesActivated, Is.EqualTo(1));
                Assert.That(entry.LastActivatedAt, Is.EqualTo(Now));
            }
        }

        /// <summary>
        /// Five ids drawn off the front of the legal list would all open on the same part, because the list is
        /// enumerated part by part. The seeding spreads its picks across that list so the opening stretch of a
        /// run is not the same on every map the registry starts with.
        /// </summary>
        [Test]
        public void SeedingDoesNotOpenEveryMapOnTheSamePart()
        {
            MapRegistryState state = Seeded();

            List<CoursePartId> openingParts = new List<CoursePartId>();
            foreach (MapRegistryEntry entry in state.Maps)
            {
                Assert.That(CourseComposer.TryParseMapId(entry.MapId, out CoursePartId[] parts), Is.True);
                openingParts.Add(parts[0]);
            }

            Assert.That(openingParts.Distinct().Count(), Is.GreaterThan(1), "every published map opens on the same part");
        }

        [Test]
        public void RotationIsDueOnceThePeriodHasPassed()
        {
            MapRegistryState state = Seeded();

            Assert.That(state.IsRotationDue(Now, Period), Is.False);
            Assert.That(state.IsRotationDue(Now + Period - MetaDuration.FromMilliseconds(1), Period), Is.False);
            Assert.That(state.IsRotationDue(Now + Period, Period), Is.True);
            Assert.That(state.IsRotationDue(Now + Period + Period, Period), Is.True);
        }

        [Test]
        public void ARotationSwapsOneMapAndKeepsTheSetFull()
        {
            MapRegistryState state = Seeded();

            List<CourseId> before        = new List<CourseId>(state.ActiveMapIds);
            int            mapsBefore    = state.Maps.Count;
            CourseId       shouldLeave   = ExpectedOutgoing(state);
            MetaTime       rotatedAt     = Now + Period;

            (CourseId outgoing, CourseId incoming) = state.Rotate(rotatedAt, _gameConfig);

            Assert.That(incoming, Is.Not.Null, "a rotation with maps left to draw from must bring one in");
            Assert.That(outgoing, Is.EqualTo(shouldLeave), "the map that has been active longest is the one that leaves");
            Assert.That(incoming, Is.Not.EqualTo(outgoing), "a map must not leave the set and walk straight back into it");

            Assert.That(state.ActiveMapIds.Count, Is.EqualTo(MapRegistryState.ActiveSetSize));
            Assert.That(state.ActiveMapIds.Distinct().Count(), Is.EqualTo(state.ActiveMapIds.Count));
            Assert.That(state.ActiveMapIds, Does.Not.Contain(outgoing));
            Assert.That(state.ActiveMapIds, Does.Contain(incoming));

            Assert.That(before.Count(mapId => !state.ActiveMapIds.Contains(mapId)), Is.EqualTo(1), "exactly one map left the set");
            Assert.That(state.Maps.Count, Is.GreaterThanOrEqualTo(mapsBefore), "the registry never loses a map");
            foreach (CourseId mapId in before)
                Assert.That(state.TryGetEntry(mapId), Is.Not.Null, $"{mapId} was dropped from the registry when it left the active set");

            Assert.That(state.LastRotatedAt, Is.EqualTo(rotatedAt));
            Assert.That(state.TryGetEntry(incoming).LastActivatedAt, Is.EqualTo(rotatedAt));
        }

        /// <summary>
        /// A map that comes back comes back as itself: the same entry, so its recordings, its par and its
        /// spread of times (all of which are found by its id) are the ones it already had.
        /// </summary>
        [Test]
        public void AReturningMapKeepsItsEntry()
        {
            MapRegistryState state = Seeded();
            MetaTime         at    = Now;

            for (int ndx = 0; ndx < 40; ndx++)
            {
                Dictionary<CourseId, (MetaTime PublishedAt, int TimesActivated)> before = new Dictionary<CourseId, (MetaTime, int)>();
                foreach (MapRegistryEntry entry in state.Maps)
                    before[entry.MapId] = (entry.PublishedAt, entry.TimesActivated);

                at = at + Period;
                (CourseId _, CourseId incoming) = state.Rotate(at, _gameConfig);
                Assert.That(incoming, Is.Not.Null);

                if (!before.TryGetValue(incoming, out (MetaTime PublishedAt, int TimesActivated) was))
                    continue;

                MapRegistryEntry returned = state.TryGetEntry(incoming);
                Assert.That(returned.PublishedAt, Is.EqualTo(was.PublishedAt), "a returning map was republished rather than reactivated");
                Assert.That(returned.TimesActivated, Is.EqualTo(was.TimesActivated + 1), "a returning map must count the visit");
                return;
            }

            Assert.Fail("no map came back to the active set in 40 rotations");
        }

        /// <summary>
        /// The set has to survive being rotated for as long as the server runs: never two of the same map in
        /// front of a player, never a map the runners cannot finish, and never a published map forgotten.
        /// </summary>
        [Test]
        public void RotatingForeverKeepsTheSetDistinctAndPlayable()
        {
            MapRegistryState state = Seeded();
            MetaTime         at    = Now;

            for (int ndx = 0; ndx < 40; ndx++)
            {
                int mapsBefore = state.Maps.Count;

                at = at + Period;
                (CourseId _, CourseId incoming) = state.Rotate(at, _gameConfig);

                Assert.That(incoming, Is.Not.Null, $"rotation {ndx} found nothing to bring in");
                Assert.That(MapRegistryState.PassesPublicationGate(incoming, _gameConfig), Is.True, $"{incoming} entered the active set without being finishable");

                Assert.That(state.ActiveMapIds.Count, Is.EqualTo(MapRegistryState.ActiveSetSize), $"rotation {ndx} left the active set short");
                Assert.That(state.ActiveMapIds.Distinct().Count(), Is.EqualTo(state.ActiveMapIds.Count), $"rotation {ndx} activated a map twice");
                Assert.That(state.Maps.Count, Is.GreaterThanOrEqualTo(mapsBefore), $"rotation {ndx} lost a published map");
                Assert.That(state.Maps.Select(entry => entry.MapId).Distinct().Count(), Is.EqualTo(state.Maps.Count), $"rotation {ndx} published a map twice");
            }
        }

        /// <summary>
        /// Rotation is a pure function of the state and the moment. A server that restarts mid-rotation and
        /// rotates again from the same persisted state must arrive at the same active set, or a ticket issued
        /// against the set before the restart names a map nobody else is on.
        /// </summary>
        [Test]
        public void TheSameStateRotatedAtTheSameMomentPicksTheSameMap()
        {
            MapRegistryState one     = Seeded();
            MapRegistryState another = Seeded();
            MetaTime         at      = Now + Period;

            (CourseId outgoingOne, CourseId incomingOne)         = one.Rotate(at, _gameConfig);
            (CourseId outgoingAnother, CourseId incomingAnother) = another.Rotate(at, _gameConfig);

            Assert.That(incomingOne, Is.EqualTo(incomingAnother));
            Assert.That(outgoingOne, Is.EqualTo(outgoingAnother));
            Assert.That(one.ActiveMapIds, Is.EqualTo(another.ActiveMapIds));
            Assert.That(one.Maps.Select(entry => entry.MapId), Is.EqualTo(another.Maps.Select(entry => entry.MapId)));
        }

        /// <summary>
        /// A config publish can retire a part, and an active map naming it composes into nothing. The
        /// reconciliation is what keeps that out of a player's hands: the map leaves the set and one that still
        /// composes takes its place.
        /// </summary>
        [Test]
        public void ReconcileDropsAMapThisConfigCannotComposeAndRefills()
        {
            MapRegistryState state = Seeded();
            CourseId         gone  = CourseId.FromString("no-such+no-such+no-such");

            state.ActiveMapIds[0] = gone;

            MetaTime at      = Now + MetaDuration.FromMinutes(1);
            int      changed = state.Reconcile(at, _gameConfig);

            Assert.That(changed, Is.EqualTo(2), "one map was dropped and one took its place");
            Assert.That(state.ActiveMapIds, Does.Not.Contain(gone));
            Assert.That(state.ActiveMapIds.Count, Is.EqualTo(MapRegistryState.ActiveSetSize));
            Assert.That(state.ActiveMapIds.Distinct().Count(), Is.EqualTo(state.ActiveMapIds.Count));

            foreach (CourseId mapId in state.ActiveMapIds)
                Assert.That(MapRegistryState.IsComposable(mapId, _gameConfig), Is.True, $"{mapId} survived reconciliation but does not compose");
        }

        [Test]
        public void ReconcileLeavesAHealthySetAlone()
        {
            MapRegistryState state = Seeded();
            List<CourseId>   before = new List<CourseId>(state.ActiveMapIds);

            Assert.That(state.Reconcile(Now + MetaDuration.FromMinutes(1), _gameConfig), Is.Zero);
            Assert.That(state.ActiveMapIds, Is.EqualTo(before));
        }

        /// <summary>
        /// The gate is what stands between a composition and a player. Every map these parts legally compose
        /// has to pass it, or the generator is producing maps nobody can be given; anything that is not a map
        /// of this config has to fail it, whatever it looks like.
        /// </summary>
        [Test]
        public void ThePublicationGateAcceptsEveryLegalMapAndNothingElse()
        {
            foreach (CourseId mapId in CourseComposer.LegalMapIds(_gameConfig))
                Assert.That(MapRegistryState.PassesPublicationGate(mapId, _gameConfig), Is.True, $"{mapId} is a legal map the scripted runners cannot finish");

            CourseId sample = CourseComposer.LegalMapIds(_gameConfig).First();
            Assert.That(CourseComposer.TryParseMapId(sample, out CoursePartId[] parts), Is.True);

            Assert.That(MapRegistryState.PassesPublicationGate(CourseId.FromString("not-a-map"), _gameConfig), Is.False, "a course id that is not a triplet is not a map");
            Assert.That(MapRegistryState.PassesPublicationGate(CourseId.FromString($"{parts[0]}{CourseComposer.PartSeparator}{parts[1]}"), _gameConfig), Is.False, "two parts are not a map");
            Assert.That(MapRegistryState.PassesPublicationGate(CourseId.FromString("no-such+no-such+no-such"), _gameConfig), Is.False, "a triplet of parts this config does not hold is not a map");
            Assert.That(MapRegistryState.PassesPublicationGate(CourseComposer.MapIdFor(parts[0], parts[0], parts[1]), _gameConfig), Is.False, "a map takes three different parts");
            Assert.That(MapRegistryState.PassesPublicationGate(null, _gameConfig), Is.False);
        }
    }
}
