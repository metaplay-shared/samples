using Game.Logic;
using Game.Server.Tournament;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.League;
using Metaplay.Core.Schedule;
using Metaplay.Server.League;
using NUnit.Framework;
using System;
using System.Linq;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests the seasonal tournament's schedule options and league registration. Both are server-only, and a
    /// mistake in either would otherwise show up only <b>at server startup</b>.
    /// <para>
    /// The league manager builds a recurring calendar schedule from the schedule options when it loads, and
    /// throws if the schedule is not a valid cycle, for example if the rest period is longer than the season.
    /// The server would then never run a season and report it only in a log.
    /// </para>
    /// </summary>
    [TestFixture]
    public class TournamentSeasonScheduleTests
    {
        static readonly TournamentOptions Options = new TournamentOptions();

        static LeagueSeasonCycleSchedule Cycle => new LeagueSeasonCycleSchedule(
            MetaCalendarDateTime.FromDateTime(Options.SeasonCycleStartDate),
            Options.SeasonCycleRecurrence,
            Options.SeasonCycleRestPeriod,
            Options.SeasonCycleEndingSoonPeriod);

        [Test]
        public void TheConfiguredCadenceIsAScheduleTheLeagueManagerWillAccept()
        {
            Assert.That(Cycle.IsValid(), Is.True,
                "the league manager throws on an invalid cycle and then never opens a season");
        }

        [Test]
        public void TheSeasonIsSevenDaysWithATwoHourRestWindow()
        {
            Assert.That(Options.SeasonCycleRecurrence.Days, Is.EqualTo(7));
            Assert.That(Options.SeasonCycleRestPeriod.Hours, Is.EqualTo(2),
                "the between-seasons database job runs in the rest window, so it cannot be nothing");
            Assert.That(Options.SeasonCycleEndingSoonPeriod.Hours, Is.EqualTo(4));
        }

        [Test]
        public void TheStartDateIsUtc()
        {
            Assert.That(Options.SeasonCycleStartDate.Kind, Is.EqualTo(DateTimeKind.Utc),
                "the league manager refuses anything else");
        }

        /// <summary>
        /// The schedule is the recurring one from runtime options, not a custom schedule that the game would have
        /// to provide on every check.
        /// </summary>
        [Test]
        public void TheScheduleComesFromRuntimeOptions()
        {
            Assert.That(Options.ScheduleType, Is.EqualTo(LeagueSeasonScheduleType.RuntimeOptions));
        }

        /// <summary>
        /// The registry has one league, in the tournament client slot, with players as participants. A second
        /// entry would also change the league managers' sharding, which is based on this list.
        /// </summary>
        [Test]
        public void ThereIsExactlyOneLeagueAndItIsTheTournament()
        {
            TableStakesLeagueRegistry registry = new TableStakesLeagueRegistry();

            Assert.That(registry.LeagueInfos, Has.Count.EqualTo(1));

            LeagueManagerRegistry.LeagueInfo league = registry.LeagueInfos.Single();

            Assert.That(league.ClientSlot, Is.EqualTo(ClientSlotGame.Tournament));
            Assert.That(league.ParticipantKind, Is.EqualTo(EntityKindCore.Player));
            Assert.That(league.LeagueManagerId.Value, Is.EqualTo((ulong)TournamentRules.LeagueId));
            Assert.That(league.ManagerActorType, Is.EqualTo(typeof(TournamentLeagueManagerActor)));
            Assert.That(league.DivisionActorType, Is.EqualTo(typeof(TournamentDivisionActor)));
            Assert.That(league.OptionsType, Is.EqualTo(typeof(TournamentOptions)));
        }
    }
}
