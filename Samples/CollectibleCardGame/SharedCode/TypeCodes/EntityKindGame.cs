using Metaplay.Core;

namespace Game.Logic
{
    /// <summary>
    /// Registry for game-specific <see cref="EntityKind"/> values. The game-specific range is [100, 300).
    /// A duplicate is a startup crash naming the value, so numbers are allocated here in order. An entity kind
    /// is a persisted identity: once a deployed game has written rows under one, it is never recycled.
    /// </summary>
    [EntityKindRegistry(100, 300)]
    public static class EntityKindGame
    {
        /// <summary> One game of Sticky Paws: an ephemeral multiplayer entity (<c>Docs/match.md</c>). </summary>
        public static readonly EntityKind Match = EntityKind.FromValue(100);

        /// <summary>
        /// The ranked queue: a singleton service entity holding an in-memory queue
        /// (<c>Docs/matchmaking.md</c>). Demonstrates an ephemeral singleton service.
        /// </summary>
        public static readonly EntityKind Matchmaker = EntityKind.FromValue(101);

        /// <summary>The persisted global ladder and transient activity aggregator.</summary>
        public static readonly EntityKind Community = EntityKind.FromValue(102);
    }
}
