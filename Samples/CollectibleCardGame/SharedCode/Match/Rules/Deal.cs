using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The deal: the one entry point that turns a host's setup into a game.
    /// <para>
    /// It is <b>not an action</b>, which is legal because it runs before there is a timeline and before there
    /// is a subscriber to send one to. The initial state
    /// a subscriber receives is what this builds. It is also the only place a hidden instance is minted and
    /// the only place public members are written outside an action — a comment says so at the call site,
    /// because it looks like the mistake the SDK's own docs warn about.
    /// </para>
    /// </summary>
    public static class Deal
    {
        /// <summary>
        /// Deal a new game onto <paramref name="match"/>. The stream is consumed in a fixed order — the
        /// Weather, seat 0's shuffle, seat 1's shuffle, the first seat — because the same seed must reproduce
        /// the same game in every host, and a host that shuffled before drawing the Weather would produce a
        /// different one with nothing to report it.
        /// <para>
        /// The mulligan deadline is stamped from the model's clock, so a host sets that clock before dealing.
        /// </para>
        /// </summary>
        public static void Create(MatchModel match, MatchSetup setup, ulong botSeed)
        {
            SharedGameConfig config = setup.Config;
            GlobalConfig     global = config.Global;

            SecretOps.Create(match, setup.Seed, botSeed);

            List<SeatState> seats = new List<SeatState>
            {
                new SeatState(global.DenStartingHp, 0, global.StartingMaxMana),
                new SeatState(global.DenStartingHp, 0, global.StartingMaxMana),
            };

            match.Rules = new MatchRulesState(
                weather: null,
                MatchPhase.Mulligan,
                turn: 0,
                firstSeat: 0,
                seatOnTurn: 0,
                seats,
                new List<CardInstance>());

            MatchRulesState rules = match.Rules;

            // 1. Mint every deal instance over the authored deck lists, before anything is shuffled. Consumes
            //    no randomness: an identity that moved with the shuffle would be the deck order in disguise.
            for (int seat = 0; seat < MatchSeats.Count; seat++)
                MintDeck(match, setup.Deck(seat), seat);

            // 2. The Weather, drawn from the pool in canonical key order so the same seed picks the same one
            //    in every host.
            rules.SetWeather(DrawWeather(match, config));

            // 3. Both shuffles, seat 0 then seat 1.
            SecretOps.ShuffleDeck(match, 0);
            SecretOps.ShuffleDeck(match, 1);

            // 4. Who goes first. One bit of a cryptographically random seed, and public.
            int firstSeat = SecretOps.NextInt(match, MatchSeats.Count);
            rules.SetFirstSeat(firstSeat);
            rules.SetSeatOnTurn(firstSeat);

            // 5. The opening hands. Dealt cards leave neither pool: they moved between deck and hand, which is
            //    not becoming public. The second seat's compensation card is not among them — it is granted
            //    when the mulligan resolves, so both opening hands hold starting-deck cards only.
            int secondSeat = MatchSeats.Other(firstSeat);
            DealOpeningHand(match, firstSeat, global.OpeningHandFirstPlayer);
            DealOpeningHand(match, secondSeat, global.OpeningHandSecondPlayer);


            match.Emit(new MatchDealtEvent(
                firstSeat,
                rules.Weather.Ref.WeatherId,
                rules.Seat(0).HandCount,
                rules.Seat(1).HandCount,
                rules.Seat(0).DeckCount,
                rules.Seat(1).DeckCount));

            // The Weather is already public by now; the client holds it on screen for as long as its own
            // reveal animation takes. The mulligan clock starts here regardless; both seats share it.
            match.Pacing.ArmOrClear(MatchDeadlineKind.Mulligan, match.CurrentTime, match.Timings.MulliganDeadline, MatchSeats.None);

            match.ClientListener.OnBoardChanged();
        }

        /// <summary>
        /// Mint one seat's whole starting deck. Every instance is hidden: its public entry carries no card and
        /// no rank, and what it is goes into its owner's secret. This is the only place that happens.
        /// </summary>
        static void MintDeck(MatchModel match, IReadOnlyList<MatchDeckCard> deck, int seat)
        {
            SeatState state = match.Rules.Seat(seat);

            foreach (MatchDeckCard entry in deck)
            {
                if (!match.Content.Cards.TryGetValue(entry.Card, out CardInfo card))
                    throw new MatchEngineException($"Seat {seat}'s deck names '{entry.Card}', which is not in the catalogue");

                CardInstance instance = ZoneOps.Mint(match, card, entry.Rank, seat, CardPlace.Unseen, isPublic: false, fromStartingDeck: true);
                SecretOps.AppendToDeck(match, seat, instance.Id);
                state.SetCounts(state.HandCount, state.DeckCount + 1);
                state.UnseenPool.Add(card.CardId);
            }

            ZoneOps.SortPool(state.UnseenPool);
        }

        static MetaRef<WeatherInfo> DrawWeather(MatchModel match, SharedGameConfig config)
        {
            List<WeatherInfo> pool = new List<WeatherInfo>(config.Weathers.Values);
            if (pool.Count == 0)
                throw new MatchEngineException("The Weather pool is empty");

            // Canonical key order, not dictionary order: the same seed must draw the same Weather everywhere.
            pool.Sort((a, b) => string.CompareOrdinal(a.WeatherId.Value, b.WeatherId.Value));

            return MetaRef<WeatherInfo>.FromItem(pool[SecretOps.NextInt(match, pool.Count)]);
        }

        /// <summary> Move cards from the top of the deck into the hand. The deal cannot overflow a hand. </summary>
        static void DealOpeningHand(MatchModel match, int seat, int count)
        {
            SeatState state = match.Rules.Seat(seat);

            for (int ndx = 0; ndx < count; ndx++)
            {
                if (state.DeckCount == 0)
                    throw new MatchEngineException($"Seat {seat}'s deck is too small to deal an opening hand of {count}");

                SecretOps.MoveTopOfDeckToHand(match, seat);
                state.SetCounts(state.HandCount + 1, state.DeckCount - 1);
            }
        }
    }
}
