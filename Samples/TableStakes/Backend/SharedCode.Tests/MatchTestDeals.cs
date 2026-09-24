using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Helpers for tests that need a hand-written deal rather than a shuffled one. A test that pins a bot's
    /// decision writes the position out card by card, because a position found by trying seeds would pin the
    /// shuffle for that seed rather than the decision.
    /// </summary>
    public static class MatchTestDeals
    {
        public static readonly MetaTime T0 = MetaTime.FromDateTime(new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc));

        /// <summary>
        /// A fixed player id per seat, so every fixture that names the same seat uses the same player.
        /// </summary>
        public static EntityId Player(int seat) => EntityId.Create(EntityKindCore.Player, (ulong)(1000 + seat));

        /// <summary>A match id from a small index, so a test can name the same match twice.</summary>
        public static EntityId MatchId(int index) => EntityId.Create(EntityKindGame.Match, (ulong)index);

        /// <summary>
        /// A seat list where the first <paramref name="numHumanSeats"/> seats are human and the rest are bots named
        /// from <see cref="TestBotConfig.Names"/>. <paramref name="hasArrived"/> sets whether every seat has
        /// arrived: true for a table in play, false for a table still waiting for its players.
        /// </summary>
        public static List<MatchSeat> Seats(int numHumanSeats, bool hasArrived)
        {
            List<MatchSeat> seats = new List<MatchSeat>(MatchRules.NumSeats);
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
            {
                if (seat < numHumanSeats)
                    seats.Add(new MatchSeat(seat, PlayerPublicIdentity.ForSeat(Player(seat), $"Player {seat}"), MatchSeatOccupancy.Human, hasArrived));
                else
                    seats.Add(new MatchSeat(seat, PlayerPublicIdentity.ForBot(EntityId.None, TestBotConfig.Names[seat], avatarId: null), MatchSeatOccupancy.Bot, hasArrived));
            }
            return seats;
        }

        public static Card Clubs(Rank rank)    => new Card(Suit.Clubs, rank);
        public static Card Diamonds(Rank rank) => new Card(Suit.Diamonds, rank);
        public static Card Hearts(Rank rank)   => new Card(Suit.Hearts, rank);
        public static Card Spades(Rank rank)   => new Card(Suit.Spades, rank);

        /// <summary>
        /// A full deck in the order the engine deals it: each seat's hand in seat order, then the trump card,
        /// then the remaining cards in <see cref="Deck.CreateOrdered"/> order. Throws when a hand has the wrong
        /// size or a card appears twice.
        /// </summary>
        public static List<Card> BuildDeck(IReadOnlyList<Card> seat0, IReadOnlyList<Card> seat1, IReadOnlyList<Card> seat2, IReadOnlyList<Card> seat3, Card trumpCard)
        {
            List<Card>            deck  = new List<Card>(Deck.NumCards);
            IReadOnlyList<Card>[] hands = new IReadOnlyList<Card>[] { seat0, seat1, seat2, seat3 };
            for (int seat = 0; seat < hands.Length; seat++)
            {
                if (hands[seat].Count != MatchRules.CardsPerSeat)
                    throw new ArgumentException($"Seat {seat} was given {hands[seat].Count} cards, not {MatchRules.CardsPerSeat}");
                deck.AddRange(hands[seat]);
            }
            deck.Add(trumpCard);

            HashSet<Card> dealtCards = new HashSet<Card>(deck);
            if (dealtCards.Count != deck.Count)
                throw new ArgumentException("A card was given to two seats");

            foreach (Card card in Deck.CreateOrdered())
            {
                if (!dealtCards.Contains(card))
                    deck.Add(card);
            }
            return deck;
        }

        /// <summary>
        /// An engine on the given deal and starting leader with <see cref="MatchTimings.Instant"/>, so no pinned
        /// test depends on a clock.
        /// </summary>
        public static MatchEngine Engine(ulong seed, List<Card> deck, int startingLeaderSeat)
            => Engine(seed, deck, startingLeaderSeat, MatchTimings.Instant);

        /// <summary>An engine on the given deal and starting leader, using the given timings.</summary>
        public static MatchEngine Engine(ulong seed, List<Card> deck, int startingLeaderSeat, MatchTimings timings)
            => MatchEngine.CreateFromDeal(seed, deck, startingLeaderSeat, timings, T0);

        /// <summary>
        /// A match model around the given engine, with a human in seat 0 and a <see cref="TestBotConfig.Sharp"/>
        /// bot in every other seat. The play has already started (see <see cref="Start"/>).
        /// </summary>
        public static MatchModel Model(MatchEngine engine, EntityId seat0PlayerId)
        {
            List<MatchSeat>    seats    = new List<MatchSeat>(MatchRules.NumSeats);
            List<BotProfile>   profiles = new List<BotProfile>(MatchRules.NumSeats);

            seats.Add(new MatchSeat(0, PlayerPublicIdentity.ForSeat(seat0PlayerId, "You"), MatchSeatOccupancy.Human, hasArrived: true));
            profiles.Add(null);
            for (int seat = 1; seat < MatchRules.NumSeats; seat++)
            {
                seats.Add(new MatchSeat(seat, PlayerPublicIdentity.ForBot(EntityId.None, $"Bot {seat}", avatarId: null), MatchSeatOccupancy.Bot, hasArrived: true));
                profiles.Add(TestBotConfig.Sharp);
            }

            MatchModel model = new MatchModel();
            model.Setup(engine, seats, profiles, T0);

            // Setup leaves the table waiting for the join window to close, and nobody can play until then. Start
            // the play with the same action a host publishes, so rules tests get a table already in play.
            Start(model, T0);
            return model;
        }

        /// <summary>
        /// Start the play on a seated model with the <see cref="MatchSeatsUpdated"/> action a host publishes when
        /// the join window closes. Every seat keeps its current state.
        /// </summary>
        public static void Start(MatchModel model, MetaTime now)
        {
            List<MatchSeatState> states = MatchSeatPolicy.CurrentStates(model);
            MetaTime             deadlineAt = model.Engine.MoveDeadlineAt;
            Apply(model, new MatchSeatsUpdated(states, deadlineAt, playHasBegun: true, joinWindowEndsAt: MetaTime.Epoch));
        }

        /// <summary>
        /// Execute an action against a model. Throws when the model refuses the action, naming
        /// <paramref name="seed"/> when it is given.
        /// </summary>
        public static void Apply(MatchModel model, MatchAction action, ulong? seed = null)
        {
            Metaplay.Core.Model.MetaActionResult result = action.InvokeExecute(model, commit: true);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"{(seed.HasValue ? $"seed {seed}: " : "")}{action.GetType().Name} was refused: {result}");
        }

        /// <summary>A default player id for the seat-0 human that tests pass to <see cref="Model"/>.</summary>
        public static readonly EntityId Seat0PlayerId = EntityId.Create(EntityKindCore.Player, 1234UL);

        /// <summary>
        /// Play the given cards in turn order, resolving each finished trick first. Throws when the engine refuses
        /// a card.
        /// </summary>
        public static void PlayInOrder(MatchEngine engine, params Card[] cards)
        {
            foreach (Card card in cards)
            {
                if (engine.TurnPhase == MatchTurnPhase.ResolvingTrick)
                    engine.Advance(engine.ResolvePauseEndsAt);

                int        seat   = engine.SeatOnTurn;
                MoveResult result = engine.PlayCard(seat, engine.PlayIndex, card, T0);
                if (!result.Accepted)
                    throw new InvalidOperationException($"The test position played {card} from seat {seat} and the engine refused it: {result.Refusal}");
            }
        }

        #region Seat timers and tables

        /// <summary>
        /// <see cref="MatchTimings.Instant"/> with the seat timers that the seat-policy and result-capture tests
        /// exercise. Bots have no think delay, because those tests are about when a seat is taken away, not how
        /// long a bot thinks.
        /// </summary>
        public static MatchTimings SeatTimings()
        {
            MatchTimings timings = MatchTimings.Instant;
            timings.JoinWindow              = MetaDuration.FromSeconds(10);
            timings.MoveDeadline            = MetaDuration.FromSeconds(20);
            timings.DisconnectGrace         = MetaDuration.FromSeconds(20);
            timings.RestartReconnectGrace   = MetaDuration.FromSeconds(90);
            timings.CoveredSeatReclaimDelay = MetaDuration.FromSeconds(3);
            timings.StrikesBeforeCover      = 2;
            return timings;
        }

        /// <summary>
        /// A new table dealt from <paramref name="seed"/> that has not started, with <paramref name="numHumanSeats"/>
        /// human seats and bots from <see cref="TestBotConfig.Opponents"/> in the rest. By default no human has
        /// arrived yet, which is how every real table starts.
        /// </summary>
        public static MatchModel NewTable(ulong seed, MatchTimings timings, int numHumanSeats = 1, bool hasArrived = false)
        {
            MatchModel model = new MatchModel();
            MatchHost.SetupNewMatch(model, seed, seed ^ 0xABCDUL, Seats(numHumanSeats, hasArrived), timings, TestBotConfig.Opponents, T0);
            return model;
        }

        /// <summary>
        /// A table where every one of the <paramref name="numHumanSeats"/> humans arrived at <see cref="T0"/> and
        /// play has started. <paramref name="pendingBotMove"/> is the pending bot move the first run used.
        /// </summary>
        public static MatchModel StartedTable(ulong seed, MatchTimings timings, out TestMatchHost host, out MatchPendingBotMove pendingBotMove, int numHumanSeats = 1)
        {
            MatchModel model = NewTable(seed, timings, numHumanSeats);
            host           = new TestMatchHost(model, seed);
            pendingBotMove = new MatchPendingBotMove();

            for (int seat = 0; seat < numHumanSeats; seat++)
                MatchHost.NoteSeatPresent(model, seat, T0, host);

            MatchHost.RunTable(model, pendingBotMove, T0, host);
            return model;
        }

        /// <inheritdoc cref="StartedTable(ulong, MatchTimings, out TestMatchHost, out MatchPendingBotMove, int)"/>
        public static MatchModel StartedTable(ulong seed, MatchTimings timings, out TestMatchHost host, int numHumanSeats = 1)
            => StartedTable(seed, timings, out host, out MatchPendingBotMove _, numHumanSeats);

        #endregion

        #region Playing a table on

        /// <summary>
        /// Run the table and play the bot policy's card for each other seat until <paramref name="seat"/> is on
        /// turn. Throws when the seat never comes on turn.
        /// </summary>
        public static void PlayUntilSeatIsOnTurn(MatchModel model, int seat, IMatchHostEnvironment host, ulong seed)
        {
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();
            for (int guard = 0; guard < MatchRules.NumPlays * 2 && model.Board.SeatOnTurn != seat; guard++)
            {
                MatchHost.RunTable(model, pendingBotMove, T0, host);
                if (model.Board.SeatOnTurn == seat)
                    break;

                int  onTurn = model.Board.SeatOnTurn;
                Card card   = BotPolicy.DecidePlayOut(MatchSeatView.ForSeat(model.Engine, onTurn), seed).Card;
                MatchHost.TryPlayForSeat(model, onTurn, card, T0, host);
                MatchHost.TryAdvance(model, T0, host);
            }

            if (model.Board.SeatOnTurn != seat)
                throw new InvalidOperationException($"seed {seed}: seat {seat} never came on turn");
        }

        /// <summary>
        /// Play the table from where it is to the finish, with the bot policy's card at every seat. Throws when the
        /// host refuses a play or an advance, or when the game does not finish.
        /// </summary>
        public static void PlayWholeGame(MatchModel model, IMatchHostEnvironment host, ulong seed)
        {
            for (int guard = 0; model.Phase == MatchPhase.Playing; guard++)
            {
                if (guard >= MatchRules.NumPlays * 4)
                    throw new InvalidOperationException($"seed {seed}: the game did not finish");

                if (model.Engine.TurnPhase == MatchTurnPhase.AwaitingMove)
                {
                    int  onTurn = model.Engine.SeatOnTurn;
                    Card card   = BotPolicy.DecidePlayOut(MatchSeatView.ForSeat(model.Engine, onTurn), seed).Card;
                    if (!MatchHost.TryPlayForSeat(model, onTurn, card, T0, host))
                        throw new InvalidOperationException($"seed {seed}: seat {onTurn} playing {card} was refused");
                }
                else if (!MatchHost.TryAdvance(model, T0, host))
                {
                    throw new InvalidOperationException($"seed {seed}: the game stalled in phase {model.Engine.TurnPhase}");
                }
            }
        }

        #endregion
    }
}
