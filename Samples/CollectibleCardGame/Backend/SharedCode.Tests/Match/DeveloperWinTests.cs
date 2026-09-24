using Metaplay.Core;
using Metaplay.Core.Model;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    [TestFixture]
    public class DeveloperWinTests
    {
        [TestCase(0)]
        [TestCase(1)]
        public void DeveloperWinEndsMulliganAndRoutesPracticeToARealHeist(int winner)
        {
            SharedGameConfig config = TestGameConfig.Shared;
            MatchModel match = MatchEngine.Create(new MatchSetup(123, config, MatchTimings.Instant,
                TestDecks.Standard(config), TestDecks.Alternate(config))).Model;
            match.Stakes = MatchStakes.Practice(new List<int> { 25, 25 },
                new List<List<CardId>> { new List<CardId>(), new List<CardId>() });
            MatchDebugWin action = new MatchDebugWin(winner);
            Assert.That(action.ServerPrepare(match), Is.EqualTo(MatchIntentResults.Success));

            // The lineups come off the secret in ServerPrepare, which is the whole reason the shortcut works
            // from the mulligan: nothing has been played, so nothing would be eligible without the reveal.
            Assert.That(action.Cards0, Is.Not.Empty);
            Assert.That(action.Cards1, Is.Not.Empty);
            Assert.That(match.Rules.Phase, Is.EqualTo(MatchPhase.Mulligan));
            action.InvokeExecute(match, commit: true);
            MatchResult result = match.Rules.Result;
            MatchOutcomeRecord record = new MatchOutcomeRecord(result.Outcome, winner, result.FinalTurn, result.Cause,
                new List<bool> { true, true }, true, MetaTime.Epoch);
            Assert.Multiple(() =>
            {
                Assert.That(match.Rules.Phase, Is.EqualTo(MatchPhase.Complete));
                Assert.That(result.WinnerSeat, Is.EqualTo(winner));
                Assert.That(result.Cause, Is.EqualTo(MatchEndCause.DeveloperWin));
                Assert.That(match.Pacing.DeadlineAt, Is.Null);
                Assert.That(MatchHeistPolicy.PhaseRuns(match.Stakes, record, action.Cards0), Is.True);
                // The guard against a replay lives in ServerPrepare now: the action itself no longer
                // re-asks, because a follower re-running it has already been validated by the leader.
                Assert.That(action.ServerPrepare(match).IsSuccess, Is.False);
            });
        }
    }
}
