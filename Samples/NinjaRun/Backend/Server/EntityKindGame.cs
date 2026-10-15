using Metaplay.Core;

namespace Game.Server
{
    /// <summary>
    /// Registry for game-specific <see cref="EntityKind"/> values. The game-specific range is [100, 300).
    /// Add server-side multiplayer entities (e.g. a matchmaking lobby, a match, a guild) here, for example:
    /// <code>
    /// public static readonly EntityKind Match = EntityKind.FromValue(100);
    /// </code>
    /// </summary>
    [EntityKindRegistry(100, 300)]
    public static class EntityKindGame
    {
        /// <summary>
        /// The opponents and the spread of times kept for one course. One entity per course, named after the
        /// course id, so the number of courses is a number of entities rather than a number of services:
        /// only the courses being played are resident.
        /// </summary>
        public static readonly EntityKind CourseGhostPool = EntityKind.FromValue(100);

        /// <summary>
        /// Every map the server has published and the handful of them a run may be started on right now. One
        /// entity for the whole game (the active set is one set, and it rotates on one clock), so it is a
        /// singleton service that stays resident rather than an entity per map.
        /// </summary>
        public static readonly EntityKind MapRegistry = EntityKind.FromValue(101);
    }
}
