using Metaplay.Core;
using Metaplay.Core.Serialization;
using System.Collections.Generic;
using System.IO;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The machinery the secrecy proof is built on: taking an authoritative model and re-dealing everything a
    /// given seat is not allowed to see, so the two models are two deals that seat cannot tell apart
    /// (<c>Docs/hidden-information.md</c>, "What keeps it honest").
    /// <para>
    /// The equivalence class is that doc's: the seat's own hand, both boards, both graveyards, both unseen
    /// pools as multisets, both counts, the Weather and the action history are held fixed, while both deck
    /// orders and the split of the opponent's pool between deck and hand move freely.
    /// </para>
    /// <para>
    /// <b>What the compared payload is now, and what that costs.</b> Its first term is
    /// <c>SerializeTagged(model, SendOverNetwork)</c> — the actual wire mask rather than a hand-built
    /// projection, which is stronger. It also makes the positive case close to a tautology: the mask strips
    /// the secret and what is left is byte-identical by construction. That is not a weakness, it is the
    /// <em>statement</em> of the new design — but it does move the weight of the proof onto two other things.
    /// The <b>negative controls</b> are what say the comparison can see a leak at all, and they are now the
    /// load-bearing half of this fixture.
    /// </para>
    /// <para>
    /// <b>What this sweep covers, exactly.</b> Three things, and it is worth being precise because the
    /// obvious larger claim is false: a re-deal mutates the <em>secret</em> half of a clone of an
    /// already-played state, and every public member is <em>stored</em>, so both members of the pair carry
    /// byte-identical public state by construction. What the sweep therefore checks is everything the
    /// fixture <em>re-derives</em> after the mutation — <see cref="SeatState.UnseenPool"/>, the observing
    /// seat's delivered hand (<c>SecretOps.HandOf</c>) and its legal set
    /// (<c>Legality.EnumerateLegalIntents</c>). A stored public member that had been written from hidden
    /// state would survive this comparison untouched.
    /// </para>
    /// <para>
    /// That member is the <b>follower mirror's</b> job, and it does catch it: a leak encoded into a stored
    /// public member is a checksum difference on the first action that writes it
    /// (<c>Docs/bots.md</c>, "Follower-offline dual execution"). The two checks are not two views
    /// of one property — they cover different halves of it, and neither is redundant.
    /// </para>
    /// </summary>
    public static class HiddenState
    {
        public static MatchModel Clone(MatchModel model, SharedGameConfig config)
        {
            MatchModel clone = MetaSerialization.CloneTagged(model, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: config);
            clone.GameConfig = config;
            return clone;
        }

        public static MatchEngine Wrap(MatchModel model) => MatchEngine.Wrap(model);

        // ---------------------------------------------------------------- the re-deal

        /// <summary>
        /// Re-deal the hidden half. Both deck orders are reshuffled and the opponent's unseen cards are
        /// re-split between their deck and their hand, keeping every count the same. Returns false when the
        /// position has nothing hidden left to move, which is a position the check cannot say anything about
        /// rather than a failure.
        /// <para>
        /// There is <b>no zone rewrite any more</b>, and its absence is the point: every hidden card's public
        /// entry already says <see cref="CardPlace.Unseen"/>, so moving a card between a deck and a hand
        /// changes nothing public by construction rather than by a fixture remembering to keep it in step.
        /// That is the model-shape half of the subtraction rule.
        /// </para>
        /// </summary>
        public static bool Redeal(MatchModel match, int observingSeat, ulong subSeed)
        {
            if (match.Rules.PendingChoice != null)
                return false;

            RandomPCG   rng       = RandomPCG.CreateFromSeed(subSeed | 1ul);
            int         enemySeat = MatchSeats.Other(observingSeat);
            SeatSecrets own       = match.SecretSeat(observingSeat);
            SeatSecrets enemy     = match.SecretSeat(enemySeat);

            // The observing seat's own deck order is secret from the observing seat too: the seat view has no
            // field for it, which is exactly what makes moving it part of the class.
            rng.ShuffleInPlace(own.Deck);

            List<CardInstanceId> hiddenInHand = Hidden(match, enemy.Hand);
            List<CardInstanceId> hiddenInDeck = Hidden(match, enemy.Deck);
            List<CardInstanceId> publicInHand = Public(match, enemy.Hand);
            List<CardInstanceId> publicInDeck = Public(match, enemy.Deck);

            if (hiddenInHand.Count == 0 || hiddenInDeck.Count == 0)
                return false;

            List<CardInstanceId> pool = new List<CardInstanceId>(hiddenInHand);
            pool.AddRange(hiddenInDeck);
            rng.ShuffleInPlace(pool);

            enemy.Hand.Clear();
            enemy.Hand.AddRange(publicInHand);
            for (int ndx = 0; ndx < hiddenInHand.Count; ndx++)
                enemy.Hand.Add(pool[ndx]);

            enemy.Deck.Clear();
            enemy.Deck.AddRange(publicInDeck);
            for (int ndx = hiddenInHand.Count; ndx < pool.Count; ndx++)
                enemy.Deck.Add(pool[ndx]);

            rng.ShuffleInPlace(enemy.Deck);

            // The pool is re-derived rather than carried over, even though moving a card between a deck and a
            // hand is not supposed to change it. Carrying it over would mean the mutated model published the
            // *unmutated* model's pool — so a pool that had quietly become a function of deck order or of the
            // deck/hand split would match itself and the sweep would report no leak.
            for (int side = 0; side < MatchSeats.Count; side++)
            {
                List<CardId> published = match.Rules.Seat(side).UnseenPool;
                published.Clear();
                published.AddRange(SecretDerivations.DeriveUnseenPool(match, side));
            }

            // And the stream moves too. hidden-information.md lists "a leftover RNG position" among the things
            // byte-equality is supposed to catch, and it can only catch it if the two models disagree about
            // where the stream is: a payload carrying the position would otherwise match itself across every
            // comparison the sweep makes. The position is secret for the same reason the seed is — the stream
            // replays forward in closed form.
            int advance = 1 + (int)(subSeed % 7ul);
            for (int step = 0; step < advance; step++)
                match.Secret.Rng.NextULong();

            return true;
        }

        /// <summary> Whether two models really did end up with the opponent holding a different hand. </summary>
        public static bool EnemyHandDiffers(MatchModel a, MatchModel b, int enemySeat)
            => !SameCardMultiset(a, a.SecretSeat(enemySeat).Hand, b, b.SecretSeat(enemySeat).Hand);

        /// <summary> Whether two models really did end up with a different deck left to draw. </summary>
        public static bool EnemyDeckDiffers(MatchModel a, MatchModel b, int enemySeat)
            => !SameCardMultiset(a, a.SecretSeat(enemySeat).Deck, b, b.SecretSeat(enemySeat).Deck);

        static bool SameCardMultiset(MatchModel a, List<CardInstanceId> left, MatchModel b, List<CardInstanceId> right)
        {
            if (left.Count != right.Count)
                return false;

            List<CardId> sortedLeft  = SortedCards(a, left);
            List<CardId> sortedRight = SortedCards(b, right);

            for (int ndx = 0; ndx < sortedLeft.Count; ndx++)
            {
                if (sortedLeft[ndx] != sortedRight[ndx])
                    return false;
            }

            return true;
        }

        /// <summary> A zone as the multiset of catalogue cards in it, in canonical order. </summary>
        public static List<CardId> SortedCards(MatchModel match, List<CardInstanceId> zone)
        {
            List<CardId> cards = new List<CardId>(zone.Count);
            foreach (CardInstanceId id in zone)
                cards.Add(CardLookup.CardId(match, id));

            cards.Sort(CardInfo.CompareCanonical);
            return cards;
        }

        static List<CardInstanceId> Hidden(MatchModel match, List<CardInstanceId> zone)
        {
            List<CardInstanceId> found = new List<CardInstanceId>();
            foreach (CardInstanceId id in zone)
            {
                if (!match.Rules.Instance(id).IsPublic)
                    found.Add(id);
            }
            return found;
        }

        static List<CardInstanceId> Public(MatchModel match, List<CardInstanceId> zone)
        {
            List<CardInstanceId> found = new List<CardInstanceId>();
            foreach (CardInstanceId id in zone)
            {
                if (match.Rules.Instance(id).IsPublic)
                    found.Add(id);
            }
            return found;
        }

        // ---------------------------------------------------------------- the draw-order plant

        /// <summary>
        /// Re-mint every identity in draw order rather than in the canonical, shuffle-independent order over
        /// the authored deck lists — the exact mistake <c>Docs/hidden-information.md</c> names ("an
        /// address that encodes where the card sat in the shuffled deck is the deck order in disguise").
        /// <para>
        /// Draw order here is "what has been drawn, then what is left to draw": each seat's hand, board and
        /// graveyard, then its deck top to bottom. That makes an identity a function of the shuffle, which is
        /// the property the control exists to demonstrate is absent from the real mint.
        /// </para>
        /// </summary>
        public static void RelabelInDrawOrder(MatchModel match)
        {
            MatchRulesState      rules = match.Rules;
            List<CardInstanceId> order = new List<CardInstanceId>(rules.Instances.Count);

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                SeatState   state  = rules.Seat(seat);
                SeatSecrets secret = match.SecretSeat(seat);

                order.AddRange(secret.Hand);
                foreach (BoardCritter critter in state.Board)
                    order.Add(critter.Id);
                order.AddRange(state.Graveyard);
                order.AddRange(secret.Deck);
            }

            // Anything in limbo has no zone to be walked from; it keeps a slot at the end so the registry
            // stays dense. A card can also appear twice in the walk — a public hand card is in the secret
            // hand list as well — so the first placement wins.
            bool[]               placed  = new bool[rules.Instances.Count];
            List<CardInstanceId> deduped = new List<CardInstanceId>(order.Count);
            foreach (CardInstanceId id in order)
            {
                if (placed[id.Value])
                    continue;

                placed[id.Value] = true;
                deduped.Add(id);
            }

            for (int ndx = 0; ndx < placed.Length; ndx++)
            {
                if (!placed[ndx])
                    deduped.Add(new CardInstanceId(ndx));
            }

            order = deduped;

            int[] oldToNew = new int[rules.Instances.Count];
            for (int newNdx = 0; newNdx < order.Count; newNdx++)
                oldToNew[order[newNdx].Value] = newNdx;

            List<CardInstance> relabelled = new List<CardInstance>(order.Count);
            for (int newNdx = 0; newNdx < order.Count; newNdx++)
            {
                CardInstance instance = rules.Instance(order[newNdx]);
                relabelled.Add(instance.IsKnown
                    ? new CardInstance(new CardInstanceId(newNdx), instance.Card, instance.Rank, instance.Owner, instance.Place, instance.IsPublic, instance.FromStartingDeck)
                    : new CardInstance(new CardInstanceId(newNdx), instance.Owner, instance.Place, instance.IsPublic, instance.FromStartingDeck));
            }

            rules.Instances.Clear();
            rules.Instances.AddRange(relabelled);

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                SeatState   state  = rules.Seat(seat);
                SeatSecrets secret = match.SecretSeat(seat);

                Remap(secret.Deck, oldToNew);
                Remap(secret.Hand, oldToNew);
                Remap(state.Graveyard, oldToNew);
                if (secret.PeekRevealed != null)
                    Remap(secret.PeekRevealed, oldToNew);

                // The hidden identities are keyed on the instance, so the keys move with it.
                List<CardInstanceId> keys  = new List<CardInstanceId>();
                List<HiddenCard>     cards = new List<HiddenCard>();
                foreach ((CardInstanceId id, HiddenCard card) in secret.Cards)
                {
                    keys.Add(new CardInstanceId(oldToNew[id.Value]));
                    cards.Add(card);
                }

                secret.Cards.Clear();
                for (int ndx = 0; ndx < keys.Count; ndx++)
                    secret.Cards.Add(keys[ndx], cards[ndx]);

                List<BoardCritter> board = new List<BoardCritter>(state.Board.Count);
                foreach (BoardCritter critter in state.Board)
                {
                    board.Add(new BoardCritter(
                        new CardInstanceId(oldToNew[critter.Id.Value]),
                        critter.Attack,
                        critter.MaxHealth,
                        critter.Damage,
                        critter.Keywords,
                        critter.IsSleepy,
                        critter.HasAttackedThisTurn,
                        critter.BubbleIntact));
                }

                state.Board.Clear();
                state.Board.AddRange(board);
            }
        }

        static void Remap(List<CardInstanceId> zone, int[] oldToNew)
        {
            for (int ndx = 0; ndx < zone.Count; ndx++)
                zone[ndx] = new CardInstanceId(oldToNew[zone[ndx].Value]);
        }

        // ---------------------------------------------------------------- what a seat receives

        /// <summary>
        /// Everything that reaches one seat, as one buffer: <b>the bytes the SDK would actually send</b>, that
        /// seat's own hand as the private channel would deliver it, and the legal set the rules produced for
        /// it. Comparing these as bytes is the claim <c>Docs/hidden-information.md</c> makes — strictly
        /// stronger than "names no card it shouldn't", and the only form that catches a count in the wrong
        /// place or an ordering that should have been a multiset.
        /// </summary>
        public static byte[] SeatPayload(MatchEngine engine, int seat)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Append(stream, Wire(engine.Model));
                Append(stream, SelfPlayBytes.Of(SecretOps.HandOf(engine.Model, seat)));
                foreach (MatchIntent action in engine.EnumerateLegalIntents(seat))
                    Append(stream, SelfPlayBytes.Of(action));

                return stream.ToArray();
            }
        }

        /// <summary>
        /// The same buffer with one seat's public state replaced: what a negative control plants. The model is
        /// cloned so the plant cannot leak into the game being played, and then serialized through the same
        /// mask — which is what makes a control's buffer byte-comparable with the real one rather than merely
        /// similar to it.
        /// </summary>
        public static byte[] PayloadWithSeatState(MatchEngine engine, int seat, int plantedSeat, SeatState planted)
        {
            MatchModel broken = Clone(engine.Model, engine.Config);
            broken.Rules.Seats[plantedSeat] = planted;

            using (MemoryStream stream = new MemoryStream())
            {
                Append(stream, Wire(broken));
                Append(stream, SelfPlayBytes.Of(SecretOps.HandOf(engine.Model, seat)));
                foreach (MatchIntent action in engine.EnumerateLegalIntents(seat))
                    Append(stream, SelfPlayBytes.Of(action));

                return stream.ToArray();
            }
        }

        /// <summary> Exactly what the SDK's subscribe path sends. </summary>
        public static byte[] Wire(MatchModel model)
            => MetaSerialization.SerializeTagged(model, MetaSerializationFlags.SendOverNetwork, logicVersion: null);

        /// <summary> A copy of one seat's public state, to plant a field into. </summary>
        public static SeatState CopyOf(SeatState real)
        {
            SeatState copy = new SeatState(real.DenHp, real.Mana, real.MaxMana, real.TuckeredOutTicks, real.HasMulliganed, real.TricksCastThisTurn);
            copy.Board.AddRange(real.Board);
            copy.Graveyard.AddRange(real.Graveyard);
            copy.UnseenPool.AddRange(real.UnseenPool);
            copy.PlayedThisMatch.AddRange(real.PlayedThisMatch);
            copy.PlayedFromOwnDeck.AddRange(real.PlayedFromOwnDeck);
            copy.SetCounts(real.HandCount, real.DeckCount);
            if (real.HasMulliganed)
                copy.SetMulliganed();
            return copy;
        }

        /// <summary> One seat's public state with a different list where the unseen pool belongs. </summary>
        public static SeatState WithUnseenPool(SeatState real, List<CardId> pool)
        {
            SeatState planted = CopyOf(real);
            planted.UnseenPool.Clear();
            planted.UnseenPool.AddRange(pool);
            return planted;
        }

        /// <summary>
        /// One seat's public state with a different number where the hand count belongs — the shape of leak
        /// hidden-information.md calls "a count in the wrong place", which no identity scan can see because
        /// the payload names no card at all.
        /// </summary>
        public static SeatState WithHandCount(SeatState real, int handCount)
        {
            SeatState planted = CopyOf(real);
            planted.SetCounts(handCount, real.DeckCount);
            return planted;
        }

        /// <summary> One seat's public state with a number that came out of the seeded stream in it. </summary>
        public static SeatState WithTuckeredOutTicks(SeatState real, int ticks)
        {
            SeatState planted = CopyOf(real);
            planted.SetTuckeredOutTicks(ticks);
            return planted;
        }

        /// <summary> One seat's public state with a chosen list where the played-cards list belongs. </summary>
        public static SeatState WithPlayedThisMatch(SeatState real, List<CardId> played)
        {
            SeatState planted = CopyOf(real);
            planted.PlayedThisMatch.Clear();
            planted.PlayedThisMatch.AddRange(played);
            return planted;
        }

        /// <summary>
        /// A value that depends on where the seeded stream currently stands, read without disturbing it. What
        /// a payload would carry if it had leaked the RNG position: the stream replays forward in closed form,
        /// so a position plus a seed is the rest of the deal.
        /// </summary>
        public static int RngPositionProxy(MatchModel match) => new RandomPCG(match.Secret.Rng).NextInt(1000000);

        /// <summary>
        /// What a zone's cards cost, added up — a single integer computed over hidden state. Chosen over
        /// something like "how many different cards" because a singleton deck makes that one equal to the hand
        /// count, and a plant that cannot differ is not a plant.
        /// </summary>
        public static int TotalPrintedCost(MatchModel match, List<CardInstanceId> zone)
        {
            int total = 0;
            foreach (CardInstanceId id in zone)
            {
                CardStats? stats = SecretDerivations.Stats(match, id);
                if (stats.HasValue)
                    total += stats.Value.Cost;
            }

            return total;
        }

        static void Append(MemoryStream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
    }
}
