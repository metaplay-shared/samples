using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Helpers for tests that run actions against a player model: a new player, a byte snapshot of its state, and
    /// the dry-run-then-commit pass the SDK runs every action through.
    /// </summary>
    public static class TestPlayers
    {
        /// <summary>The player id every player from <see cref="New"/> has.</summary>
        public static readonly EntityId PlayerId = EntityId.Create(EntityKindCore.Player, 7);

        /// <summary>
        /// A new player created at <paramref name="at"/> on <paramref name="config"/>, or on
        /// <see cref="TestGameConfig.Build"/> when it is null. When <paramref name="captured"/> is given, every
        /// analytics event the model emits is added to it.
        /// </summary>
        public static PlayerModel New(MetaTime at, SharedGameConfig config = null, List<PlayerEventBase> captured = null)
        {
            PlayerModel player = new PlayerModel();

            if (captured != null)
                player.AnalyticsEventHandler = new AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>((context, payload) => captured.Add(payload));

            player.InitializeNewPlayerModel(at, config ?? TestGameConfig.Build(), PlayerId, "Tester");
            return player;
        }

        /// <summary>The player's whole state as bytes, so a test can check that an action changed nothing.</summary>
        public static byte[] Snapshot(PlayerModel player) =>
            MetaSerialization.SerializeTagged(player, MetaSerializationFlags.IncludeAll, logicVersion: null);

        /// <summary>
        /// Executes <paramref name="action"/> the way the SDK does: the dry-run pass (<c>commit: false</c>), then
        /// the commit pass. Asserts that both passes return the same result, and returns it.
        /// </summary>
        public static MetaActionResult DryRunThenCommit(PlayerModel player, PlayerActionBase action)
        {
            MetaActionResult dryRun = action.InvokeExecute(player, commit: false);
            MetaActionResult commit = action.InvokeExecute(player, commit: true);

            Assert.That(commit, Is.EqualTo(dryRun), $"the dry run and the commit of {action.GetType().Name} disagreed");
            return commit;
        }

        /// <summary>
        /// A finished table's result for the player, from a table where the three other seats were humans and the
        /// player played to the end. A position of 0 is a win.
        /// </summary>
        public static PlayerRecordMatchResult MatchResult(int matchIndex, MetaTime finishedAt, int position = 0, int tricksWon = 3) =>
            new PlayerRecordMatchResult(MatchTestDeals.MatchId(matchIndex), finishedAt, position, tricksWon, humanOpponents: 3, finishedByPlayer: true);
    }
}
