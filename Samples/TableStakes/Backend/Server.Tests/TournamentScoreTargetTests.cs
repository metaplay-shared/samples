using Game.Logic;
using Game.Server.Player;
using Metaplay.Core;
using Metaplay.Core.League;
using NUnit.Framework;
using System;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests which group a player's tournament totals may be sent to (<c>docs/seasonal-tournament.md</c>,
    /// "Scoring"). Totals go only to the group the player's state was joined in, so a join that the league has
    /// made but the player's model has not executed yet cannot send one season's totals to the next season's
    /// group.
    /// </summary>
    [TestFixture]
    public class TournamentScoreTargetTests
    {
        static readonly EntityId SeasonOneGroup = new DivisionIndex(0, 1, 0, 0).ToEntityId();

        static PlayerTournamentState JoinedSeasonOne()
        {
            PlayerTournamentState tournament = new PlayerTournamentState();
            MetaTime              joinedAt   = MetaTime.FromDateTime(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            tournament.Join(1, SeasonOneGroup, joinedAt + MetaDuration.FromDays(7), rewardTable: null, joinedAt);
            return tournament;
        }

        /// <summary>
        /// The run is sent to the group it was joined in, not to the next season's group before that join
        /// executes, and not while the league has no group for the player (season 0 here stands for
        /// <see cref="EntityId.None"/>).
        /// </summary>
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(0, false)]
        public void TheRunIsSentOnlyToTheGroupItWasJoinedIn(int targetSeason, bool expectedSent)
        {
            EntityId target = targetSeason == 0 ? EntityId.None : new DivisionIndex(0, targetSeason, 0, 0).ToEntityId();

            Assert.That(PlayerActor.IsTournamentRunInDivision(JoinedSeasonOne(), target), Is.EqualTo(expectedSent));
        }

        [Test]
        public void NothingIsSentBeforeThePlayerHasJoinedASeason()
        {
            Assert.That(PlayerActor.IsTournamentRunInDivision(new PlayerTournamentState(), SeasonOneGroup), Is.False);
        }
    }
}
