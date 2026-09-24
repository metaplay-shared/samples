using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// The engine's state within a turn. This is separate from <see cref="MatchPhase"/>, which the match host
    /// manages together with seats, players and connections.
    /// </summary>
    [MetaSerializable]
    public enum MatchTurnPhase
    {
        /// <summary>One seat is on turn and must play one legal card.</summary>
        AwaitingMove = 0,

        /// <summary>
        /// The last card of a trick was played and the trick is credited to its winner. No seat is on turn until
        /// the resolve pause ends.
        /// </summary>
        ResolvingTrick = 1,

        /// <summary>All tricks are played.</summary>
        Finished = 2,
    }

    /// <summary>
    /// The outcome of submitting a move to the engine.
    /// </summary>
    public readonly struct MoveResult
    {
        /// <summary>Whether the card was played.</summary>
        public readonly bool Accepted;

        /// <summary>Why the move was refused, or <see cref="MoveRefusalReason.None"/> if it was accepted.</summary>
        public readonly MoveRefusalReason Refusal;

        /// <summary>Whether this move completed a trick, putting the engine into its resolve pause.</summary>
        public readonly bool TrickCompleted;

        /// <summary>The seat that won the completed trick, or -1 if this move did not complete one.</summary>
        public readonly int TrickWinnerSeat;

        MoveResult(bool accepted, MoveRefusalReason refusal, bool trickCompleted, int trickWinnerSeat)
        {
            Accepted        = accepted;
            Refusal         = refusal;
            TrickCompleted  = trickCompleted;
            TrickWinnerSeat = trickWinnerSeat;
        }

        public static MoveResult Refused(MoveRefusalReason reason) => new MoveResult(false, reason, false, -1);
        public static MoveResult Played() => new MoveResult(true, MoveRefusalReason.None, false, -1);
        public static MoveResult PlayedAndCompletedTrick(int winnerSeat) => new MoveResult(true, MoveRefusalReason.None, true, winnerSeat);

        public override string ToString() => Accepted ? "accepted" : $"refused({Refusal})";
    }

    /// <summary>
    /// An accepted move that is not yet applied: the card, and the turn state the engine will be in after
    /// <see cref="MatchEngine.CommitMove"/>.
    /// <para>
    /// The plan lets the host put the move on the timeline before the engine changes. The engine is server-only
    /// and not checksummed, so if the engine got ahead of the board, no checksum would detect it, and the engine
    /// would refuse every later move as having a stale play index (<c>docs/match.md</c>).
    /// </para>
    /// </summary>
    public readonly struct MovePlan
    {
        /// <summary>The play index the plan was made at. <see cref="MatchEngine.CommitMove"/> throws at any other index.</summary>
        public readonly int PlayIndex;

        public readonly int  Seat;
        public readonly Card Card;

        /// <summary>The turn phase after the move is committed.</summary>
        public readonly MatchTurnPhase TurnPhase;

        public readonly MetaTime MoveDeadlineAt;
        public readonly MetaTime ResolvePauseEndsAt;

        /// <summary>The seat that wins the trick this move completes, or -1 if it completes none.</summary>
        public readonly int TrickWinnerSeat;

        internal MovePlan(int playIndex, int seat, Card card, MatchTurnPhase phase, MetaTime moveDeadlineAt, MetaTime resolvePauseEndsAt, int trickWinnerSeat)
        {
            PlayIndex          = playIndex;
            Seat               = seat;
            Card               = card;
            TurnPhase          = phase;
            MoveDeadlineAt     = moveDeadlineAt;
            ResolvePauseEndsAt = resolvePauseEndsAt;
            TrickWinnerSeat    = trickWinnerSeat;
        }

        public override string ToString() => $"seat {Seat} plays {Card} at index {PlayIndex}";
    }

    /// <summary>
    /// The end of a resolve pause, planned but not yet applied. Works like <see cref="MovePlan"/>, for
    /// <see cref="MatchEngine.CommitAdvance"/>.
    /// </summary>
    public readonly struct AdvancePlan
    {
        /// <summary>The play index the plan was made at.</summary>
        public readonly int PlayIndex;

        public readonly MatchTurnPhase TurnPhase;
        public readonly MetaTime       MoveDeadlineAt;

        internal AdvancePlan(int playIndex, MatchTurnPhase phase, MetaTime moveDeadlineAt)
        {
            PlayIndex      = playIndex;
            TurnPhase      = phase;
            MoveDeadlineAt = moveDeadlineAt;
        }

        public override string ToString() => $"advance to {TurnPhase} at index {PlayIndex}";
    }

    /// <summary>
    /// The durations the host sets when a card is played: the next seat's move deadline and the resolve pause
    /// after a completed trick.
    /// <para>
    /// The host passes these per move instead of the engine reading <see cref="MatchTimings"/>, because the
    /// deadline depends on the seat. Only a connected seat has a move deadline. A disconnected seat has a grace
    /// timer instead (<c>docs/match.md</c>, "When players stop playing"). A table played out by bots uses
    /// <see cref="Instant"/>.
    /// </para>
    /// </summary>
    public readonly struct MoveTimings
    {
        /// <summary>The move deadline for the seat that comes on turn, or <see cref="MetaDuration.Zero"/> for no deadline.</summary>
        public readonly MetaDuration NextMoveDeadline;

        /// <summary>The pause after a completed trick, or <see cref="MetaDuration.Zero"/> for none.</summary>
        public readonly MetaDuration ResolvePause;

        public MoveTimings(MetaDuration nextMoveDeadline, MetaDuration resolvePause)
        {
            NextMoveDeadline = nextMoveDeadline;
            ResolvePause     = resolvePause;
        }

        /// <summary>The table's durations, applied the same way to every seat.</summary>
        public static MoveTimings FromTable(MatchTimings timings) => new MoveTimings(timings.MoveDeadline, timings.ResolvePause);

        /// <summary>No deadline and no pause. Used when a table is played out by bots.</summary>
        public static MoveTimings Instant => new MoveTimings(MetaDuration.Zero, MetaDuration.Zero);

        public override string ToString() => $"deadline {NextMoveDeadline}, pause {ResolvePause}";
    }

    /// <summary>
    /// One game of Table Stakes: the deal, the legality rule, trick resolution, the standings, and the state machine
    /// that runs a game from deal to result. It is deterministic: the same seed and moves produce the same game. It
    /// reads no clock and does not know about players. The host supplies the time, the timings and the seat mapping.
    /// <para>
    /// The engine holds every hand and the RNG state. It is a server-only member of the match model and is never sent
    /// to a client (<c>docs/match.md</c>). <see cref="GetHand"/> returns one seat's hand at a time.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MatchEngine
    {
        public const int NumSeats      = MatchRules.NumSeats;
        public const int CardsPerSeat  = MatchRules.CardsPerSeat;
        public const int NumTricks     = MatchRules.NumTricks;
        public const int CardsPerTrick = MatchRules.CardsPerTrick;
        public const int NumPlays      = MatchRules.NumPlays;

        /// <summary>Index into <c>_deck</c> of the card revealed to set the trump suit.</summary>
        const int TrumpCardNdx = NumSeats * CardsPerSeat;

        /// <summary>
        /// The whole deck in shuffled order. The hands come first, <see cref="CardsPerSeat"/> consecutive cards
        /// per seat in seat order, followed by the trump card at <c>TrumpCardNdx</c> and then the undealt
        /// cards. No rule reads the undealt cards, but they are kept so the state reproduces the game exactly.
        /// A seat's hand is computed from this list and the play history.
        /// </summary>
        [MetaMember(1)] List<Card> _deck;

        /// <summary>The RNG in the state the deal left it. Secret, like the seed.</summary>
        [MetaMember(2)] RandomPCG _rng;

        /// <summary>Every card played so far, in play order.</summary>
        [MetaMember(3)] List<PlayRecord> _plays;

        /// <summary>The winning seat of each resolved trick, in trick order.</summary>
        [MetaMember(4)] List<int> _trickWinnerSeats;

        [MetaMember(5)] int _startingLeaderSeat;

        [MetaMember(6)] MatchTurnPhase _turnPhase;

        [MetaMember(7)] MetaTime _moveDeadlineAt;

        [MetaMember(8)] MetaTime _resolvePauseEndsAt;

        [MetaMember(9)] MatchTimings _timings;

        MatchEngine() { }

        /// <summary>
        /// Deal a new game from a seed. The RNG shuffles the deck and draws the starting leader, and nothing else uses
        /// it, so the same seed always produces the same game. The deck layout is described on <c>_deck</c>.
        /// </summary>
        /// <param name="seed">
        /// Cryptographically random seed, supplied by the host. A guessable seed, such as one derived from an entity id
        /// or a timestamp, would let a client recompute the shuffle and see every hand.
        /// </param>
        /// <param name="timings">The durations this game runs on, supplied by the host.</param>
        /// <param name="now">The current time, used to arm the first move deadline.</param>
        public static MatchEngine Create(ulong seed, MatchTimings timings, MetaTime now)
        {
            RandomPCG  rng  = RandomPCG.CreateFromSeed(seed);
            List<Card> deck = Deck.CreateOrdered();
            rng.ShuffleInPlace(deck);

            int startingLeaderSeat = rng.NextInt(NumSeats);
            return Init(rng, deck, startingLeaderSeat, timings, now);
        }

        /// <summary>
        /// Start a game from a given deck order and starting leader instead of a shuffle, so tests can set up a
        /// specific position. <paramref name="deck"/> uses the same layout as <c>_deck</c>. Throws
        /// <see cref="ArgumentException"/> when the deck is not exactly <see cref="Deck.NumCards"/> distinct cards.
        /// The seed only initializes <c>_rng</c>, which the game does not draw from after the deal. It is internal so
        /// that production code can only start a game with <see cref="Create"/>, where the deal comes from an
        /// unguessable seed.
        /// </summary>
        internal static MatchEngine CreateFromDeal(ulong seed, IReadOnlyList<Card> deck, int startingLeaderSeat, MatchTimings timings, MetaTime now)
        {
            if (deck == null || deck.Count != Deck.NumCards)
                throw new ArgumentException($"A deal is exactly {Deck.NumCards} cards", nameof(deck));
            HashSet<Card> distinct = new HashSet<Card>();
            foreach (Card card in deck)
            {
                if (!distinct.Add(card))
                    throw new ArgumentException($"{card} appears twice in the deal", nameof(deck));
            }
            MatchRules.ThrowIfInvalidSeat(startingLeaderSeat);

            return Init(RandomPCG.CreateFromSeed(seed), new List<Card>(deck), startingLeaderSeat, timings, now);
        }

        /// <summary>A game at its first move, dealt from <paramref name="deck"/>. Both ways to start a game end here.</summary>
        static MatchEngine Init(RandomPCG rng, List<Card> deck, int startingLeaderSeat, MatchTimings timings, MetaTime now)
        {
            MatchEngine engine = new MatchEngine();
            engine._rng                = rng;
            engine._deck               = deck;
            engine._startingLeaderSeat = startingLeaderSeat;
            engine._plays              = new List<PlayRecord>(NumPlays);
            engine._trickWinnerSeats   = new List<int>(NumTricks);
            engine._timings            = timings;
            engine._turnPhase          = MatchTurnPhase.AwaitingMove;
            engine._resolvePauseEndsAt = MetaTime.Epoch;
            engine._moveDeadlineAt     = ArmDeadlineAt(now, timings.MoveDeadline);
            return engine;
        }

        #region Public state

        public MatchTimings Timings => _timings;

        public MatchTurnPhase TurnPhase => _turnPhase;

        /// <summary>
        /// The number of cards played so far, which is the play index the next move must name. It equals
        /// <see cref="NumPlays"/> after the last card, when no move is valid.
        /// </summary>
        public int PlayIndex => _plays.Count;

        /// <summary>The zero-based index of the trick in progress, or <see cref="NumTricks"/> once the game is over.</summary>
        public int TrickIndex => MatchRules.TrickIndex(_plays.Count);

        /// <summary>How many cards of the current trick have been played, below <see cref="CardsPerTrick"/>.</summary>
        public int PositionInTrick => MatchRules.PositionInTrick(_plays.Count);

        /// <summary>The card revealed after the deal. It is public, and its suit is trump.</summary>
        public Card TrumpCard => _deck[TrumpCardNdx];

        public Suit TrumpSuit => TrumpCard.Suit;

        /// <summary>The seat that led the first trick, drawn from the RNG after the shuffle.</summary>
        public int StartingLeaderSeat => _startingLeaderSeat;

        /// <summary>The seat leading the trick in progress, or between tricks, the seat that leads the next one.</summary>
        public int CurrentLeaderSeat => MatchRules.CurrentLeaderSeat(_trickWinnerSeats, _startingLeaderSeat);

        /// <summary>The seat that owes a card, or -1 when no seat is on turn.</summary>
        public int SeatOnTurn => MatchRules.SeatOnTurn(_turnPhase, _plays.Count, _trickWinnerSeats, _startingLeaderSeat);

        /// <summary>
        /// The seat on turn after the next card is played, or -1 when that card completes a trick or no move is
        /// expected. The host needs it before planning the move, because it gives a move deadline only to a
        /// connected seat.
        /// </summary>
        public int SeatOnTurnAfterNextPlay
        {
            get
            {
                if (_turnPhase != MatchTurnPhase.AwaitingMove)
                    return -1;
                if (PositionInTrick == CardsPerTrick - 1)
                    return -1;
                return (CurrentLeaderSeat + PositionInTrick + 1) % NumSeats;
            }
        }

        /// <summary>
        /// The seat that leads after the resolve pause ends, or -1 when there is no resolve pause or the last
        /// trick was played. Like <see cref="SeatOnTurnAfterNextPlay"/>, the host uses it to choose the deadline.
        /// </summary>
        public int SeatOnTurnAfterPause => _turnPhase == MatchTurnPhase.ResolvingTrick && _plays.Count < NumPlays ? CurrentLeaderSeat : -1;

        /// <summary>The suit that must be followed in the trick in progress, or null when the next card played is a lead.</summary>
        public Suit? LedSuit => MatchRules.LedSuit(_plays);

        /// <summary>
        /// Every card played so far, with the seat that played it, in play order. Returned as a read-only wrapper
        /// so a caller cannot cast it back to the list and change the history.
        /// </summary>
        public IReadOnlyList<PlayRecord> Plays => _plays.AsReadOnly();

        /// <summary>The winning seat of each resolved trick, in trick order.</summary>
        public IReadOnlyList<int> TrickWinnerSeats => _trickWinnerSeats.AsReadOnly();

        /// <summary>
        /// When the seat on turn runs out of time, or <see cref="MetaTime.Epoch"/> for no deadline. The engine
        /// stores the deadline but does not enforce it. The host detects a passed deadline and auto-plays with
        /// <see cref="BotPolicy"/>. Only valid in <see cref="MatchTurnPhase.AwaitingMove"/>.
        /// </summary>
        public MetaTime MoveDeadlineAt => _moveDeadlineAt;

        /// <summary>
        /// When the resolve pause ends. Only valid in <see cref="MatchTurnPhase.ResolvingTrick"/>.
        /// </summary>
        public MetaTime ResolvePauseEndsAt => _resolvePauseEndsAt;

        public bool IsFinished => _turnPhase == MatchTurnPhase.Finished;

        /// <summary>
        /// The cards seat <paramref name="seat"/> still holds, in deal order. There is no accessor for all hands,
        /// so a bot or a client projection cannot read other seats' hands by mistake.
        /// </summary>
        public IReadOnlyList<Card> GetHand(int seat)
        {
            MatchRules.ThrowIfInvalidSeat(seat);
            List<Card> hand = new List<Card>(CardsPerSeat);
            for (int ndx = 0; ndx < CardsPerSeat; ndx++)
            {
                Card card = _deck[seat * CardsPerSeat + ndx];
                if (!HasBeenPlayed(card))
                    hand.Add(card);
            }
            return hand;
        }

        /// <summary>How many cards seat <paramref name="seat"/> still holds. Public information.</summary>
        public int GetCardsRemaining(int seat) => MatchRules.GetCardsRemaining(_plays, seat);

        /// <summary>How many tricks seat <paramref name="seat"/> has won. Public information.</summary>
        public int GetTricksWon(int seat) => MatchRules.GetTricksWon(_trickWinnerSeats, seat);

        /// <summary>The cards seat <paramref name="seat"/> may legally play now, in hand order. Empty when it is not on turn.</summary>
        public List<Card> GetLegalPlays(int seat)
        {
            MatchRules.ThrowIfInvalidSeat(seat);
            if (_turnPhase != MatchTurnPhase.AwaitingMove || seat != SeatOnTurn)
                return new List<Card>();
            return MatchRules.GetLegalPlays(GetHand(seat), LedSuit);
        }

        /// <summary>
        /// All seats ranked from the tricks resolved so far. The match records the standings computed after the
        /// game finishes.
        /// </summary>
        public List<SeatStanding> ComputeStandings() => MatchRules.ComputeStandings(_trickWinnerSeats);

        #endregion

        #region Driving the game

        /// <summary>
        /// Plan and commit a move with the table's timings. See <see cref="PlanMove"/> for the checks.
        /// </summary>
        public MoveResult PlayCard(int seat, int playIndex, Card card, MetaTime now)
            => PlayCard(seat, playIndex, card, now, MoveTimings.FromTable(_timings));

        /// <summary>
        /// Plan and commit a move with timings the host chose for the next seat (see <see cref="MoveTimings"/>).
        /// </summary>
        public MoveResult PlayCard(int seat, int playIndex, Card card, MetaTime now, MoveTimings timings)
        {
            MoveResult result = PlanMove(seat, playIndex, card, now, timings, out MovePlan plan);
            if (result.Accepted)
                CommitMove(plan);
            return result;
        }

        /// <summary>
        /// Check a move and compute its result without applying it. All rule checks happen here, so
        /// <see cref="CommitMove"/> cannot fail for a plan from an accepted move. <paramref name="timings"/> gives
        /// the next seat's deadline and the resolve pause (see <see cref="MoveTimings"/>).
        /// <para>
        /// A move with a stale play index is refused before any other check. This handles every race, such as a
        /// card that arrives after the deadline auto-played the seat, a delayed bot move, or a double tap, so no
        /// seat can play twice for one index.
        /// </para>
        /// </summary>
        public MoveResult PlanMove(int seat, int playIndex, Card card, MetaTime now, MoveTimings timings, out MovePlan plan)
        {
            MatchRules.ThrowIfInvalidSeat(seat);
            plan = default;

            if (playIndex != PlayIndex)
                return MoveResult.Refused(MoveRefusalReason.StalePlayIndex);
            if (_turnPhase != MatchTurnPhase.AwaitingMove)
                return MoveResult.Refused(MoveRefusalReason.NotInPlayablePhase);
            if (seat != SeatOnTurn)
                return MoveResult.Refused(MoveRefusalReason.NotYourTurn);

            IReadOnlyList<Card> hand = GetHand(seat);
            if (!hand.Contains(card))
                return MoveResult.Refused(MoveRefusalReason.CardNotInHand);

            Suit? ledSuit = LedSuit;
            if (!MatchRules.IsLegalPlay(hand, ledSuit, card))
                return MoveResult.Refused(MoveRefusalReason.MustFollowSuit);

            // The last card of a trick resolves it and starts the resolve pause. Any other card passes the turn to
            // the next seat.
            bool completesTrick = (_plays.Count + 1) % CardsPerTrick == 0;
            if (!completesTrick)
            {
                plan = new MovePlan(PlayIndex, seat, card, MatchTurnPhase.AwaitingMove, ArmDeadlineAt(now, timings.NextMoveDeadline), MetaTime.Epoch, -1);
                return MoveResult.Played();
            }

            List<PlayRecord> trick = GetCurrentTrickPlays();
            trick.Add(new PlayRecord(seat, card));
            int winnerSeat = MatchRules.ResolveTrick(trick, TrumpSuit);

            plan = new MovePlan(PlayIndex, seat, card, MatchTurnPhase.ResolvingTrick, MetaTime.Epoch, now + timings.ResolvePause, winnerSeat);
            return MoveResult.PlayedAndCompletedTrick(winnerSeat);
        }

        /// <summary>
        /// Apply a planned move. The host calls this only after the move is on the timeline, so the engine is
        /// never ahead of the board the clients see. Throws <see cref="InvalidOperationException"/> when the
        /// engine is no longer at the plan's play index or not waiting for a move.
        /// </summary>
        public void CommitMove(MovePlan plan)
        {
            if (plan.PlayIndex != PlayIndex || _turnPhase != MatchTurnPhase.AwaitingMove)
                throw new InvalidOperationException($"Committing a move planned at index {plan.PlayIndex} to an engine at index {PlayIndex} in phase {_turnPhase}");

            _plays.Add(new PlayRecord(plan.Seat, plan.Card));
            if (plan.TrickWinnerSeat >= 0)
                _trickWinnerSeats.Add(plan.TrickWinnerSeat);

            _turnPhase          = plan.TurnPhase;
            _moveDeadlineAt     = plan.MoveDeadlineAt;
            _resolvePauseEndsAt = plan.ResolvePauseEndsAt;
        }

        /// <summary>
        /// End the resolve pause if it has ended by <paramref name="now"/>. The next trick starts with the trick
        /// winner leading, or the game finishes after the last trick. Does nothing in any other case, so the host
        /// can call it on every tick.
        /// </summary>
        /// <returns>Whether the call changed anything.</returns>
        public bool Advance(MetaTime now) => Advance(now, _timings.MoveDeadline);

        /// <summary>
        /// Like <see cref="Advance(MetaTime)"/>, but gives the next trick's leader <paramref name="nextMoveDeadline"/>
        /// instead of the table's deadline (see <see cref="MoveTimings"/>).
        /// </summary>
        public bool Advance(MetaTime now, MetaDuration nextMoveDeadline)
        {
            if (!PlanAdvance(now, nextMoveDeadline, out AdvancePlan plan))
                return false;
            CommitAdvance(plan);
            return true;
        }

        /// <summary>
        /// Compute the end of the resolve pause without applying it. Returns false when the board is not in the
        /// resolve pause or the pause has not ended by <paramref name="now"/>.
        /// </summary>
        public bool PlanAdvance(MetaTime now, MetaDuration nextMoveDeadline, out AdvancePlan plan)
        {
            plan = default;
            if (_turnPhase != MatchTurnPhase.ResolvingTrick)
                return false;
            if (now < _resolvePauseEndsAt)
                return false;

            if (_plays.Count >= NumPlays)
                plan = new AdvancePlan(PlayIndex, MatchTurnPhase.Finished, MetaTime.Epoch);
            else
                plan = new AdvancePlan(PlayIndex, MatchTurnPhase.AwaitingMove, ArmDeadlineAt(now, nextMoveDeadline));
            return true;
        }

        /// <summary>
        /// Apply a planned advance after it is on the timeline. Throws <see cref="InvalidOperationException"/>
        /// when the engine is no longer at the plan's play index or not in the resolve pause.
        /// </summary>
        public void CommitAdvance(AdvancePlan plan)
        {
            if (plan.PlayIndex != PlayIndex || _turnPhase != MatchTurnPhase.ResolvingTrick)
                throw new InvalidOperationException($"Committing an advance planned at index {plan.PlayIndex} to an engine at index {PlayIndex} in phase {_turnPhase}");

            _resolvePauseEndsAt = MetaTime.Epoch;
            _turnPhase          = plan.TurnPhase;
            _moveDeadlineAt     = plan.MoveDeadlineAt;
        }

        /// <summary>
        /// Set the move deadline of the seat on turn to <paramref name="now"/> plus <paramref name="duration"/>,
        /// or to no deadline when <paramref name="duration"/> is zero. Does nothing outside
        /// <see cref="MatchTurnPhase.AwaitingMove"/>. The host calls this when play begins after the join window,
        /// and when the seat on turn connects or disconnects. The duration is a parameter because only a
        /// connected seat gets a deadline (see <see cref="MoveTimings"/>).
        /// </summary>
        public void ArmMoveDeadline(MetaTime now, MetaDuration duration)
        {
            if (_turnPhase == MatchTurnPhase.AwaitingMove)
                _moveDeadlineAt = ArmDeadlineAt(now, duration);
        }

        /// <summary>
        /// Set the move deadline to a time the host already wrote to the board, so the engine and the board agree.
        /// <see cref="MetaTime.Epoch"/> means no deadline. Does nothing outside <see cref="MatchTurnPhase.AwaitingMove"/>.
        /// </summary>
        public void SetMoveDeadline(MetaTime deadlineAt)
        {
            if (_turnPhase == MatchTurnPhase.AwaitingMove)
                _moveDeadlineAt = deadlineAt;
        }

        /// <summary>
        /// The deadline for a seat coming on turn: <paramref name="now"/> plus <paramref name="duration"/>, or
        /// <see cref="MetaTime.Epoch"/> (no deadline) when <paramref name="duration"/> is zero. The engine also
        /// writes Epoch when no seat is on turn, so one value means "no deadline" everywhere.
        /// <para>
        /// The host passes zero for a disconnected seat, which has a grace timer instead, and for a table played
        /// out by bots. Zero gives no deadline rather than one that has already passed, so the client shows no
        /// countdown ring (<c>docs/web-client.md</c>).
        /// </para>
        /// </summary>
        static MetaTime ArmDeadlineAt(MetaTime now, MetaDuration duration)
            => duration > MetaDuration.Zero ? now + duration : MetaTime.Epoch;

        #endregion

        #region Helpers

        /// <summary>
        /// The cards on the table, in play order: the trick in progress, or the completed trick during the resolve
        /// pause.
        /// </summary>
        public List<PlayRecord> GetCurrentTrickPlays() => MatchRules.GetCurrentTrickPlays(_plays, _turnPhase);

        bool HasBeenPlayed(Card card) => MatchRules.HasBeenPlayed(_plays, card);


        #endregion
    }
}
