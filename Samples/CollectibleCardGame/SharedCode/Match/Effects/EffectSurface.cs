using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The rules' own side of the effect boundary: the read surface and the write surface an interpreter is
    /// handed, both backed by the live game. One object implements both because they are two views of one
    /// mutation, and it is handed out only for the duration of a single step.
    /// <para>
    /// Nothing on the read half is writable and nothing else is handed over, which is the clause the whole
    /// boundary rests on: an interpreter that could write a zone directly would be able to break the pool, the
    /// caps and the event stream at once, and the leak would be invisible.
    /// </para>
    /// </summary>
    sealed class EffectSurface : IEffectContext, IEffectMutations
    {
        readonly MatchModel _match;
        EffectQueueItem     _item;

        internal EffectSurface(MatchModel match)
        {
            _match = match;
        }

        internal void Begin(EffectQueueItem item) => _item = item;
        internal void End() => _item = null;

        MatchRulesState Rules => _match.Rules;

        // ---------------------------------------------------------------- read surface

        public SharedGameConfig Config => _match.Content;

        public IReadOnlyList<BoardCritter> Board(int seat) => Rules.Seat(seat).Board;

        public BoardCritter CritterAt(CardInstanceId id) => ResolutionRules.FindCritterAnywhere(Rules, id);

        /// <summary> What a public instance is, or null for a hidden one on either side. </summary>
        public CardInfo CardOf(CardInstanceId id)
        {
            CardInstance instance = Rules.TryGetInstance(id);
            return instance != null && instance.IsKnown ? instance.Info : null;
        }

        public IReadOnlyList<CardInstanceId> Graveyard(int seat)       => Rules.Seat(seat).Graveyard;
        public IReadOnlyList<CardId>         PlayedThisMatch(int seat) => Rules.Seat(seat).PlayedThisMatch;

        // ---------------------------------------------------------------- write surface

        public void DealDamage(EffectTargetRef target, int amount)
        {
            // What the source actually dealt, not what the step asked for: a step whose target had already
            // died is a no-op, and a no-op neither feeds a Snacktime Den nor reveals a Sneaky critter.
            int dealt = ResolutionRules.ApplyDamage(_match, target, amount, _item.Source.Id);
            if (dealt <= 0)
                return;

            // Snacktime applies to any damage the critter sources, including its own triggered effect.
            BoardCritter dealer = ResolutionRules.FindCritterAnywhere(Rules, _item.Source.Id);
            if (dealer == null)
                return;

            CombatRules.ApplySnacktime(_match, dealer, _item.ResolvingSeat, dealt);
            CombatRules.RevealIfSneaky(_match, dealer, dealt);
        }

        public void Heal(EffectTargetRef target, int amount) => ResolutionRules.ApplyHeal(_match, target, amount);

        public void Draw(int seat, int count) => ResolutionRules.DrawCards(_match, seat, count);

        public void GainMana(int seat, int amount, ManaDuration duration) => ResolutionRules.GainMana(_match, seat, amount, duration);

        public void Buff(CardInstanceId critter, int attackDelta, int maxHealthDelta) => ResolutionRules.BuffCritter(_match, critter, attackDelta, maxHealthDelta);

        public void GrantKeywords(CardInstanceId critter, KeywordFlags keywords) => ResolutionRules.GrantKeywordsTo(_match, critter, keywords);

        public void Summon(int seat, CardInfo card, int count) => ResolutionRules.SummonCritters(_match, seat, card, count);

        public void Bounce(CardInstanceId critter) => ResolutionRules.BounceCritter(_match, critter);

        public void AddCopyToHand(int seat, CardInfo card) => ResolutionRules.AddCopyToHand(_match, seat, card);

        public void PeekDeck(int seat, int lookCount, int keepCount) => ChoiceRules.PeekDeck(_match, seat, lookCount, keepCount);
    }
}
