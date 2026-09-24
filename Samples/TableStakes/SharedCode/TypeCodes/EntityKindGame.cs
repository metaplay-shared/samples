using Metaplay.Core;

namespace Game.Logic
{
    /// <summary>
    /// Registry of game-specific <see cref="EntityKind"/> values, in the range set by
    /// <see cref="EntityKindRegistryAttribute"/>.
    /// <para>
    /// These values must never change: an entity ID includes its kind, so renumbering a kind breaks every
    /// stored entity and every stored reference to it. Retire a value instead of reusing it.
    /// </para>
    /// </summary>
    [EntityKindRegistry(100, 300)]
    public static class EntityKindGame
    {
        /// <summary>One table hosting one game of Table Stakes. See <c>docs/match.md</c>.</summary>
        public static readonly EntityKind Match = EntityKind.FromValue(100);

        /// <summary>The singleton service that seats players at tables. See <c>docs/matchmaking.md</c>.</summary>
        public static readonly EntityKind Matchmaker = EntityKind.FromValue(101);

        // 102 is retired (it was the match sweeper). Do not reuse it.

        /// <summary>
        /// The singleton service that creates weekly themed events on the LiveOps timeline up to a configured
        /// time ahead. See <c>docs/weekly-event.md</c>.
        /// </summary>
        public static readonly EntityKind WeeklyEventSeeder = EntityKind.FromValue(103);
    }
}
