using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The public half of every zone move. Every move through here maintains the stored counts, the unseen
    /// pool, the caps and the events — so combat, the effect interpreter and the rule bodies all get the same
    /// invariants without any of them knowing about them (<c>Docs/rules.md</c>, "Zones").
    /// <para>
    /// <b>Three properties every op here has, and they are the whole public/secret line:</b>
    /// </para>
    /// <list type="number">
    /// <item><b>Every branch is on a public value.</b> No <c>if</c> in this file reads a secret. That is what
    /// the stored <see cref="SeatState.HandCount"/> and <see cref="SeatState.DeckCount"/> are for: a
    /// follower has no list to count.</item>
    /// <item><b>Each secret excursion is one named <see cref="SecretOps"/> call and is a no-op on a
    /// follower.</b> It moves nothing public.</item>
    /// <item><b>The public mutation and the event are outside the excursion</b>, so they run identically on
    /// both sides.</item>
    /// </list>
    /// <para>
    /// A seat's <em>membership</em> lists live in its secret, whether the card in them is hidden or public — a
    /// public card in a hand is still in that hand, and the hand views and the legal set are built off that
    /// list. So an op that places a card in a hand or a deck makes the excursion for the list and does the
    /// counting itself. What it never does is <b>choose</b> between the two hidden zones by looking: the two
    /// rules that move a card between them — the mulligan and the peek — pair a public count computation with
    /// one secret partition, in <see cref="MulliganRules"/> and <see cref="ChoiceRules"/>.
    /// </para>
    /// </summary>
    public static class ZoneOps
    {
        // ---------------------------------------------------------------- minting

        /// <summary>
        /// Add a card to the match. Deal identities are minted over the authored deck lists before the
        /// shuffle; instances created during play take the next identity, which is safe because they are
        /// created by public events in a public order — so a follower mints the same instance from the same
        /// config at the same registry index.
        /// <para>
        /// A mint with <paramref name="isPublic"/> false is reachable only from <see cref="Deal.Create"/>,
        /// which runs before there is a timeline; every mint an <em>action</em> makes is public, which is
        /// what keeps a follower able to make it too.
        /// </para>
        /// </summary>
        public static CardInstance Mint(MatchModel match, CardInfo card, int rank, int owner, CardPlace place, bool isPublic, bool fromStartingDeck)
        {
            MatchRulesState rules    = match.Rules;
            CardInstanceId  id       = new CardInstanceId(rules.Instances.Count);
            CardInstance    instance = new CardInstance(id, owner, place, isPublic, fromStartingDeck);
            rules.Instances.Add(instance);

            if (isPublic)
                instance.Reveal(MetaRef<CardInfo>.FromItem(card), rank);
            else
                SecretOps.RememberHiddenIdentity(match, owner, id, MetaRef<CardInfo>.FromItem(card), rank);

            return instance;
        }

        /// <summary>
        /// Fill in what a hidden instance is, from an identity the action's own payload carried. This is the
        /// <b>one place</b> a hidden identity becomes public, and the payload is the only way it can get
        /// here: a follower has no other source for it, which is why <see cref="MatchPlayCard"/> is the only
        /// action that carries one (<c>Docs/hidden-information.md</c>).
        /// </summary>
        public static void Reveal(MatchModel match, CardInstance instance, CardId card, int rank)
        {
            if (instance.IsKnown)
                return;

            if (!match.Content.Cards.TryGetValue(card, out CardInfo info))
                throw new MatchEngineException($"A revealed identity names '{card}', which is not in the catalogue");

            instance.Reveal(MetaRef<CardInfo>.FromItem(info), rank);

            // One answer, never two that could disagree: the registry holds it now.
            SecretOps.ForgetHiddenIdentity(match, instance.Id);
        }

        // ---------------------------------------------------------------- the unseen pool

        /// <summary>
        /// The card has become public: played, destroyed, or revealed to everybody. It leaves its owner's
        /// unseen pool permanently and does not re-enter it when a bounce puts it back in a hand.
        /// <para>
        /// The entry must already know what the card is. Every caller reaches here through a mint that was
        /// public or a <see cref="Reveal"/> from a payload, so a hidden instance arriving here is a rule body
        /// that forgot to reveal it — loud rather than a pool that quietly stops shrinking.
        /// </para>
        /// </summary>
        public static void MakePublic(MatchModel match, CardInstance instance)
        {
            if (instance.IsPublic)
                return;

            if (!instance.IsKnown)
                throw new MatchEngineException($"{instance.Id} became public without its identity being revealed first");

            instance.MakePublic();

            SeatState seat   = match.Rules.Seat(instance.Owner);
            CardId    cardId = instance.CardId;
            int       ndx    = seat.UnseenPool.IndexOf(cardId);
            if (ndx >= 0)
            {
                seat.UnseenPool.RemoveAt(ndx);
                match.Emit(new UnseenPoolChangedEvent(instance.Owner, new List<CardId> { cardId }));
            }
        }

        /// <summary> Put the pool in canonical card order, which is the order everything deterministic uses. </summary>
        public static void SortPool(List<CardId> pool) => pool.Sort(CardInfo.CompareCanonical);

        // ---------------------------------------------------------------- drawing

        /// <summary>
        /// One draw, with both refusals. A draw that cannot be taken costs the seat a Tuckered Out tick, and
        /// a full hand is a way of not taking one — without that, a seat holding nine cards never empties its
        /// deck and the game has no clock at all (<c>Docs/game-design.md</c>, "Ending the game").
        /// <para>
        /// This is the case the whole stored-count decision exists for: every branch below reads a public
        /// number, and the drawn card's identity never enters the public side at all — which is why no rule
        /// may branch on <em>what</em> was drawn (there is no on-draw trigger, and the first one would need a
        /// payload).
        /// </para>
        /// </summary>
        public static void Draw(MatchModel match, int seat)
        {
            SeatState state = match.Rules.Seat(seat);

            if (state.DeckCount == 0)
            {
                TuckeredOutTick(match, seat);
                return;
            }

            if (state.HandCount >= match.Content.Global.MaxHandSize)
            {
                // The card is never burned: it goes to the bottom. The seat still takes the tick.
                SecretOps.MoveTopOfDeckToBottom(match, seat);
                match.Emit(new DrawOverflowedEvent(seat, state.HandCount, state.DeckCount));
                TuckeredOutTick(match, seat);
                return;
            }

            SecretOps.MoveTopOfDeckToHand(match, seat);
            state.SetCounts(state.HandCount + 1, state.DeckCount - 1);
            match.Emit(new CardDrawnEvent(seat, state.HandCount, state.DeckCount));
        }

        // ---------------------------------------------------------------- hands, decks and the board

        /// <summary>
        /// Put a <b>public</b> card into its owner's hand — a bounce's landing, a graveyard copy. A full hand
        /// sends it to the bottom of the owner's deck instead. No Tuckered Out tick: the clock is about
        /// draws a seat could not take, and this is not a draw.
        /// </summary>
        public static void AddToHand(MatchModel match, CardInstance instance)
        {
            int       seat  = instance.Owner;
            SeatState state = match.Rules.Seat(seat);

            // Out of wherever it was first: a card that reached a hand while still listed in a deck would be
            // in two zones at once, which nothing downstream checks for.
            RemoveFromCurrentPlace(match, instance);
            MakePublic(match, instance);

            if (PlaceInHandOrOverflow(match, instance))
                match.Emit(new CardAddedToHandEvent(seat, state.HandCount, instance.Id, instance.CardId));
        }

        /// <summary>
        /// Put a public card in its owner's hand, or on the bottom of the deck with an overflow event when the
        /// hand is full. True when it reached the hand.
        /// </summary>
        static bool PlaceInHandOrOverflow(MatchModel match, CardInstance instance)
        {
            int       seat  = instance.Owner;
            SeatState state = match.Rules.Seat(seat);

            if (state.HandCount >= match.Content.Global.MaxHandSize)
            {
                SecretOps.MoveToBottom(match, instance.Id);
                instance.SetPlace(CardPlace.Deck);
                state.SetCounts(state.HandCount, state.DeckCount + 1);
                match.Emit(new DrawOverflowedEvent(seat, state.HandCount, state.DeckCount));
                return false;
            }

            SecretOps.MoveToHand(match, instance.Id);
            instance.SetPlace(CardPlace.Hand);
            state.SetCounts(state.HandCount + 1, state.DeckCount);
            return true;
        }

        /// <summary>
        /// Put a critter into play. The caller has already checked the board cap: a play is refused
        /// <c>BoardFull</c> and an effect summon fizzles, and neither reaches here.
        /// </summary>
        public static BoardCritter ToBoard(MatchModel match, CardInstance instance)
        {
            SeatState state = match.Rules.Seat(instance.Owner);
            CardInfo  card  = instance.Info;
            CardStats stats = instance.Stats;

            KeywordFlags keywords = card.GetKeywordFlags() | WeatherModifiers.AuraKeywords(match.Rules.Weather?.Ref);
            bool         sleepy   = (keywords & KeywordFlags.Zoomies) == 0;

            RemoveFromCurrentPlace(match, instance);
            instance.SetPlace(CardPlace.Board);
            MakePublic(match, instance);

            BoardCritter critter = new BoardCritter(instance.Id, stats.Attack, stats.Health, keywords, sleepy);
            state.Board.Add(critter);

            match.Emit(new CritterEnteredPlayEvent(instance.Owner, instance.Id, instance.CardId, critter.Attack, critter.MaxHealth, critter.Keywords, critter.IsSleepy));
            return critter;
        }

        /// <summary> Send a card to its owner's graveyard, which is public and in arrival order. </summary>
        public static void ToGraveyard(MatchModel match, CardInstance instance)
        {
            SeatState state = match.Rules.Seat(instance.Owner);
            RemoveFromCurrentPlace(match, instance);
            instance.SetPlace(CardPlace.Graveyard);
            MakePublic(match, instance);
            state.Graveyard.Add(instance.Id);
        }

        /// <summary> Return a critter to its owner's hand as its catalogue card. It stays public. </summary>
        public static void Bounce(MatchModel match, CardInstanceId critterId)
        {
            CardInstance instance = match.Rules.TryGetInstance(critterId);
            if (instance == null || instance.Place != CardPlace.Board)
                return;

            int       seat  = instance.Owner;
            SeatState state = match.Rules.Seat(seat);
            RemoveCritterFromBoard(state, critterId);
            PlaceInHandOrOverflow(match, instance);
            match.Emit(new CardBouncedEvent(critterId, seat, state.HandCount));
        }

        /// <summary> Take a dead critter off the board. Its instance moves to the graveyard. </summary>
        public static void RemoveDeadCritter(MatchModel match, int seat, CardInstanceId critterId)
        {
            RemoveCritterFromBoard(match.Rules.Seat(seat), critterId);
            ToGraveyard(match, match.Rules.Instance(critterId));
        }

        static void RemoveCritterFromBoard(SeatState state, CardInstanceId critterId)
        {
            for (int ndx = 0; ndx < state.Board.Count; ndx++)
            {
                if (state.Board[ndx].Id == critterId)
                {
                    state.Board.RemoveAt(ndx);
                    return;
                }
            }
        }

        /// <summary>
        /// Take a card out of the place the registry says it is in. Every branch is a public list or a public
        /// count.
        /// <para>
        /// <see cref="CardPlace.Unseen"/> cannot arrive here, and it throws rather than no-opping: the two
        /// rules that move a hidden card — the mulligan and the peek — pair their own public count
        /// arithmetic with one <see cref="SecretOps"/> partition, so a hidden card reaching this method is a
        /// rule body that took the public route to a secret zone. Silently no-opping would leave the counts
        /// wrong, which is a desync one checksum period later with nothing naming the cause.
        /// </para>
        /// </summary>
        static void RemoveFromCurrentPlace(MatchModel match, CardInstance instance)
        {
            SeatState state = match.Rules.Seat(instance.Owner);
            switch (instance.Place)
            {
                case CardPlace.Unseen:
                    throw new MatchEngineException($"{instance.Id} is in a hidden zone; only SecretOps moves one out of it");

                case CardPlace.Hand:
                    SecretOps.RemoveFromHiddenZones(match, instance.Id);
                    state.SetCounts(state.HandCount - 1, state.DeckCount);
                    break;

                case CardPlace.Deck:
                    SecretOps.RemoveFromHiddenZones(match, instance.Id);
                    state.SetCounts(state.HandCount, state.DeckCount - 1);
                    break;

                case CardPlace.Board:
                    RemoveCritterFromBoard(state, instance.Id);
                    break;

                case CardPlace.Graveyard:
                    // Nothing leaves a graveyard today, but a zone move that forgot to take the card out of
                    // the one it was in would put it in two at once, which is the bug this exists to
                    // prevent.
                    state.Graveyard.Remove(instance.Id);
                    break;
            }
        }

        // ---------------------------------------------------------------- the clock

        /// <summary>
        /// One Tuckered Out tick. The counter is per seat, monotonic, and never resets; the damage
        /// schedule is a config knob, which is what makes the clock's pace something somebody can change.
        /// </summary>
        public static void TuckeredOutTick(MatchModel match, int seat)
        {
            SeatState state = match.Rules.Seat(seat);
            int       tick  = state.TuckeredOutTicks + 1;
            state.SetTuckeredOutTicks(tick);

            int damage = match.Content.Global.TuckeredOutFirstDamage + (tick - 1) * match.Content.Global.TuckeredOutIncrement;
            match.Emit(new TuckeredOutEvent(seat, tick, damage));
            ResolutionRules.DamageDen(match, seat, damage, CardInstanceId.None);
        }
    }
}
