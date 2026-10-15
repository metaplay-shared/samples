using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Global game configuration values (singleton settings, not item libraries). Built from
    /// <c>GameConfigSource/Global.csv</c>, which is written as <c>Member,Value</c> rows rather than as a table.
    /// <para>
    /// Add game-wide tunables here as the game grows. A value belongs here when a designer could reasonably
    /// want it different and there is exactly one of it; anything with an id per row belongs in a library.
    /// Movement values belong in <see cref="TimeTrialRules"/> instead, because they are part of what a
    /// recording is recomputed against.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class GlobalConfig : GameConfigKeyValue<GlobalConfig>
    {
        /// <summary>
        /// How long after it is issued a run ticket may still be submitted, in seconds. It has to outlast a
        /// half-minute run plus a brief disconnect, and short enough that a ticket is not worth hoarding.
        /// </summary>
        [MetaMember(1)] public int TimeTrialTicketLifetimeSeconds = 600;

        // Member id 2 (CourseRotationPeriodSeconds) is retired and must not be reused.

        /// <summary>Orbs paid for winning a segment outright.</summary>
        [MetaMember(3)] public int OrbsForFirst = 3;

        /// <summary>Orbs paid for coming second in a segment.</summary>
        [MetaMember(4)] public int OrbsForSecond = 2;

        /// <summary>
        /// Orbs paid for coming third. A run at the player's own pace comes third on every segment, so this is
        /// the floor a player can always grind at by spending time rather than by getting better.
        /// </summary>
        [MetaMember(5)] public int OrbsForThird = 1;

        /// <summary>Orbs paid for coming last in a segment.</summary>
        [MetaMember(6)] public int OrbsForFourth = 0;

        /// <summary>
        /// How much higher the ground above a connective climb sits than the ground below it, in metres. The
        /// rise is what is drawn: a climb takes the same time however steep it is, so this is what puts a
        /// composed map on a mountainside rather than what it costs to get up one.
        /// </summary>
        [MetaMember(7)] public F32 CourseClimbRise = F32.FromInt(5);

        /// <summary>
        /// How long a connective climb's slope is, in metres along the course. The runner is carried up it at
        /// running pace with the button doing nothing, so this is part of what a climb costs every runner. It
        /// starts <see cref="ClimbDefinition.FootFlatLength"/> past the bottom torii, which the flat before it
        /// adds to the cost. See <see cref="ClimbDefinition"/>.
        /// </summary>
        [MetaMember(8)] public F32 CourseClimbSlopeLength = F32.FromInt(24);

        /// <summary>
        /// How long the flat run-out from a climb's top to its gate is, in metres. It is what puts every runner
        /// through the gate on the ground at running pace, and the rest of what a climb costs.
        /// See <see cref="ClimbDefinition"/>.
        /// </summary>
        [MetaMember(9)] public F32 CourseClimbRunOutLength = F32.FromInt(8);

        /// <summary>
        /// How long a map stays in the registry's active set before the one that has been there longest is
        /// replaced, in seconds. Shortening it turns the set over faster without changing how many maps
        /// are in it.
        /// </summary>
        [MetaMember(10)] public int MapRotationPeriodSeconds = 14400;

        /// <summary>
        /// The map offline practice plays. Offline runs the client and its own server in one process, so there
        /// is no registry to ask for the active set and the map is named here instead.
        /// </summary>
        [MetaMember(11)] public CourseId OfflinePracticeMapId = CourseId.FromString("rr-crossriver+rr-smallclimb+rr-spikesteps");
    }
}
