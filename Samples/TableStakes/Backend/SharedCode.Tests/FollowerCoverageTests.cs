using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Checks that every match action type is replayed on a client's copy by <see cref="TestMatchHost"/>, which
    /// throws when an action leaves the copy with a different checksum than the host. The test drives its own
    /// tables and reads only their hosts, so it does not depend on other tests. A new action type fails here until a scenario
    /// below publishes it.
    /// </summary>
    [TestFixture]
    public class FollowerCoverageTests
    {
        const ulong Seed = 5150UL;

        [Test]
        public void EveryMatchActionTypeIsReplayedOnAClientCopy()
        {
            // A whole game: seat updates when the human arrives and play begins, then every play and advance.
            MatchModel game = MatchTestDeals.StartedTable(Seed, MatchTestDeals.SeatTimings(), out TestMatchHost gameHost);
            MatchTestDeals.PlayWholeGame(game, gameHost, Seed);

            // A table whose only human never arrives is abandoned when the join window ends.
            MatchModel    empty     = MatchTestDeals.NewTable(Seed, MatchTestDeals.SeatTimings());
            TestMatchHost emptyHost = new TestMatchHost(empty, Seed);
            MatchHost.RunTable(empty, new MatchPendingBotMove(), MatchTestDeals.T0 + MatchTestDeals.SeatTimings().JoinWindow, emptyHost);
            Assert.That(empty.Phase, Is.EqualTo(MatchPhase.Abandoned), $"seed {Seed}");

            List<Type> missing = MatchActionTypes().Where(type => !gameHost.FollowedActionTypes.Contains(type) && !emptyHost.FollowedActionTypes.Contains(type)).ToList();
            Assert.That(missing, Is.Empty, "these actions were never replayed on a client's copy");
        }

        static IEnumerable<Type> MatchActionTypes()
            => typeof(MatchAction).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(MatchAction).IsAssignableFrom(type));
    }
}
