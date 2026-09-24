using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// How long a seat may think. Every value here is a host parameter, never game config and never a
    /// constant in rules source, while Den hit points and the mana ramp are content
    /// (<c>Docs/rules.md</c>, "Two kinds of number, two different homes").
    /// <para>
    /// These are <b>decision clocks only</b>. The server holds no animation: what a resolution looks like and
    /// how long it stays on screen is the client's, paced by <c>BoardPresentation</c>
    /// (<c>Docs/client.md</c>). What bounds the client's catch-up is the presentation allowance the
    /// host adds to each positive clock, not a gate the table holds shut.
    /// </para>
    /// <para>
    /// It is a <b>public member of the model</b>, and that is load-bearing rather than convenient: an action
    /// that arms a deadline computes <c>CurrentTime + Timings.TurnDeadline</c> from the model's own clock, and
    /// the follower reads the same durations off the same model, which makes every pacing stamp a pure
    /// function of (payload, public state, tick).
    /// </para>
    /// <para>
    /// The durations are <em>host-supplied</em>, once: the actor writes them when it sets the table up and
    /// nothing writes them again, so a match cannot change its pacing mid-game under any circumstance. An
    /// option edit reaches the tables formed after it and no others.
    /// </para>
    /// <para>
    /// A zero duration means <b>not in force</b>, not "already lapsed": a zero turn deadline arms no deadline
    /// and the client draws no ring.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public readonly struct MatchTimings
    {
        [MetaMember(1)] public readonly MetaDuration MulliganDeadline;
        [MetaMember(2)] public readonly MetaDuration TurnDeadline;
        /// <summary> The per-seat, per-match reserve bank the turn deadline is extended out of. </summary>
        [MetaMember(3)] public readonly MetaDuration TurnReserveBank;
        /// <summary> One extension's worth. Only a seat that has already acted this turn may draw one. </summary>
        [MetaMember(4)] public readonly MetaDuration TurnReserveExtension;
        /// <summary> How long the owner of a held resolution has to make the peek choice. </summary>
        [MetaMember(5)] public readonly MetaDuration EffectChoiceDeadline;

        [MetaDeserializationConstructor]
        public MatchTimings(
            MetaDuration mulliganDeadline,
            MetaDuration turnDeadline,
            MetaDuration turnReserveBank,
            MetaDuration turnReserveExtension,
            MetaDuration effectChoiceDeadline)
        {
            MulliganDeadline     = mulliganDeadline;
            TurnDeadline         = turnDeadline;
            TurnReserveBank      = turnReserveBank;
            TurnReserveExtension = turnReserveExtension;
            EffectChoiceDeadline = effectChoiceDeadline;
        }

        /// <summary>
        /// Everything zero: no deadlines. What every unit test and the self-play harness use by default — a
        /// suite that forced time forward by sleeping would be testing the test runner.
        /// </summary>
        public static readonly MatchTimings Instant = default;

        /// <summary>
        /// The shipped budget from <c>Docs/match.md</c>. Offered here so a host and a test can
        /// name the same numbers; a real host reads them from its own runtime options.
        /// </summary>
        public static readonly MatchTimings Default = new MatchTimings(
            mulliganDeadline:     MetaDuration.FromSeconds(30),
            turnDeadline:         MetaDuration.FromSeconds(60),
            turnReserveBank:      MetaDuration.FromSeconds(60),
            turnReserveExtension: MetaDuration.FromSeconds(15),
            effectChoiceDeadline: MetaDuration.FromSeconds(20));
    }
}
