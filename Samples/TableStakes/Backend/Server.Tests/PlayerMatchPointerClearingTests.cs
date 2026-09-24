using Game.Logic;
using Game.Server.Player;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using NUnit.Framework;
using System;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests which failed table probes make the player's actor clear its pointer to the table.
    /// <para>
    /// Clearing the pointer to a live table loses the game, because the player never re-attaches and a bot
    /// plays the seat. Keeping a pointer to an unusable table fails every later login. The pointer is cleared
    /// only if the probe reached the table.
    /// </para>
    /// </summary>
    [TestFixture]
    public class PlayerMatchPointerClearingTests
    {
        static readonly EntityId TableId = EntityId.Create(EntityKindGame.Match, 42);

        [TestCase(EntityShard.EntityUnreachableError.Cause.ShardShuttingDown)]
        [TestCase(EntityShard.EntityUnreachableError.Cause.ConnectionToNodeLost)]
        [TestCase(EntityShard.EntityUnreachableError.Cause.CannotSpawnActor)]
        public void AnUnreachableTableIsKept(EntityShard.EntityUnreachableError.Cause cause)
        {
            Assert.That(PlayerActor.ProbeFailureForgetsTable(new EntityShard.EntityUnreachableError(TableId, cause)), Is.False,
                "a draining shard during a rolling deploy would drop the player's live game");
        }

        /// <summary>A crash on wake, a missing row and a failed handler all mean the probe reached a table that cannot be used.</summary>
        static TestCaseData[] ProbesThatReachedAnUnusableTable() => new TestCaseData[]
        {
            new TestCaseData(new EntityShard.EntityCrashedError(TableId, typeof(InvalidOperationException), "row cannot be read", "")).SetArgDisplayNames("CrashedOnWake"),
            new TestCaseData(new InvalidEntityAsk("no row")).SetArgDisplayNames("NoRow"),
            new TestCaseData(new EntityShard.UnexpectedEntityAskError(TableId, "EntityAsk handler", new InvalidOperationException("boom"))).SetArgDisplayNames("HandlerFailed"),
        };

        [TestCaseSource(nameof(ProbesThatReachedAnUnusableTable))]
        public void AnUnusableTableIsForgotten(EntityAskExceptionBase failure)
        {
            Assert.That(PlayerActor.ProbeFailureForgetsTable(failure), Is.True);
        }
    }
}
