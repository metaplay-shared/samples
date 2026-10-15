using Metaplay.Core;
using Metaplay.Core.Math;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Execution of <see cref="PlayerAction"/>s against the game-specific <see cref="PlayerModel"/>, without
    /// needing to run the server.
    /// </summary>
    [TestFixture]
    public class PlayerActionTests
    {
        /// <summary> Game config loaded as fixture setup. </summary>
        static SharedGameConfig _gameConfig;

        /// <summary>Fixture setup to load the game config from disk.</summary>
        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        #region Helpers

        /// <summary>A new <see cref="PlayerModel"/> in its initial state, after its initial login.</summary>
        PlayerModel CreatePlayerModel()
        {
            PlayerModel playerModel = PlayerModelUtil.CreateNewPlayerModel<PlayerModel>(
                MetaTime.FromDateTime(new DateTime(2021, 5, 30, 19, 57, 17, DateTimeKind.Utc)),
                _gameConfig,
                playerId: EntityId.CreateRandom(EntityKindCore.Player),
                name: "Example name");

            playerModel.LogicVersion = IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options.SupportedLogicVersions.MaxVersion;

            // Simulate initial login to initialize resources
            playerModel.OnInitialLogin();

            return playerModel;
        }

        /// <summary>
        /// Helper to execute a <see cref="PlayerAction"/> against the specified <see cref="PlayerModel"/>.
        /// </summary>
        /// <param name="playerModel">Model to execute the action against</param>
        /// <param name="action">Player action to execute</param>
        /// <returns>Result of the execution</returns>
        MetaActionResult ExecuteAction(PlayerModel playerModel, ModelAction action)
        {
            MetaActionResult result = action.InvokeExecute(playerModel, commit: true);
            return result;
        }

        /// <summary>
        /// Progress the given <paramref name="playerModel"/> by <paramref name="numTicks"/> ticks.
        /// </summary>
        /// <param name="playerModel">Model to execute ticks for</param>
        /// <param name="numTicks">Number of ticks to execute</param>
        void ExecuteTicks(PlayerModel playerModel, int numTicks)
        {
            for (int ndx = 0; ndx < numTicks; ndx++)
                playerModel.Tick(NullChecksumEvaluator.Context);
        }

        /// <summary>A map the published parts compose, which is what the server picks a run's course from.</summary>
        CourseId SomeMapId() => CourseComposer.LegalMapIds(_gameConfig)[0];

        /// <summary>Open a run the way the server does: on a named map, with the field it selected for it.</summary>
        void OpenRun(PlayerModel playerModel, CourseId courseId, List<TimeTrialGhost> lineup = null)
            => ExecuteAction(playerModel, new PlayerOpenTimeTrial(courseId, lineup));

        #endregion // Helpers

        #region Test cases

        [Test]
        public void PlayerOpenTimeTrial_IssuesATicketOnTheMapTheServerPicked()
        {
            PlayerModel playerModel = CreatePlayerModel();
            CourseId    mapId       = SomeMapId();
            Assert.That(playerModel.ActiveTimeTrial, Is.Null);

            OpenRun(playerModel, mapId);

            TimeTrialTicket ticket = playerModel.ActiveTimeTrial;
            Assert.That(ticket, Is.Not.Null);
            Assert.That(ticket.TicketId, Is.EqualTo(1));
            Assert.That(ticket.CourseId, Is.EqualTo(mapId));
            Assert.That(ticket.RulesetVersion, Is.EqualTo(TimeTrialConstants.RulesetVersion));
            Assert.That(ticket.IssuedAt, Is.EqualTo(playerModel.CurrentTime));
            Assert.That(playerModel.LastTimeTrialCourseId, Is.EqualTo(mapId), "the map is remembered, so the next pick can avoid repeating it");
        }

        /// <summary>The field the server selected is bound to the ticket it arrives with.</summary>
        [Test]
        public void PlayerOpenTimeTrial_BindsTheFieldToTheTicketItOpens()
        {
            PlayerModel playerModel = CreatePlayerModel();
            CourseId    mapId       = SomeMapId();

            CourseDefinition map          = CourseResolver.TryResolve(mapId, _gameConfig);
            int              segmentCount = TimeTrialSegments.CountFor(map.Climbs.Count);
            TimeTrialGhost   opponent     = TimeTrialRuns.GhostOf(mapId, "Slowcoach", TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 30_000), climbMs: 5_000));

            OpenRun(playerModel, mapId, new List<TimeTrialGhost> { opponent });

            Assert.That(playerModel.ActiveLineup, Is.Not.Null);
            Assert.That(playerModel.ActiveLineup.TicketId, Is.EqualTo(playerModel.ActiveTimeTrial.TicketId));
            Assert.That(playerModel.ActiveLineup.Opponents, Has.Count.EqualTo(1));
        }

        [Test]
        public void PlayerOpenTimeTrial_RefusesAMapNothingResolves()
        {
            PlayerModel playerModel = CreatePlayerModel();

            Assert.That(
                ExecuteAction(playerModel, new PlayerOpenTimeTrial(CourseId.FromString("no-such-map"), lineup: null)),
                Is.EqualTo(ActionResults.NoSuchCourse));
            Assert.That(playerModel.ActiveTimeTrial, Is.Null);
        }

        [Test]
        public void PlayerOpenTimeTrial_ReplacesAnAttemptThePlayerWalkedAwayFrom()
        {
            PlayerModel playerModel = CreatePlayerModel();
            CourseId    mapId       = SomeMapId();

            OpenRun(playerModel, mapId);
            ExecuteTicks(playerModel, PlayerModel.TicksPerSecond * 5);
            OpenRun(playerModel, mapId);

            Assert.That(playerModel.ActiveTimeTrial.TicketId, Is.EqualTo(2), "the abandoned attempt is replaced, not queued behind");
            Assert.That(playerModel.NextTimeTrialTicketId, Is.EqualTo(2));
        }

        /// <summary>
        /// Offline practice names its own course: a map composed from the published parts. It is raced against
        /// nobody.
        /// </summary>
        [Test]
        public void PlayerStartPracticeTimeTrial_OpensAMapWithNoField()
        {
            CourseId    courseId    = SomeMapId();
            PlayerModel playerModel = CreatePlayerModel();

            Assert.That(ExecuteAction(playerModel, new PlayerStartPracticeTimeTrial(courseId)), Is.EqualTo(MetaActionResult.Success), $"{courseId} did not open");
            Assert.That(playerModel.ActiveTimeTrial.CourseId, Is.EqualTo(courseId));
            Assert.That(playerModel.ActiveLineup, Is.Null, $"{courseId}: practice is raced against nobody");
            Assert.That(playerModel.LastTimeTrialCourseId, Is.EqualTo(courseId));
        }

        [Test]
        public void PlayerStartPracticeTimeTrial_RefusesACourseNothingResolves()
        {
            PlayerModel playerModel = CreatePlayerModel();

            Assert.That(
                ExecuteAction(playerModel, new PlayerStartPracticeTimeTrial(CourseId.FromString("no-such-course"))),
                Is.EqualTo(ActionResults.NoSuchCourse));
            Assert.That(playerModel.ActiveTimeTrial, Is.Null);
        }

        [Test]
        public void PlayerAcceptTimeTrialResult_RecordsTheTimeAndClosesTheAttempt()
        {
            PlayerModel playerModel = CreatePlayerModel();
            OpenRun(playerModel, SomeMapId());

            CourseId courseId = playerModel.ActiveTimeTrial.CourseId;
            int      ticketId = playerModel.ActiveTimeTrial.TicketId;

            MetaActionResult result = ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(ticketId, new TimeTrialResult(29_400, 1764), standing: null));

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.That(playerModel.ActiveTimeTrial, Is.Null, "an accepted attempt is used up");
            Assert.That(playerModel.GetBestResult(courseId).FinishTimeMs, Is.EqualTo(29_400));
            Assert.That(playerModel.CompletedRunCounts.GetValueOrDefault(courseId), Is.EqualTo(1));
        }

        /// <summary>
        /// A new player has completed no run until a ranked run is accepted. A practice or rejected run leaves it
        /// unset.
        /// </summary>
        [Test]
        public void HasCompletedARun_FlipsWhenAResultIsAccepted()
        {
            PlayerModel playerModel = CreatePlayerModel();
            Assert.That(playerModel.HasCompletedARun, Is.False, "a new player has completed nothing");

            ExecuteAction(playerModel, new PlayerStartPracticeTimeTrial(SomeMapId()));
            ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(playerModel.ActiveTimeTrial.TicketId, new TimeTrialResult(29_400, 1764), standing: null));
            Assert.That(playerModel.HasCompletedARun, Is.False, "a practice run is not a completed run");

            OpenRun(playerModel, SomeMapId());
            ExecuteAction(playerModel, new PlayerRejectTimeTrialResult(playerModel.ActiveTimeTrial.TicketId, TimeTrialRejectReason.DidNotFinish));
            Assert.That(playerModel.HasCompletedARun, Is.False, "a rejected run is not a completed run");

            OpenRun(playerModel, SomeMapId());
            ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(playerModel.ActiveTimeTrial.TicketId, new TimeTrialResult(29_400, 1764), standing: null));
            Assert.That(playerModel.HasCompletedARun, Is.True);
        }

        [Test]
        public void PlayerAcceptTimeTrialResult_KeepsTheFasterTime()
        {
            PlayerModel playerModel = CreatePlayerModel();

            OpenRun(playerModel, SomeMapId());
            CourseId courseId = playerModel.ActiveTimeTrial.CourseId;
            ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(playerModel.ActiveTimeTrial.TicketId, new TimeTrialResult(29_400, 1764), standing: null));

            OpenRun(playerModel, SomeMapId());
            ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(playerModel.ActiveTimeTrial.TicketId, new TimeTrialResult(31_000, 1860), standing: null));
            Assert.That(playerModel.GetBestResult(courseId).FinishTimeMs, Is.EqualTo(29_400), "a slower run does not replace a personal best");

            OpenRun(playerModel, SomeMapId());
            ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(playerModel.ActiveTimeTrial.TicketId, new TimeTrialResult(29_400, 1764), standing: null));
            Assert.That(playerModel.GetBestResult(courseId).FinishTimeMs, Is.EqualTo(29_400), "an equal time is a tie, so the earlier one stands");

            OpenRun(playerModel, SomeMapId());
            ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(playerModel.ActiveTimeTrial.TicketId, new TimeTrialResult(28_100, 1686), standing: null));
            Assert.That(playerModel.GetBestResult(courseId).FinishTimeMs, Is.EqualTo(28_100));

            Assert.That(playerModel.CompletedRunCounts.GetValueOrDefault(courseId), Is.EqualTo(4), "every accepted run counts as a completion");
        }

        [Test]
        public void PlayerAcceptTimeTrialResult_FailsForAnAttemptThePlayerDoesNotHaveOpen()
        {
            PlayerModel playerModel = CreatePlayerModel();

            Assert.That(
                ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(1, new TimeTrialResult(29_400, 1764), standing: null)),
                Is.EqualTo(ActionResults.NoActiveTimeTrial));

            OpenRun(playerModel, SomeMapId());
            Assert.That(
                ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(99, new TimeTrialResult(29_400, 1764), standing: null)),
                Is.EqualTo(ActionResults.TimeTrialTicketMismatch));

            Assert.That(playerModel.BestResults, Is.Empty);
            Assert.That(playerModel.ActiveTimeTrial, Is.Not.Null, "a result for some other attempt must not consume this one");
        }

        [Test]
        public void PlayerRejectTimeTrialResult_ClosesTheAttempt()
        {
            PlayerModel playerModel = CreatePlayerModel();
            OpenRun(playerModel, SomeMapId());
            int ticketId = playerModel.ActiveTimeTrial.TicketId;

            ExecuteAction(playerModel, new PlayerRejectTimeTrialResult(ticketId, TimeTrialRejectReason.DidNotFinish));

            Assert.That(playerModel.ActiveTimeTrial, Is.Null);
            Assert.That(playerModel.BestResults, Is.Empty);
        }

        [Test]
        public void PlayerRejectTimeTrialResult_LeavesANewerAttemptAlone()
        {
            // A late submission for an attempt the player already replaced is turned down, but the run they are
            // in the middle of has to survive it.
            PlayerModel playerModel = CreatePlayerModel();
            OpenRun(playerModel, SomeMapId());
            int abandonedTicketId = playerModel.ActiveTimeTrial.TicketId;
            OpenRun(playerModel, SomeMapId());

            ExecuteAction(playerModel, new PlayerRejectTimeTrialResult(abandonedTicketId, TimeTrialRejectReason.TicketMismatch));

            Assert.That(playerModel.ActiveTimeTrial, Is.Not.Null);
            Assert.That(playerModel.ActiveTimeTrial.TicketId, Is.EqualTo(abandonedTicketId + 1));
        }

        /// <summary>
        /// A segment ends at a climb's bottom torii and the next starts at the top one, so the climb between
        /// them is placed against nobody. An opponent who is level on every segment but slower over every climb
        /// ties the player everywhere, and one who is quicker over the opening segment alone takes only that
        /// segment's placement.
        /// </summary>
        [Test]
        public void PlayerAcceptTimeTrialResult_PlacesSegmentsWithoutTheClimbsBetweenThem()
        {
            PlayerModel playerModel = CreatePlayerModel();
            OpenRun(playerModel, SomeMapId());

            TimeTrialTicket  ticket       = playerModel.ActiveTimeTrial;
            CourseDefinition course       = CourseResolver.TryResolve(ticket.CourseId, _gameConfig);
            int              segmentCount = TimeTrialSegments.CountFor(course.Climbs.Count);
            Assert.That(segmentCount, Is.GreaterThan(1), "the map has to have a climb for this to say anything");

            TimeTrialResult mine = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 20_000), climbMs: 5_000);

            // Level on every segment, but twice as long between every pair of torii.
            TimeTrialResult slowClimber = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 20_000), climbMs: 10_000);

            // Quicker over the opening segment and slower over each of the rest.
            List<int> fastStarter = TimeTrialRuns.EvenSegments(segmentCount, 25_000);
            fastStarter[0] = 15_000;

            playerModel.SetActiveLineup(ticket.TicketId, new List<TimeTrialGhost>
            {
                TimeTrialRuns.GhostOf(course.Id, "Slow Climber", slowClimber),
                TimeTrialRuns.GhostOf(course.Id, "Fast Starter", TimeTrialRuns.ResultOf(fastStarter, climbMs: 5_000)),
            });

            ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(ticket.TicketId, mine, standing: null));

            Assert.That(playerModel.LastSegmentPlacements, Has.Count.EqualTo(segmentCount));
            Assert.That(playerModel.LastSegmentPlacements[0], Is.EqualTo(2), "only the runner who was quicker over the opening segment is ahead there");
            for (int segment = 1; segment < segmentCount; segment++)
            {
                Assert.That(playerModel.LastSegmentPlacements[segment], Is.EqualTo(1),
                    $"segment {segment + 1} was a tie the player shares the better placement of, and the slower climb is nobody's to lose");
            }
        }

        /// <summary>
        /// A run against an opponent recorded without both torii of every climb is placed nowhere and pays
        /// nothing.
        /// </summary>
        [Test]
        public void PlayerAcceptTimeTrialResult_PaysNothingForALineupMissingItsToriiTimes()
        {
            PlayerModel playerModel = CreatePlayerModel();
            OpenRun(playerModel, SomeMapId());

            TimeTrialTicket  ticket       = playerModel.ActiveTimeTrial;
            CourseDefinition course       = CourseResolver.TryResolve(ticket.CourseId, _gameConfig);
            int              segmentCount = TimeTrialSegments.CountFor(course.Climbs.Count);

            TimeTrialResult mine    = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 20_000), climbMs: 5_000);
            TimeTrialResult slower  = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 30_000), climbMs: 5_000);

            playerModel.SetActiveLineup(ticket.TicketId, new List<TimeTrialGhost>
            {
                new TimeTrialGhost(
                    course.Id,
                    TimeTrialConstants.RulesetVersion,
                    new TimeTrialRecording(),
                    "Gates Only",
                    slower.FinishTimeMs,
                    TimeTrialGhostKind.Player,
                    slower.GateTimesMs),
            });

            ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(ticket.TicketId, mine, standing: null));

            Assert.That(playerModel.LastSegmentPlacements, Is.All.Zero, "a run with nothing comparable to race is placed nowhere");
            Assert.That(playerModel.LastOrbsEarned, Is.Zero);
            Assert.That(playerModel.Orbs, Is.Zero);
        }

        /// <summary>
        /// The active lineup is part of the model checksum, so a client racing a different field from the
        /// server's is caught as a desync.
        /// </summary>
        [Test]
        public void ActiveLineup_IsPartOfTheModelChecksum()
        {
            PlayerModel playerModel = CreatePlayerModel();
            OpenRun(playerModel, SomeMapId());

            TimeTrialTicket  ticket = playerModel.ActiveTimeTrial;
            CourseDefinition course = CourseResolver.TryResolve(ticket.CourseId, _gameConfig);
            TimeTrialResult  result = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(TimeTrialSegments.CountFor(course.Climbs.Count), 20_000), climbMs: 5_000);

            FlatIOBuffer buffer = new FlatIOBuffer();
            playerModel.SetActiveLineup(ticket.TicketId, new List<TimeTrialGhost>());
            uint withoutOpponent = JournalUtil.ComputeChecksum(buffer, playerModel);

            playerModel.SetActiveLineup(ticket.TicketId, new List<TimeTrialGhost> { TimeTrialRuns.GhostOf(course.Id, "Rival", result) });
            uint withOpponent = JournalUtil.ComputeChecksum(buffer, playerModel);

            Assert.That(withOpponent, Is.Not.EqualTo(withoutOpponent));
        }

        /// <summary>
        /// Practice is a property of the ticket: the practice action always issues a practice ticket, and the
        /// open action issues one when the server opens a developer's race.
        /// </summary>
        [Test]
        public void APracticeTicketCarriesItsFlag()
        {
            PlayerModel practiced = CreatePlayerModel();
            ExecuteAction(practiced, new PlayerStartPracticeTimeTrial(SomeMapId()));
            Assert.That(practiced.ActiveTimeTrial.IsPractice, Is.True, "the practice action issued a ranked ticket");

            PlayerModel raced = CreatePlayerModel();
            CourseId    mapId = SomeMapId();
            CourseDefinition map = CourseResolver.TryResolve(mapId, _gameConfig);
            ExecuteAction(raced, new PlayerOpenTimeTrial(mapId, TimeTrialSeedRunners.Lineup(map, _gameConfig.TimeTrialRules), isPractice: true));

            Assert.That(raced.ActiveTimeTrial.IsPractice, Is.True, "a developer's race issued a ranked ticket");
            Assert.That(raced.ActiveLineup, Is.Not.Null, "a developer's race is run against the field it was sent");
            Assert.That(raced.ActiveLineup.Opponents, Has.Count.EqualTo(3));
        }

        /// <summary>
        /// Accepting a practice result closes the attempt and leaves every recorded thing as the last counted run
        /// left it: best times, run counts, the pool best, the Orb balance, the last placements and payout.
        /// </summary>
        [Test]
        public void PlayerAcceptTimeTrialResult_ForAPracticeTicket_WritesNothing()
        {
            PlayerModel playerModel = CreatePlayerModel();
            CourseId    mapId       = SomeMapId();
            CourseDefinition map    = CourseResolver.TryResolve(mapId, _gameConfig);
            int segmentCount        = TimeTrialSegments.CountFor(map.Climbs.Count);

            // One counted run first, placed against a slower field, so there is recorded state to leave alone.
            TimeTrialGhost slower = TimeTrialRuns.GhostOf(mapId, "Slowcoach", TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 30_000), climbMs: 5_000));
            OpenRun(playerModel, mapId, new List<TimeTrialGhost> { slower });
            TimeTrialResult counted = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 25_000), climbMs: 5_000);
            int[] field = new int[CourseTimeBuckets.BucketCount];
            field[3] = 5;
            field[8] = 7;
            ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(playerModel.ActiveTimeTrial.TicketId, counted, new TimeTrialStanding(mapId, counted.FinishTimeMs, 40_000, field)));

            int       orbsBefore      = playerModel.Orbs;
            List<int> placementsBefore = new List<int>(playerModel.LastSegmentPlacements);
            int       paidBefore       = playerModel.LastOrbsEarned;
            int       poolBestBefore   = playerModel.GetPoolBestTimeMs(mapId);
            Assert.That(orbsBefore, Is.GreaterThan(0), "the counted run has to pay something for this to say anything");

            // Then a practice race that is faster everywhere, against the same slower field.
            ExecuteAction(playerModel, new PlayerOpenTimeTrial(mapId, new List<TimeTrialGhost> { slower }, isPractice: true));
            int practiceTicketId = playerModel.ActiveTimeTrial.TicketId;
            TimeTrialResult faster = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 10_000), climbMs: 5_000);

            MetaActionResult result = ExecuteAction(playerModel, new PlayerAcceptTimeTrialResult(practiceTicketId, faster, new TimeTrialStanding(mapId, faster.FinishTimeMs, 40_000)));

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.Multiple(() =>
            {
                Assert.That(playerModel.ActiveTimeTrial, Is.Null, "the practice attempt is still open");
                Assert.That(playerModel.ActiveLineup, Is.Null, "the practice field outlived its attempt");
                Assert.That(playerModel.GetBestResult(mapId).FinishTimeMs, Is.EqualTo(counted.FinishTimeMs), "practice wrote a best time");
                Assert.That(playerModel.CompletedRunCounts.GetValueOrDefault(mapId), Is.EqualTo(1), "practice counted as a completed run");
                Assert.That(playerModel.GetPoolBestTimeMs(mapId), Is.EqualTo(poolBestBefore), "practice wrote a pool best");
                Assert.That(playerModel.Orbs, Is.EqualTo(orbsBefore), "practice paid Orbs");
                Assert.That(playerModel.LastOrbsEarned, Is.EqualTo(paidBefore), "practice replaced the last payout");
                Assert.That(playerModel.LastSegmentPlacements, Is.EqualTo(placementsBefore), "practice replaced the last placements");
            });
        }

        /// <summary>
        /// Developer actions are allowed for a developer player or on a server with its development features
        /// on, and for nobody else.
        /// </summary>
        [Test]
        public void DeveloperActionsNeedADeveloperOrADevelopmentServer()
        {
            PlayerModel playerModel = CreatePlayerModel();
            Assert.That(playerModel.AllowsDeveloperActions, Is.False);

            playerModel.ServerDevelopmentFeaturesEnabled = true;
            Assert.That(playerModel.AllowsDeveloperActions, Is.True);

            playerModel.ServerDevelopmentFeaturesEnabled = false;
            playerModel.IsDeveloper = true;
            Assert.That(playerModel.AllowsDeveloperActions, Is.True);
        }

        #endregion // Test cases

        #region A run on a part editor draft

        /// <summary>A part editor draft: a published part's geometry under an id the config does not hold.</summary>
        DraftCoursePart SomeDraft(string id = "part-editor-draft")
        {
            CoursePartInfo source  = _gameConfig.CourseParts.Values.OrderBy(part => part.Id.Value, StringComparer.Ordinal).First();
            CoursePartInfo renamed = new CoursePartInfo(CoursePartId.FromString(id), source.Ground, source.Hazards, source.Positions, source.IsSetPiece, source.Bridges);
            Assert.That(_gameConfig.CourseParts.ContainsKey(renamed.Id), Is.False, "the draft's id has to be one the config does not hold");
            return new DraftCoursePart(renamed);
        }

        /// <summary>A part with the given hazard beds on one long stretch of ground, sound by every rule of a part.</summary>
        static CoursePartInfo PartWithBeds(string id, List<HazardSpan> beds, int lengthMetres)
            => new CoursePartInfo(
                CoursePartId.FromString(id),
                new List<GroundSegment> { new GroundSegment(F32.Zero, F32.FromInt(lengthMetres), F32.Zero) },
                beds,
                new List<CoursePartPosition> { CoursePartPosition.First },
                isSetPiece: false);

        /// <summary>A course as comparable text: everything the composer decided, and nothing else.</summary>
        static string Describe(CourseDefinition course)
        {
            List<string> lines = new List<string> { $"{course.Id} start {course.StartX} finish {course.FinishX}" };
            foreach (GroundSegment segment in course.Ground)
                lines.Add($"ground {segment.StartX} {segment.EndX} {segment.TopY}");
            foreach (HazardSpan hazard in course.Hazards)
                lines.Add($"spikes {hazard.StartX} {hazard.EndX}");
            foreach (ClimbDefinition climb in course.Climbs)
                lines.Add($"climb {climb.Id} {climb.BottomX} {climb.TopX} {climb.TopY} {climb.GateX}");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// A run on a draft is a practice attempt carrying the draft itself, and its ticket resolves to the draft
        /// composed alone.
        /// </summary>
        [Test]
        public void PlayerStartDraftPartTimeTrial_IssuesAPracticeTicketCarryingTheDraft()
        {
            PlayerModel     playerModel = CreatePlayerModel();
            DraftCoursePart draft       = SomeDraft();

            MetaActionResult result = ExecuteAction(playerModel, new PlayerStartDraftPartTimeTrial(draft));

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            TimeTrialTicket ticket = playerModel.ActiveTimeTrial;
            Assert.That(ticket, Is.Not.Null, "no attempt was opened");
            Assert.That(ticket.IsPractice, Is.True, "a run on a draft issued a ranked ticket");
            Assert.That(ticket.DraftPart, Is.SameAs(draft), "the ticket does not carry the draft");
            Assert.That(ticket.CourseId, Is.EqualTo(CourseId.FromString(draft.Id.Value)), "the ticket is not named by the draft's id");
            Assert.That(playerModel.ActiveLineup, Is.Null, "a draft is run against nobody");

            Assert.That(CourseResolver.TryResolve(ticket.CourseId, _gameConfig), Is.Null, "a draft's id resolved on its own");
            CourseDefinition course = CourseResolver.TryResolve(ticket, _gameConfig);
            Assert.That(course, Is.Not.Null, "the ticket did not resolve to the draft");
            Assert.That(Describe(course), Is.EqualTo(Describe(CourseInspection.PartAlone(draft.ToPart(), _gameConfig.Global))),
                "the ticket resolved to something other than the draft composed alone");
            Assert.That(course.Climbs, Is.Empty, "a draft alone has no climbs");
        }

        /// <summary>
        /// A draft that breaks one of a part's rules is turned down with no attempt opened, and so is no draft
        /// at all.
        /// </summary>
        [Test]
        public void PlayerStartDraftPartTimeTrial_RefusesADraftThatBreaksARule()
        {
            PlayerModel playerModel = CreatePlayerModel();

            // Ground beginning past local X=0 breaks the part's rules.
            CoursePartInfo broken = new CoursePartInfo(
                CoursePartId.FromString("broken-draft"),
                new List<GroundSegment> { new GroundSegment(F32.FromInt(2), F32.FromInt(30), F32.Zero) },
                new List<HazardSpan>(),
                new List<CoursePartPosition> { CoursePartPosition.First },
                isSetPiece: false);
            Assert.That(broken.Validate(), Is.Not.Empty, "the part has to break a rule for this to say anything");

            Assert.That(ExecuteAction(playerModel, new PlayerStartDraftPartTimeTrial(new DraftCoursePart(broken))), Is.EqualTo(ActionResults.InvalidDraftPart));
            Assert.That(ExecuteAction(playerModel, new PlayerStartDraftPartTimeTrial(null)), Is.EqualTo(ActionResults.InvalidDraftPart));
            Assert.That(playerModel.ActiveTimeTrial, Is.Null, "a refused draft opened an attempt");
        }

        /// <summary>
        /// A sound draft with more rows of one kind than <see cref="DraftCoursePart.MaxRowsPerList"/> is turned
        /// down, and one at the cap is accepted.
        /// </summary>
        [Test]
        public void PlayerStartDraftPartTimeTrial_RefusesADraftWithMoreRowsThanAPartCould()
        {
            PlayerModel playerModel = CreatePlayerModel();

            List<HazardSpan> beds = new List<HazardSpan>();
            for (int ndx = 0; ndx <= DraftCoursePart.MaxRowsPerList; ndx++)
                beds.Add(new HazardSpan(F32.FromInt(5 + 2 * ndx), F32.FromInt(6 + 2 * ndx)));
            CoursePartInfo crowded = PartWithBeds("crowded-draft", beds, lengthMetres: 300);
            Assert.That(crowded.Validate(), Is.Empty, "the part has to be sound, so that the cap is what refuses it");

            DraftCoursePart draft = new DraftCoursePart(crowded);
            Assert.That(draft.IsWithinBounds(), Is.False);
            Assert.That(ExecuteAction(playerModel, new PlayerStartDraftPartTimeTrial(draft)), Is.EqualTo(ActionResults.InvalidDraftPart));
            Assert.That(playerModel.ActiveTimeTrial, Is.Null);

            beds.RemoveAt(beds.Count - 1);
            DraftCoursePart withinBounds = new DraftCoursePart(PartWithBeds("crowded-draft", beds, lengthMetres: 300));
            Assert.That(withinBounds.IsWithinBounds(), Is.True);
            Assert.That(ExecuteAction(playerModel, new PlayerStartDraftPartTimeTrial(withinBounds)), Is.EqualTo(MetaActionResult.Success), "a draft at the cap was refused");
        }

        /// <summary>
        /// A draft ticket serialized and read back still carries its draft by value and resolves to the same
        /// course.
        /// </summary>
        [Test]
        public void ADraftTicket_CarriesItsDraftByValueThroughSerialization()
        {
            PlayerModel playerModel = CreatePlayerModel();
            ExecuteAction(playerModel, new PlayerStartDraftPartTimeTrial(SomeDraft()));
            TimeTrialTicket ticket = playerModel.ActiveTimeTrial;

            byte[]          bytes    = MetaSerialization.SerializeTagged(ticket, MetaSerializationFlags.IncludeAll, logicVersion: null);
            TimeTrialTicket restored = MetaSerialization.DeserializeTagged<TimeTrialTicket>(bytes, MetaSerializationFlags.IncludeAll, resolver: _gameConfig, logicVersion: null);

            Assert.That(restored.DraftPart, Is.Not.Null, "the draft did not survive serialization");
            Assert.That(restored.DraftPart, Is.Not.SameAs(ticket.DraftPart));
            Assert.That(restored.CourseId, Is.EqualTo(ticket.CourseId));
            Assert.That(restored.IsPractice, Is.True);
            Assert.That(restored.DraftPart.Ground, Has.Count.EqualTo(ticket.DraftPart.Ground.Count));
            Assert.That(restored.DraftPart.Hazards, Has.Count.EqualTo(ticket.DraftPart.Hazards.Count));
            Assert.That(restored.DraftPart.Bridges, Has.Count.EqualTo(ticket.DraftPart.Bridges.Count));
            Assert.That(Describe(CourseResolver.TryResolve(restored, _gameConfig)), Is.EqualTo(Describe(CourseResolver.TryResolve(ticket, _gameConfig))),
                "the restored ticket resolved to a different course");
        }

        #endregion // A run on a part editor draft
    }
}
