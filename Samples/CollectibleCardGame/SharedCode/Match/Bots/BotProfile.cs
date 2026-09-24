namespace Game.Logic
{
    /// <summary>
    /// How strong a bot seat plays. A profile is not a different policy: it is a small parameter set applied on
    /// top of the one decision procedure in <see cref="BotPolicy"/>, so every profile reasons the same way and
    /// they differ only in how often — and how far — the choice departs from the top-ranked action
    /// (<c>Docs/bots.md</c>, "Personalities").
    /// <para>
    /// Imperfection is seeded and defensible. A departure is drawn from a substream keyed on the decision's own
    /// coordinates and lands inside the plausible band, so the mistakes are the kind a person makes rather than
    /// a random discard — and the same game, replayed, makes the same mistake in the same place.
    /// </para>
    /// </summary>
    public sealed class BotProfile
    {
        /// <summary> One unit of "a mana of card value" in the policy's scoring. Band widths are in these. </summary>
        public const int ManaValueUnit = 100;

        public readonly string Name;

        /// <summary>
        /// How often, in basis points, a decision departs from the top-ranked action. Zero means the profile
        /// never constructs its mistake substream at all — not that it rolls and always fails.
        /// </summary>
        public readonly int MistakeChanceBp;

        /// <summary>
        /// What "plausible" means: how far below the top score a candidate may sit and still be a departure the
        /// policy is willing to make. A mistake always lands inside this band, never outside it.
        /// </summary>
        public readonly int CandidateBandWidth;

        BotProfile(string name, int mistakeChanceBp, int candidateBandWidth)
        {
            Name               = name;
            MistakeChanceBp    = mistakeChanceBp;
            CandidateBandWidth = candidateBandWidth;
        }

        /// <summary>
        /// Whether this profile ever consults a mistake substream. The guard is structural rather than a roll
        /// that always fails, so a profile with no imperfection is stable even if the mistake mechanism changes
        /// shape later.
        /// </summary>
        public bool MakesMistakes => MistakeChanceBp > 0 && CandidateBandWidth > 0;

        /// <summary>
        /// Never a deliberate mistake. What cover, auto-play and the Heist auto-default always use: a seat
        /// played on behalf of an absent human is a service to that human, and their collection is on the table
        /// (<c>Docs/bots.md</c>).
        /// </summary>
        public static readonly BotProfile Strongest = new BotProfile(nameof(Strongest), 0, 0);

        /// <summary>
        /// Behaviourally identical to <see cref="Strongest"/> and named apart on purpose: the pinned-decision
        /// tests and the balance harness read from this one, because a per-card win-rate signal contaminated by
        /// seeded mistakes measures the mistakes rather than the card.
        /// </summary>
        public static readonly BotProfile StrictlyDeterministic = new BotProfile(nameof(StrictlyDeterministic), 0, 0);

        /// <summary> The gentlest imperfection: an occasional second-best line. </summary>
        public static readonly BotProfile Practiced = new BotProfile(nameof(Practiced), 1200, 2 * ManaValueUnit);

        /// <summary> Misplays tempo and trades often enough to be noticed, never enough to be silly. </summary>
        public static readonly BotProfile Casual = new BotProfile(nameof(Casual), 3000, 6 * ManaValueUnit);

        /// <summary> The weakest tier that still has a floor: a wide band and a mistake more often than not. </summary>
        public static readonly BotProfile Sloppy = new BotProfile(nameof(Sloppy), 6000, 12 * ManaValueUnit);

        /// <summary>
        /// A profile at numbers the named tiers do not offer. Practice's difficulty picker will want values
        /// between tiers once the tiers are tuned, and the profile tests want the degenerate ends of the
        /// range — a profile that always departs is how "the mechanism is wired to something" is checked
        /// without shipping one.
        /// </summary>
        public static BotProfile Tuned(string name, int mistakeChanceBp, int candidateBandWidth)
            => new BotProfile(name, mistakeChanceBp, candidateBandWidth);

        /// <summary>
        /// Every profile, weakest last. Practice's difficulty picker and the matchmaker's fallback draw from
        /// this list; the numbers themselves are a tuning concern.
        /// </summary>
        public static readonly BotProfile[] All =
        {
            Strongest,
            StrictlyDeterministic,
            Practiced,
            Casual,
            Sloppy,
        };

        public override string ToString() => Name;
    }

    /// <summary>
    /// Which of a bot's decision streams a draw belongs to. Two decisions can be taken at the same
    /// <see cref="MatchRulesState.ActionCount"/> — a peek is answered at the count of the play that raised it,
    /// and so is the play that would follow it — so the kind is part of the substream key and changing one
    /// decision's outcome never perturbs an unrelated decision's draw.
    /// </summary>
    public enum BotDecisionKind
    {
        MainPhase = 1,
        Mulligan  = 2,
        /// <summary>
        /// Answering a held peek. It needs a kind of its own precisely because it shares an action count with
        /// the next main-phase decision — the count does not advance across the pause — which is the collision
        /// this enum exists to separate. <see cref="BotPolicy"/> answers a peek by a deterministic rule and never
        /// draws here; the random-legal source does.
        /// </summary>
        EffectChoice = 3,
    }
}
