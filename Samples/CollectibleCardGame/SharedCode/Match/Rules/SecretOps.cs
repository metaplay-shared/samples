using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The only reader and writer of the match's hidden half. Every method here begins by resolving
    /// <see cref="MatchModel.SecretSeat"/> — or <see cref="MatchModel.Secret"/> — and returning on null, so a
    /// follower runs every one of them as a no-op. That is what makes "the public side of every action is
    /// identical on both sides" true rather than hoped for.
    /// <para>
    /// <b>Two methods are deliberately not guard-first</b>, and they are named here so the sentence above
    /// stays true rather than nearly true. <see cref="Create"/> assigns the secret rather than reading one,
    /// so there is nothing to guard against. <see cref="NextInt"/> throws on a follower, and that is the
    /// better trade of the two available: a guard would have it return a deterministic wrong answer, and a
    /// wrong answer from the stream is a game that diverges quietly, while a throw inside an action is loud
    /// on both sides — the mirror reports it as a divergence, which is negative control NC2. Both of its call
    /// sites are in <see cref="Deal.Create"/>, which runs before there is a timeline to replicate.
    /// </para>
    /// <para>
    /// Two consequences worth stating, because they are the shape of the whole design:
    /// </para>
    /// <list type="bullet">
    /// <item>A rule body reads as a public flow with <b>named excursions</b> into this type. A reviewer finds
    /// every excursion by grepping one type name, and every method name says which secret it touches.</item>
    /// <item>Nothing here returns a value a <em>public</em> mutation may branch on. The reads exist for the
    /// hand views, the bot's own seat view, the actor's pre-action checks and the invariant catalog — all
    /// server-side callers. The one number that crosses back into public arithmetic is the count a public
    /// loop computed and handed <em>in</em>.</item>
    /// </list>
    /// <para>
    /// <b>Iteration order.</b> No public mutation may iterate a secret collection;
    /// <see cref="SeatSecrets.Cards"/> is only ever looked up by id.
    /// </para>
    /// </summary>
    public static class SecretOps
    {
        // ---------------------------------------------------------------- moves

        /// <summary>
        /// Take the top card of the seat's deck into its hand. The public counts are the caller's, because
        /// they are the mutation both sides make.
        /// </summary>
        public static void MoveTopOfDeckToHand(MatchModel match, int seat)
        {
            SeatSecrets secret = match.SecretSeat(seat);
            if (secret == null || secret.Deck.Count == 0)
                return;

            CardInstanceId top = secret.Deck[0];
            secret.Deck.RemoveAt(0);
            HandAdd(match, secret, seat, top);
        }

        /// <summary> Move the top card of the seat's deck to the bottom of the same deck. </summary>
        public static void MoveTopOfDeckToBottom(MatchModel match, int seat)
        {
            SeatSecrets secret = match.SecretSeat(seat);
            if (secret == null || secret.Deck.Count == 0)
                return;

            CardInstanceId top = secret.Deck[0];
            secret.Deck.RemoveAt(0);
            secret.Deck.Add(top);
        }

        /// <summary>
        /// Take one named instance out of whichever hidden zone holds it. Which of the two it was in is the
        /// thing the public entry deliberately does not say, which is why this is here and not in
        /// <see cref="ZoneOps"/>.
        /// </summary>
        public static void RemoveFromHiddenZones(MatchModel match, CardInstanceId id)
        {
            if (!TryOwnerOf(match, id, out SeatSecrets secret, out int seat))
                return;

            HandRemove(match, secret, seat, id);
            secret.Deck.Remove(id);
        }

        /// <summary>
        /// The card has become public, so the registry entry now holds the answer and this copy of it is a
        /// second answer that could disagree. Forget it.
        /// </summary>
        public static void ForgetHiddenIdentity(MatchModel match, CardInstanceId id)
        {
            SeatSecrets secret = SecretOf(match, id);
            secret?.Cards.Remove(id);
        }

        /// <summary> Record what a newly minted hidden instance is. Only the deal mints one. </summary>
        public static void RememberHiddenIdentity(MatchModel match, int seat, CardInstanceId id, MetaRef<CardInfo> card, int rank)
        {
            SeatSecrets secret = match.SecretSeat(seat);
            secret?.Cards.Add(id, new HiddenCard(card, rank));
        }

        /// <summary> Put a hidden instance on the bottom of its owner's deck, out of wherever it was. </summary>
        public static void MoveToBottom(MatchModel match, CardInstanceId id)
        {
            if (!TryOwnerOf(match, id, out SeatSecrets secret, out int seat))
                return;

            HandRemove(match, secret, seat, id);
            secret.Deck.Remove(id);
            secret.Deck.Add(id);
        }

        /// <summary> Put a hidden instance into its owner's hand, out of wherever it was. </summary>
        public static void MoveToHand(MatchModel match, CardInstanceId id)
        {
            if (!TryOwnerOf(match, id, out SeatSecrets secret, out int seat))
                return;

            HandRemove(match, secret, seat, id);
            secret.Deck.Remove(id);
            HandAdd(match, secret, seat, id);
        }

        /// <summary> Append an instance to a seat's deck. The deal's only writer. </summary>
        public static void AppendToDeck(MatchModel match, int seat, CardInstanceId id)
        {
            SeatSecrets secret = match.SecretSeat(seat);
            secret?.Deck.Add(id);
        }

        /// <summary> Append an instance to a seat's hand. The deal's only writer. </summary>
        public static void AppendToHand(MatchModel match, int seat, CardInstanceId id)
        {
            SeatSecrets secret = match.SecretSeat(seat);
            if (secret != null)
                HandAdd(match, secret, seat, id);
        }

        // ---------------------------------------------------------------- the hand, and its record

        /// <summary>
        /// Put an instance in a seat's hand <em>and</em> queue the operation that will tell its owner. Every
        /// write to <see cref="SeatSecrets.Hand"/> goes through here or <see cref="HandRemove"/>, so the thing
        /// the owner is told and the thing that happened are written by one statement and cannot disagree.
        /// An instance whose identity cannot be resolved queues nothing: there is nothing to say about it, and
        /// the owner is given a whole fresh hand by <c>GetMemberPrivateState</c> on its next subscribe.
        /// </summary>
        static void HandAdd(MatchModel match, SeatSecrets secret, int seat, CardInstanceId id)
        {
            secret.Hand.Add(id);

            HandCard? card = HandCardOf(match, id);
            if (card.HasValue)
                match.Outbox.Add((seat, new MatchOwnCardGained(card.Value)));
        }

        /// <summary> Take an instance out of a seat's hand and queue that too. Queues nothing if it was not there. </summary>
        static void HandRemove(MatchModel match, SeatSecrets secret, int seat, CardInstanceId id)
        {
            if (secret.Hand.Remove(id))
                match.Outbox.Add((seat, new MatchOwnCardLost(id)));
        }

        /// <summary>
        /// The owner of an instance and that owner's hidden half, from <b>one</b> lookup.
        /// <para>
        /// Both together on purpose. A queued hand operation carries the seat it is for, and the host indexes
        /// its seat table with that — so a seat derived from a second, independent lookup that could disagree
        /// with the first is an out-of-range crash waiting for the day the two answers differ. Answering both
        /// from the same instance makes "there is a secret to move a card in" and "there is a seat to tell"
        /// the same question.
        /// </para>
        /// </summary>
        static bool TryOwnerOf(MatchModel match, CardInstanceId id, out SeatSecrets secret, out int seat)
        {
            CardInstance instance = match.Rules.TryGetInstance(id);
            if (instance == null)
            {
                secret = null;
                seat   = MatchSeats.None;
                return false;
            }

            seat   = instance.Owner;
            secret = match.SecretSeat(seat);
            return secret != null;
        }

        // ---------------------------------------------------------------- reads, server-side by construction

        /// <summary> What a hidden instance is, or null when this side does not know. </summary>
        public static HiddenCard? HiddenCardOf(MatchModel match, CardInstanceId id)
        {
            SeatSecrets secret = SecretOf(match, id);
            if (secret == null)
                return null;

            return secret.Cards.TryGetValue(id, out HiddenCard hidden) ? hidden : (HiddenCard?)null;
        }

        /// <summary>
        /// One card of a hand as both sides describe one: the instance, the card and the rank. Null when the
        /// instance is not in a hand this side can read.
        /// </summary>
        public static HandCard? HandCardOf(MatchModel match, CardInstanceId id)
        {
            CardInstance instance = match.Rules.TryGetInstance(id);
            if (instance == null)
                return null;

            SeatSecrets secret = match.SecretSeat(instance.Owner);
            if (secret == null || !secret.Hand.Contains(id))
                return null;

            if (instance.IsKnown)
                return new HandCard(id, instance.CardId, instance.Rank);

            HiddenCard? hidden = HiddenCardOf(match, id);
            if (hidden == null)
                return null;

            return new HandCard(id, hidden.Value.CardId, hidden.Value.Rank);
        }

        /// <summary> One seat's whole hand in hand order, or an empty list on a follower. </summary>
        public static List<HandCard> HandOf(MatchModel match, int seat)
        {
            List<HandCard> hand   = new List<HandCard>();
            SeatSecrets    secret = match.SecretSeat(seat);
            if (secret == null)
                return hand;

            foreach (CardInstanceId id in secret.Hand)
            {
                HandCard? card = HandCardOf(match, id);
                if (card.HasValue)
                    hand.Add(card.Value);
            }

            return hand;
        }

        /// <summary> The top cards of a seat's deck, top first. Empty on a follower. </summary>
        public static List<CardInstanceId> RevealTopOfDeck(MatchModel match, int seat, int count)
        {
            SeatSecrets secret = match.SecretSeat(seat);
            if (secret == null)
                return new List<CardInstanceId>();

            int take = count < secret.Deck.Count ? count : secret.Deck.Count;
            return secret.Deck.GetRange(0, take);
        }

        /// <summary>
        /// Whether this instance is in this seat's hand. False on a follower, which has no hands; only the
        /// server's intent check asks.
        /// </summary>
        public static bool IsInHand(MatchModel match, int seat, CardInstanceId id)
            => match.SecretSeat(seat)?.Hand.Contains(id) ?? false;

        // ---------------------------------------------------------------- the held peek

        /// <summary> What a held peek revealed to its owner, in reveal order. Empty on a follower. </summary>
        public static List<CardInstanceId> PeekRevealed(MatchModel match, int seat)
            => match.SecretSeat(seat)?.PeekRevealed ?? new List<CardInstanceId>();

        /// <summary> Record what a peek revealed to its owner. Called from inside the peek. </summary>
        public static void WritePeekRevealed(MatchModel match, int seat, List<CardInstanceId> revealed)
            => match.SecretSeat(seat)?.SetPeekRevealed(revealed);

        /// <summary> Forget what a resolved peek revealed. </summary>
        public static void ClearPeekRevealed(MatchModel match, int seat)
            => match.SecretSeat(seat)?.SetPeekRevealed(null);

        /// <summary>
        /// What the choice is when nobody made it: keep the highest-cost cards, breaking ties on canonical
        /// card order then identity — the same rule the bot policy applies, so an absent seat's answer and a
        /// bot's answer are the same answer (<c>Docs/effects.md</c>).
        /// <para>
        /// It is computed here rather than inside the action because it reads the revealed cards' costs,
        /// which are secret. <see cref="EffectChoiceIntent.Default"/> carries the answer into an intent.
        /// </para>
        /// </summary>
        public static List<int> DefaultPeekKeep(MatchModel match, int seat)
        {
            PendingEffectChoice pending = match.Rules.PendingChoice;
            SeatSecrets         secret  = match.SecretSeat(seat);
            if (pending == null || secret?.PeekRevealed == null)
                return new List<int>();

            List<int> order = new List<int>(secret.PeekRevealed.Count);
            for (int ndx = 0; ndx < secret.PeekRevealed.Count; ndx++)
                order.Add(ndx);

            order.Sort((a, b) =>
            {
                CardInstanceId idA   = secret.PeekRevealed[a];
                CardInstanceId idB   = secret.PeekRevealed[b];
                HiddenCard?    cardA = HiddenCardOf(match, idA);
                HiddenCard?    cardB = HiddenCardOf(match, idB);
                if (cardA == null || cardB == null)
                    return a.CompareTo(b);

                int byCost = cardB.Value.Stats.Cost.CompareTo(cardA.Value.Stats.Cost);
                if (byCost != 0)
                    return byCost;

                int byCard = CardInfo.CompareCanonical(cardA.Value.CardId, cardB.Value.CardId);
                if (byCard != 0)
                    return byCard;

                return a.CompareTo(b);
            });

            int keepCount = pending.KeepCount < order.Count ? pending.KeepCount : order.Count;
            order.RemoveRange(keepCount, order.Count - keepCount);
            order.Sort();
            return order;
        }

        /// <summary>
        /// The secret half of a resolved peek. The revealed cards are partitioned in <b>reveal order</b>:
        /// the first <paramref name="keptToHand"/> of the kept ones join the hand and everything else — the
        /// cards the seat declined, and any kept card the public loop found no room for — goes to the bottom
        /// of the deck in reveal order.
        /// <para>
        /// <paramref name="keptToHand"/> is a number the <em>public</em> loop computed from
        /// <c>HandCount</c> and the payload's kept count, which is what keeps this from being a secret input
        /// to a public mutation: the public side decides how many fit and this side decides which they are.
        /// The trailing kept cards are the ones that overflow because a hand only grows, so once it is full
        /// it stays full.
        /// </para>
        /// </summary>
        public static void ApplyPeekChoice(MatchModel match, int seat, int keptToHand, List<int> keep)
        {
            SeatSecrets secret = match.SecretSeat(seat);
            if (secret?.PeekRevealed == null)
                return;

            List<CardInstanceId> revealed = secret.PeekRevealed;
            keep ??= new List<int>();

            // The answer names indices in the reveal; this is the only place that turns one back into a
            // card, and it is the only side that can. Reveal order is the shared frame: the owner's view was
            // built by walking the same list in the same order.
            List<CardInstanceId> toHand = new List<CardInstanceId>(keptToHand);
            for (int ndx = 0; ndx < revealed.Count && toHand.Count < keptToHand; ndx++)
            {
                if (keep.Contains(ndx))
                    toHand.Add(revealed[ndx]);
            }

            // Everything that did not reach the hand goes to the bottom, in reveal order.
            foreach (CardInstanceId id in revealed)
            {
                if (toHand.Contains(id))
                    MoveToHand(match, id);
                else
                    MoveToBottom(match, id);
            }
        }

        /// <summary>
        /// One seat's mulligan: put the named cards on the bottom of its deck, reshuffle, and redraw as many.
        /// The hand and deck counts net to zero, which is why the caller has none to update. Reshuffled before
        /// the redraw, so a redrawn card may be one just returned. A no-op on a follower, which has no hand to
        /// swap and receives the new cards as addressed operations.
        /// </summary>
        public static void ReplaceInMulligan(MatchModel match, int seat, List<CardInstanceId> replace)
        {
            SeatSecrets secret = match.SecretSeat(seat);
            if (secret == null || replace == null)
                return;

            int replaced = 0;
            foreach (CardInstanceId id in replace)
            {
                if (!secret.Hand.Contains(id))
                    continue;

                HandRemove(match, secret, seat, id);
                secret.Deck.Add(id);
                replaced++;
            }

            if (replaced == 0)
                return;

            ShuffleDeck(match, seat);

            for (int ndx = 0; ndx < replaced; ndx++)
                MoveTopOfDeckToHand(match, seat);
        }

        // ---------------------------------------------------------------- the stream

        /// <summary>
        /// Mint the match's secret: the seeded stream, the independent bot seed, and an empty hidden half per
        /// seat. The deal's first act, and the only place a <see cref="MatchSecrets"/> is created.
        /// </summary>
        public static void Create(MatchModel match, ulong dealSeed, ulong botSeed)
            => match.Secret = new MatchSecrets(RandomPCG.CreateFromSeed(dealSeed), botSeed);

        /// <summary>
        /// The match's independent bot seed, or zero on a follower. The actor drives bot seats with it.
        /// </summary>
        public static ulong BotSeedOf(MatchModel match) => match.Secret == null ? 0ul : match.Secret.BotSeed;

        /// <summary>
        /// One draw from the match's stream, bounded. The deal's two public choices — which Weather, and who
        /// goes first — come from here, which is why they are reads of the <em>secret</em> even though their
        /// answers are public: the stream's position is as secret as the seed.
        /// </summary>
        public static int NextInt(MatchModel match, int bound) => match.Secret.Rng.NextInt(bound);

        /// <summary>
        /// Shuffle one seat's deck with the match's stream. The deal and the mulligan's reshuffle are its only
        /// two consumers, and both run on the server: a follower has no stream and must never advance one.
        /// </summary>
        public static void ShuffleDeck(MatchModel match, int seat)
        {
            SeatSecrets secret = match.SecretSeat(seat);
            if (secret == null)
                return;

            match.Secret.Rng.ShuffleInPlace(secret.Deck);
        }

        // ---------------------------------------------------------------- the one resolution

        static SeatSecrets SecretOf(MatchModel match, CardInstanceId id)
        {
            CardInstance instance = match.Rules.TryGetInstance(id);
            return instance == null ? null : match.SecretSeat(instance.Owner);
        }
    }
}
