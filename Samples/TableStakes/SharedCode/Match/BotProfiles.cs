using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// How a profile picks its card.
    /// </summary>
    [MetaSerializable]
    public enum BotDecisionMode
    {
        /// <summary>
        /// The trick heuristic in <see cref="BotPolicy.RankLegalPlays"/>, with the profile's
        /// <see cref="BotProfile.MistakeChancePercent"/>.
        /// </summary>
        Heuristic = 0,

        /// <summary>A uniformly random legal card. For tests.</summary>
        RandomLegal = 1,

        /// <summary>
        /// The lowest legal card in <see cref="Card.CompareTo"/> order. For tests that need a fixed game: the
        /// choice depends on neither the seed nor the heuristic, so changing the heuristic does not change it.
        /// </summary>
        LowestLegal = 2,
    }

    /// <summary>
    /// How one bot seat plays: its decision mode, mistake chance and pacing.
    /// <para>
    /// The values are copied from a <see cref="BotProfileInfo"/> when the table forms, not referenced by key
    /// (<c>docs/game-config.md</c>). A config update during the match therefore does not change a bot's
    /// strength, and a bot's decision depends only on the seat view and the seed, not on which config the
    /// reader has loaded.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class BotProfile
    {
        /// <summary>
        /// The key of the <see cref="BotProfileInfo"/> the values were copied from, or a code-defined id such as
        /// the one on <see cref="BotProfiles.Strongest"/>.
        /// </summary>
        [MetaMember(1)] public BotProfileId    Id   { get; private set; }
        [MetaMember(2)] public BotDecisionMode Mode { get; private set; }

        /// <summary>
        /// The chance, in percent, that the bot plays its second-ranked card instead of its first. Only used by
        /// <see cref="BotDecisionMode.Heuristic"/>.
        /// </summary>
        [MetaMember(3)] public int MistakeChancePercent { get; private set; }

        /// <summary>
        /// The chance, in percent, that a move's think delay is drawn from the long range
        /// (<see cref="MatchTimings.BotThinkDelayMax"/> to <see cref="MatchTimings.BotThinkDelayOccasionalMax"/>)
        /// instead of the normal one. It affects pacing only, not the card. The durations come from the host
        /// (<c>docs/match.md</c>, "Timings come from the host").
        /// </summary>
        [MetaMember(4)] public int LongThinkChancePercent { get; private set; }

        public BotProfile() { }

        public BotProfile(BotProfileId id, BotDecisionMode mode, int mistakeChancePercent, int longThinkChancePercent)
        {
            Id                     = id;
            Mode                   = mode;
            MistakeChancePercent   = mistakeChancePercent;
            LongThinkChancePercent = longThinkChancePercent;
        }

        public override string ToString() => Id?.Value ?? "(no profile)";
    }

    /// <summary>
    /// Draws a bot seat's profile, and defines the profile used to play for an absent human.
    /// <para>
    /// The profiles a table may draw are in the <c>BotProfiles</c> game config library (<c>docs/bots.md</c>).
    /// <see cref="Strongest"/> is defined in code rather than config so that a config edit cannot make a bot
    /// play a human's cards with deliberate mistakes.
    /// </para>
    /// </summary>
    public static class BotProfiles
    {
        /// <summary>
        /// The heuristic with no deliberate mistakes. Used to play an absent human's seat, and for a bot seat
        /// when there is no profile to draw.
        /// </summary>
        public static readonly BotProfile Strongest =
            new BotProfile(BotProfileId.FromString("cover"), BotDecisionMode.Heuristic, mistakeChancePercent: 0, longThinkChancePercent: 15);

        /// <summary>
        /// The profile for seat <paramref name="seat"/> when the table forms, drawn uniformly from
        /// <paramref name="candidates"/> as a pure function of the seed and the seat.
        /// <para>
        /// Returns <see cref="Strongest"/> when <paramref name="candidates"/> is null or empty, or the drawn entry
        /// is null. The config build refuses an empty <c>BotProfiles</c> library, so an empty list comes from an
        /// archive built without that library.
        /// </para>
        /// </summary>
        public static BotProfile DrawForSeat(IReadOnlyList<BotProfile> candidates, ulong seed, int seat)
        {
            MatchRules.ThrowIfInvalidSeat(seat);

            if (candidates == null || candidates.Count == 0)
                return Strongest;

            RandomPCG rng = RandomPCG.CreateFromSeed(unchecked(seed + (ulong)(seat + 1) * 0x9E3779B97F4A7C15UL));
            return candidates[rng.NextInt(candidates.Count)] ?? Strongest;
        }
    }
}
