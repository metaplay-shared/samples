using Game.Logic;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;

namespace Game.Server.WeeklyEvent
{
    /// <summary>
    /// Weekly event seeder settings (<c>docs/weekly-event.md</c>).
    /// <para>
    /// They are runtime options, not game config, because LiveOps events are created on the timeline rather
    /// than published with config. Nothing in the player's model depends on them, and changing the horizon
    /// should not require a config publish that every client downloads.
    /// </para>
    /// </summary>
    [RuntimeOptions("WeeklyEventSeeding", isStatic: false, "Keeps a rolling horizon of weekly themed events on the LiveOps timeline.")]
    public class WeeklyEventSeedingOptions : RuntimeOptionsBase
    {
        [MetaDescription("Whether the server seeds weekly events at all. Off leaves the timeline to an operator, and a player with no event sees the designed empty state. Turning it back on resumes seeding at the next pass; no restart is needed.")]
        public bool Enabled { get; private set; } = true;

        [MetaDescription("How many weeks ahead each pass keeps seeded, counting the one running now. Measured from the pass, so it moves forward every time the pass runs.")]
        public int HorizonWeeks { get; private set; } = WeeklyEventSeeding.DefaultHorizonWeeks;

        [MetaDescription("How often a pass runs after the first one. It exists so a server that stays up for months keeps extending its horizon without a deploy.")]
        public TimeSpan PassInterval { get; private set; } = TimeSpan.FromHours(6);

        [MetaDescription("How long after the seeder starts the first pass runs. Long enough that the active game config has been published to this node on an ordinary start.")]
        public TimeSpan FirstPassDelay { get; private set; } = TimeSpan.FromSeconds(5);

        [MetaDescription("How long the seeder waits before trying again when a pass could not run — no active game config yet, or the timeline did not answer.")]
        public TimeSpan RetryDelay { get; private set; } = TimeSpan.FromSeconds(15);
    }
}
