using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// What plays a seat with nobody behind it. A greedy heuristic, not a search: it is asked for one action at
    /// a time from the legal set the shared rules produced and keeps being asked until it chooses to end the
    /// turn, which is the same shape a human's turn has (<c>Docs/bots.md</c>).
    /// <para>
    /// Two properties hold by construction and are asserted by self-play:
    /// </para>
    /// <list type="bullet">
    /// <item><b>It never takes an illegal action.</b> Every action it returns is an element of
    /// <see cref="SeatView.LegalActions"/> — it does not construct one of its own — with the two exceptions
    /// the rules only offer as a template: the mulligan's replace set and the peek's keep set, both of which
    /// the engine validates like any other intent.</item>
    /// <item><b>A decision is a pure function of (seat view, seed).</b> The only state it holds is the
    /// content, the profile and the match seed, and the only randomness it consumes is the profile's mistake
    /// substream. It is handed a <see cref="SeatView"/>, which names the public members and nothing else, so
    /// a policy that played well by cheating is a policy the view has no name for — pinned by a reflection
    /// test over the view's member list rather than by the type's shape
    /// (<c>Docs/hidden-information.md</c>, "What keeps it honest").</item>
    /// </list>
    /// <para>
    /// Stateless and therefore shareable: one instance serves both seats of a match, and the seat it is
    /// deciding for arrives as an argument rather than as a field.
    /// </para>
    /// </summary>
    public sealed partial class BotPolicy : IActionSource
    {
        readonly SharedGameConfig _config;
        readonly BotProfile       _profile;
        readonly ulong            _matchSeed;

        /// <summary>
        /// <paramref name="matchSeed"/> keys the profile's mistake substreams and nothing else. A profile that
        /// makes no mistakes never reads it, which is why the strongest profile needs no seed to be handed one.
        /// The host passes <see cref="MatchSecrets.BotSeed"/>; treat it as secret.
        /// </summary>
        public BotPolicy(SharedGameConfig config, BotProfile profile, ulong matchSeed = 0)
        {
            _config    = config;
            _profile   = profile;
            _matchSeed = matchSeed;
        }

        /// <summary> The strongest profile over this content: what cover, auto-play and practice always use. </summary>
        public static BotPolicy Strongest(SharedGameConfig config) => new BotPolicy(config, BotProfile.Strongest);

        public BotProfile Profile => _profile;

        // ---------------------------------------------------------------- the decision

        /// <summary>
        /// One action for this seat, or null to end the turn. The order is fixed: answer a held resolution if
        /// one is waiting, then the mulligan while that phase is open, then lethal, then the scored choice.
        /// </summary>
        public MatchIntent ChooseAction(SeatView view, int seat)
        {
            // A held resolution is exclusive: the choice that answers it is the only intent the table accepts,
            // and the legal set is empty while it is held (Docs/engine-rulings.md).
            if (view.PendingChoice != null)
                return ChooseEffectChoice(view, seat);

            if (view.Rules.Phase == MatchPhase.Mulligan)
                return ChooseMulligan(view, seat);

            IReadOnlyList<MatchIntent> legal = view.LegalActions;
            if (legal.Count == 0)
                return null;

            // Lethal first. It is cheap to check and missing it is the single largest source of "the bot is
            // stupid" — and this branch is exempt from the mistake roll at every profile strength, which is
            // the literal, checkable form of "it does not throw a game that is already won".
            MatchIntent lethal = FindLethalAction(view, seat, legal);
            if (lethal != null)
                return lethal;

            Situation situation = new Situation(view, seat);

            List<int> scores = new List<int>(legal.Count);
            for (int ndx = 0; ndx < legal.Count; ndx++)
                scores.Add(Score(situation, legal[ndx]));

            // Strictly greater, so an exact tie keeps the earlier candidate: the legal set arrives in the
            // rules' own canonical enumeration order, which makes the tie-break a fact about the position
            // rather than a draw from the seed (Docs/bots.md, "Deciding an action").
            int best = 0;
            for (int ndx = 1; ndx < scores.Count; ndx++)
            {
                if (scores[ndx] > scores[best])
                    best = ndx;
            }

            int chosen = ApplyMistake(seat, view.Rules.ActionCount, scores, best);

            // Ending the turn is expressed as null so a host can tell "I am finished" from "here is an action".
            return legal[chosen] is EndTurnIntent ? null : legal[chosen];
        }

        /// <summary>
        /// What the policy thinks each legal action is worth, in the legal set's own order. The lethal branch
        /// is not in it — that is a filter over the set rather than a score — so this is the ranking the
        /// profile's plausible band is measured against, and the thing to read when asking why a bot did that.
        /// </summary>
        public List<int> ScoreLegalActions(SeatView view, int seat)
        {
            Situation situation = new Situation(view, seat);

            List<int> scores = new List<int>(view.LegalActions.Count);
            for (int ndx = 0; ndx < view.LegalActions.Count; ndx++)
                scores.Add(Score(situation, view.LegalActions[ndx]));

            return scores;
        }

        // ---------------------------------------------------------------- the mistake mechanism

        /// <summary>
        /// The profile's seeded imperfection, and the only thing in the policy that reads the seed. It never
        /// reaches outside the plausible band, so a mistake is a line a person might have taken rather than a
        /// move with no reading behind it.
        /// </summary>
        int ApplyMistake(int seat, int actionCount, List<int> scores, int best)
        {
            if (!_profile.MakesMistakes)
                return best;

            RandomPCG rng = BotSubstreams.For(_matchSeed, seat, BotDecisionKind.MainPhase, actionCount);
            if (rng.NextInt(10000) >= _profile.MistakeChanceBp)
                return best;

            int       floor = scores[best] - _profile.CandidateBandWidth;
            List<int> band  = new List<int>();
            for (int ndx = 0; ndx < scores.Count; ndx++)
            {
                if (ndx != best && scores[ndx] >= floor)
                    band.Add(ndx);
            }

            // Nothing else was plausible, so the top action is what a person would have taken too.
            if (band.Count == 0)
                return best;

            return band[rng.NextInt(band.Count)];
        }

        // ---------------------------------------------------------------- the mulligan

        /// <summary>
        /// A curve rule: keep what the opening turns can cast, throw what they cannot, with an override for the
        /// cards that earn their keep above the line — a Guard, cheap removal, a body with a Hello
        /// (<c>Docs/bots.md</c>). One shot over the whole hand, which is the shape the mulligan intent
        /// has.
        /// </summary>
        MatchIntent ChooseMulligan(SeatView view, int seat)
        {
            if (view.LegalActions.Count == 0)
                return null;

            IReadOnlyList<HandCard> hand    = view.Hand;
            List<CardInstanceId> replace = new List<CardInstanceId>();
            List<int>            thrown  = new List<int>();
            List<int>            kept    = new List<int>();

            for (int ndx = 0; ndx < hand.Count; ndx++)
            {
                if (!WorthKeeping(view, seat, hand[ndx]))
                {
                    replace.Add(hand[ndx].Instance);
                    thrown.Add(ndx);
                }
                else
                    kept.Add(ndx);
            }

            PerturbMulligan(view, seat, hand, replace, thrown, kept);

            return new MulliganIntent(replace);
        }

        /// <summary>
        /// A weaker profile keeps one card it should have thrown, or throws one it should have kept. It is a
        /// single flip rather than a re-roll of the hand, because a hand mulliganed at random is the "random
        /// discard" bots.md rules out.
        /// </summary>
        void PerturbMulligan(SeatView view, int seat, IReadOnlyList<HandCard> hand, List<CardInstanceId> replace, List<int> thrown, List<int> kept)
        {
            if (!_profile.MakesMistakes)
                return;

            RandomPCG rng = BotSubstreams.For(_matchSeed, seat, BotDecisionKind.Mulligan, view.Rules.ActionCount);
            if (rng.NextInt(10000) >= _profile.MistakeChanceBp)
                return;

            // Keeping something expensive is the more human of the two mistakes, so it goes first.
            if (thrown.Count > 0)
            {
                replace.Remove(hand[thrown[rng.NextInt(thrown.Count)]].Instance);
                return;
            }

            if (kept.Count > 0)
                replace.Add(hand[kept[rng.NextInt(kept.Count)]].Instance);
        }

        /// <summary>
        /// Keep what the first several turns can actually cast, plus the cards that pull their weight one mana
        /// above that line. The cost is derived through <see cref="ManaRules.CostToPlay"/>, so a Weather that
        /// discounts a class of card is already in it.
        /// </summary>
        bool WorthKeeping(SeatView view, int seat, HandCard held)
        {
            int cost = CostOf(view, seat, held);

            if (cost <= MulliganKeepCost)
                return true;

            if (cost > MulliganKeepCost + 1)
                return false;

            CardInfo card = Card(held.Card);

            // A Guard buys the turns the rest of the hand needs.
            if ((card.GetKeywordFlags() & KeywordFlags.Guard) != 0)
                return true;

            // Removal and a body that does something on arrival both beat a blank of the same cost.
            return card.BindsTrigger(CardTrigger.Hello);
        }

        /// <summary>
        /// The curve line: what a seat can cast by its third own turn, since maximum mana is the turn number
        /// and an opening hand that cannot act before then has not opened anything.
        /// </summary>
        const int MulliganKeepCost = 3;

        // ---------------------------------------------------------------- the held resolution

        /// <summary>
        /// The peek's answer: keep the costliest cards, ties on canonical card order and then on identity. This
        /// is deliberately the same rule the engine applies when nobody chooses in time, so a bot's answer and
        /// a lapsed seat's answer are the same answer — a player who steps away mid-peek does not get a
        /// different game from one whose seat was covered.
        /// <para>
        /// It is not subject to the mistake mechanism for that reason: a "misplayed" peek would be a choice the
        /// engine's own default could never produce, and the default's predictability is a stated property
        /// (<c>Docs/effects.md</c>).
        /// </para>
        /// </summary>
        MatchIntent ChooseEffectChoice(SeatView view, int seat)
        {
            PendingChoiceView pending = view.PendingChoice;

            // Sorted by what is worth keeping, but answered by index in the reveal — so the indices are carried
            // through the sort rather than the cards.
            List<int> order = new List<int>(pending.Revealed.Count);
            for (int ndx = 0; ndx < pending.Revealed.Count; ndx++)
                order.Add(ndx);

            order.Sort((a, b) => CompareByKeepValue(pending.Revealed[a], pending.Revealed[b]));

            int       keepCount = pending.KeepCount < order.Count ? pending.KeepCount : order.Count;
            List<int> keep      = order.GetRange(0, keepCount);
            keep.Sort();

            return new EffectChoiceIntent(view.Rules.PendingChoice.Id, keep);
        }

        /// <summary>
        /// Costliest first. The cost compared is the card's printed cost at its rank rather than what it would
        /// cost to cast right now, because a Weather's discount is a fact about this turn and the peek is a
        /// judgement about the card.
        /// </summary>
        int CompareByKeepValue(HandCard a, HandCard b)
        {
            int byCost = PrintedCost(b).CompareTo(PrintedCost(a));
            if (byCost != 0)
                return byCost;

            int byCard = CardInfo.CompareCanonical(a.Card, b.Card);
            if (byCard != 0)
                return byCard;

            return a.Instance.CompareTo(b.Instance);
        }

        int PrintedCost(HandCard view) => Card(view.Card).GetStatsAtRank(view.Rank).Cost;

        /// <summary> What a held card costs this seat right now, from the one rule that answers that. </summary>
        int CostOf(SeatView view, int seat, HandCard held)
            => ManaRules.CostToPlay(_config, view.Rules, seat, held);

        CardInfo Card(CardId id) => _config.Cards[id];
    }
}
