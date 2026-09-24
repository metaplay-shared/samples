using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The public game: the state every rule runs on and the SDK checksums. It is a member of
    /// <see cref="MatchModel"/>, so a rules divergence between the server and a follower is a checksum
    /// mismatch rather than two boards drawn from two answers.
    /// <para>
    /// Everything hidden is under <see cref="MatchModel.Secret"/> and nothing here reaches it. That is the
    /// whole secrecy line: this type has no member whose value is a hidden identity, and the counts a
    /// follower cannot derive — <see cref="SeatState.HandCount"/>, <see cref="SeatState.DeckCount"/> — are
    /// stored rather than computed.
    /// </para>
    /// <para>
    /// It stays a nested member rather than being flattened onto the model for one load-bearing reason:
    /// <c>SerializeTagged(Rules, IncludeAll)</c> is exactly "the rules half and nothing else", which is what
    /// the determinism suite and the golden game compare, and what makes "the same game at zero timings and
    /// at real timings" a question anybody can check. The SDK's own checksum covers the whole public model
    /// including the pacing stamps, so it cannot answer that one; keeping the split structural keeps both
    /// answerable (<c>Docs/rules.md</c>, "Determinism discipline").
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MatchRulesState
    {
        [MetaMember(2)]  public MetaRef<WeatherInfo>  Weather       { get; private set; }
        [MetaMember(3)]  public MatchPhase            Phase         { get; private set; }
        /// <summary> 1-based, counting both seats' turns. Turn 1 is the first seat's first turn; 0 is the mulligan. </summary>
        [MetaMember(14)] public int                   Turn          { get; private set; }
        [MetaMember(5)]  public int                   FirstSeat     { get; private set; }
        [MetaMember(6)]  public int                   SeatOnTurn    { get; private set; }
        /// <summary> Exactly two, index == seat. </summary>
        [MetaMember(7)]  public List<SeatState>       Seats         { get; private set; }
        /// <summary>
        /// The instance registry: index == <see cref="CardInstanceId.Value"/>, dense, and public halves only.
        /// A hidden instance's entry says <see cref="CardPlace.Unseen"/> and carries no card and no rank.
        /// </summary>
        [MetaMember(8)]  public List<CardInstance>    Instances     { get; private set; }
        /// <summary>
        /// The FIFO effect queue. Every member of an item is public, which is what lets a resolution
        /// suspended across a peek resume identically on a follower.
        /// </summary>
        [MetaMember(9)]  public List<EffectQueueItem> EffectQueue   { get; private set; }
        /// <summary> Null until <see cref="MatchPhase.Complete"/>. </summary>
        [MetaMember(10)] public MatchResult           Result        { get; private set; }
        /// <summary>
        /// The public half of the one interactive resolution, or null. What was revealed is in the seat's
        /// secret and reaches its owner on the private channel.
        /// </summary>
        [MetaMember(11)] public PendingEffectChoice   PendingChoice { get; private set; }
        /// <summary>
        /// What the rules still owe once the queue drains. It is state rather than a call stack because the
        /// drain can be suspended for a seat's choice and resumed from another action.
        /// </summary>
        [MetaMember(12)] public ResolutionContinuation Continuation { get; private set; }
        /// <summary>
        /// How much of each seat's turn-reserve bank has been spent. The <em>size</em> of the bank is a
        /// host-supplied duration like every other timing, but how much of it a seat has drawn is a fact
        /// about this game: it lives here so it is hashed with everything else, and so the gating rule — only a seat that has already acted this turn may draw on it — has exactly one
        /// implementation that no host can reimplement differently.
        /// </summary>
        [MetaMember(13)] public List<MetaDuration>     TurnReserveSpent { get; private set; }
        /// <summary>
        /// Accepted seat actions so far — mulligans, plays, attacks, turn ends and peek answers, a host's
        /// defaulted answer included. It only moves forward, and it is what a host or a board keys "has anything
        /// happened since" on: a bot's armed decision, a bot's mistake stream, the board's presented step. No
        /// rule reads it and no intent carries it; an intent is judged against the state it arrives at.
        /// </summary>
        [MetaMember(15)] public int                    ActionCount      { get; private set; }
        /// <summary> Whether the seat on turn has played, attacked or ended this turn. The reserve gate reads it. </summary>
        [MetaMember(16)] public bool                   ActedThisTurn    { get; private set; }
        /// <summary>
        /// How many peek choices this match has raised. Each held choice is stamped with the count at the moment
        /// it was raised, and the answer echoes it (<see cref="PendingEffectChoice.Id"/>).
        /// </summary>
        [MetaMember(17)] public int                    ChoicesRaised    { get; private set; }

        public MatchRulesState() { }

        public MatchRulesState(MetaRef<WeatherInfo> weather, MatchPhase phase, int turn, int firstSeat, int seatOnTurn, List<SeatState> seats, List<CardInstance> instances)
        {
            Weather          = weather;
            Phase            = phase;
            Turn             = turn;
            FirstSeat        = firstSeat;
            SeatOnTurn       = seatOnTurn;
            Seats            = seats;
            Instances        = instances;
            EffectQueue      = new List<EffectQueueItem>();
            TurnReserveSpent = new List<MetaDuration> { MetaDuration.Zero, MetaDuration.Zero };
        }

        public SeatState Seat(int seat) => Seats[seat];

        public CardInstance Instance(CardInstanceId id) => Instances[id.Value];

        /// <summary> The instance, or null when the id names nothing in this match. </summary>
        public CardInstance TryGetInstance(CardInstanceId id)
            => id.Value >= 0 && id.Value < Instances.Count ? Instances[id.Value] : null;

        // \note These are public rather than internal because the rule actions that call them live in a
        //       different folder of the same assembly, and because the test-side driver in
        //       Backend/SharedCode.Tests assembles a state directly. Nothing outside Match/ calls one.
        public void SetWeather(MetaRef<WeatherInfo> weather) => Weather = weather;
        public void SetFirstSeat(int seat) => FirstSeat = seat;
        public void SetPhase(MatchPhase phase) => Phase = phase;
        /// <summary> A turn opens: its number, and nobody has acted in it yet. </summary>
        public void SetTurn(int turn)
        {
            Turn          = turn;
            ActedThisTurn = false;
        }
        /// <summary> One accepted seat action. <paramref name="actsOnTurn"/> is false for a mulligan and a peek answer. </summary>
        public void CountAction(bool actsOnTurn)
        {
            ActionCount++;
            if (actsOnTurn)
                ActedThisTurn = true;
        }
        /// <summary> Raise one more peek choice and answer its id. </summary>
        public int RaiseChoice() => ++ChoicesRaised;
        public void SetSeatOnTurn(int seat) => SeatOnTurn = seat;
        public void SetResult(MatchResult result) => Result = result;
        public void SetPendingChoice(PendingEffectChoice choice) => PendingChoice = choice;
        public void SetContinuation(ResolutionContinuation continuation) => Continuation = continuation;
        public void SpendTurnReserve(int seat, MetaDuration amount) => TurnReserveSpent[seat] += amount;
    }

    /// <summary>
    /// One seat's public zones and counters. Index in <see cref="MatchRulesState.Seats"/> is the seat number.
    /// <para>
    /// The deck and the hand are <b>not here</b>: they are in <see cref="SeatSecrets"/>. What is here is
    /// their sizes, stored rather than derived, because a follower has no list to count and every rule that
    /// touches a hidden zone has to branch on a number both sides hold
    /// (<c>Docs/hidden-information.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class SeatState
    {
        [MetaMember(1)]  public int                  DenHp              { get; private set; }
        [MetaMember(2)]  public int                  Mana               { get; private set; }
        [MetaMember(3)]  public int                  MaxMana            { get; private set; }
        /// <summary> Public in full. Order is entered-play order: a stable iteration order, never a position. </summary>
        [MetaMember(6)]  public List<BoardCritter>   Board              { get; private set; }
        /// <summary> Public in full, in arrival order. What each one is comes off the public registry. </summary>
        [MetaMember(7)]  public List<CardInstanceId> Graveyard          { get; private set; }
        /// <summary> Public. Maintained by <see cref="ZoneOps"/>, in canonical card order, and never grows. </summary>
        [MetaMember(8)]  public List<CardId>         UnseenPool         { get; private set; }
        [MetaMember(9)]  public int                  TuckeredOutTicks   { get; private set; }
        [MetaMember(10)] public bool                 HasMulliganed      { get; private set; }
        /// <summary> Every card this seat played from hand, in play order. The <c>Per:PlayedThisMatch</c> counter. </summary>
        [MetaMember(11)] public List<CardId>         PlayedThisMatch    { get; private set; }
        /// <summary> Reset in the start-of-turn package. Acorn Rain's "first trick each turn" reads it. </summary>
        [MetaMember(13)] public int                  TricksCastThisTurn { get; private set; }
        /// <summary>
        /// How many cards are in this seat's deck. <b>Stored, not derived</b>: the deck itself is in the
        /// seat's secret and a follower has no way to count it. Every zone op that moves a card into or out
        /// of a deck maintains this, and the follower runs the same op — <see cref="SecretOps"/> does the
        /// move, the public op does the count.
        /// </summary>
        [MetaMember(15)] public int                  DeckCount          { get; private set; }
        /// <summary> Stored, for the same reason. This is the number the opponent's card backs are drawn from. </summary>
        [MetaMember(16)] public int                  HandCount          { get; private set; }
        /// <summary>
        /// The Heist input: cards played from this seat's own starting deck, each with the rank its owner
        /// brought it at: the Heist screen needs the rank to say what a pick would take.
        /// </summary>
        [MetaMember(19)] public List<HeistEligibleCard> PlayedFromOwnDeck { get; private set; }

        public SeatState() { }

        public SeatState(int denHp, int mana, int maxMana, int tuckeredOutTicks = 0, bool hasMulliganed = false, int tricksCastThisTurn = 0)
        {
            DenHp              = denHp;
            Mana               = mana;
            MaxMana            = maxMana;
            TuckeredOutTicks   = tuckeredOutTicks;
            HasMulliganed      = hasMulliganed;
            TricksCastThisTurn = tricksCastThisTurn;
            Board              = new List<BoardCritter>();
            Graveyard          = new List<CardInstanceId>();
            UnseenPool         = new List<CardId>();
            PlayedThisMatch    = new List<CardId>();
            PlayedFromOwnDeck  = new List<HeistEligibleCard>();
        }

        /// <summary> The critter with this identity on this board, or null. </summary>
        public BoardCritter FindCritter(CardInstanceId id)
        {
            for (int ndx = 0; ndx < Board.Count; ndx++)
            {
                if (Board[ndx].Id == id)
                    return Board[ndx];
            }
            return null;
        }

        public void SetDenHp(int hp) => DenHp = hp;
        public void SetMana(int mana) => Mana = mana;
        public void SetMaxMana(int maxMana) => MaxMana = maxMana;
        public void SetTuckeredOutTicks(int ticks) => TuckeredOutTicks = ticks;
        public void SetTricksCastThisTurn(int value) => TricksCastThisTurn = value;

        /// <summary> The two stored sizes, written together because every zone move changes both or neither. </summary>
        public void SetCounts(int handCount, int deckCount)
        {
            HandCount = handCount;
            DeckCount = deckCount;
        }

        /// <summary> This seat has answered the mulligan. </summary>
        public void SetMulliganed() => HasMulliganed = true;
    }

    /// <summary>
    /// One row of what a Heist could take: the card, and the rank its owner brought it at. The rank is here
    /// because the pick moves a card <em>at a rank</em>, and the loser's rank is the only one that answers
    /// what the winner gains.
    /// </summary>
    [MetaSerializable]
    public readonly struct HeistEligibleCard
    {
        [MetaMember(1)] public readonly CardId Card;
        [MetaMember(2)] public readonly int    OwnedRank;

        [MetaDeserializationConstructor]
        public HeistEligibleCard(CardId card, int ownedRank)
        {
            Card      = card;
            OwnedRank = ownedRank;
        }

        public override string ToString() => $"{Card} r{OwnedRank}";
    }

    /// <summary> What the rules do once the effect queue reaches the end. </summary>
    [MetaSerializable]
    public enum ResolutionContinuation
    {
        None = 0,
        /// <summary> A player action resolved: check the Dens; the turn goes on. </summary>
        FinishAction    = 1,
        /// <summary> End-of-turn effects resolved: check the Dens and hand the turn over. </summary>
        FinishEndTurn   = 2,
        /// <summary> The start-of-turn package resolved: check the Dens and open the seat's turn. </summary>
        FinishStartTurn = 3,
    }

    /// <summary>
    /// A resolution held for one seat's choice: the launch pool's peek effect, which cannot decide at cast
    /// time because the information does not exist until the effect reveals it
    /// (<c>Docs/effects.md</c>, "The one interactive resolution"). The queue is held, not abandoned,
    /// and nobody else may act meanwhile.
    /// <para>
    /// Only the public half is here. The revealed identities are in
    /// <see cref="SeatSecrets.PeekRevealed"/> and reach their owner on the private channel.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PendingEffectChoice
    {
        [MetaMember(1)] public int Seat          { get; private set; }
        /// <summary>
        /// Which choice this is, from <see cref="MatchRulesState.ChoicesRaised"/>. The answer names indices
        /// within one reveal, so it echoes this id and an answer to an earlier reveal is refused.
        /// </summary>
        [MetaMember(5)] public int Id            { get; private set; }
        /// <summary> How many of the revealed cards the seat keeps; the rest go to the bottom of the deck. </summary>
        [MetaMember(3)] public int KeepCount     { get; private set; }
        /// <summary> How many were revealed. Public: it is a count, and the count is what a spectator may see. </summary>
        [MetaMember(4)] public int RevealedCount { get; private set; }

        public PendingEffectChoice() { }

        public PendingEffectChoice(int id, int seat, int revealedCount, int keepCount)
        {
            Id            = id;
            Seat          = seat;
            RevealedCount = revealedCount;
            KeepCount     = keepCount;
        }
    }

    /// <summary>
    /// What the rules produce at the end. What the played-card lists are worth is not the rules' business:
    /// locks, stakes tiers, the pick and the transfer are all the host's.
    /// </summary>
    [MetaSerializable]
    public class MatchResult
    {
        [MetaMember(1)] public MatchOutcome  Outcome    { get; private set; }
        /// <summary> The winning seat, or <see cref="MatchSeats.None"/> on a draw. </summary>
        [MetaMember(2)] public int           WinnerSeat { get; private set; }
        [MetaMember(5)] public int           FinalTurn  { get; private set; }
        [MetaMember(6)] public MatchEndCause Cause      { get; private set; }
        /// <summary> Per seat, the cards it played from its own starting deck, with their owned ranks. </summary>
        [MetaMember(7)] public List<HeistEligibleCard> PlayedBySeat0 { get; private set; }
        [MetaMember(8)] public List<HeistEligibleCard> PlayedBySeat1 { get; private set; }

        public MatchResult() { }

        public MatchResult(MatchOutcome outcome, int winnerSeat, List<HeistEligibleCard> playedBySeat0, List<HeistEligibleCard> playedBySeat1, int finalTurn, MatchEndCause cause)
        {
            Outcome       = outcome;
            WinnerSeat    = winnerSeat;
            PlayedBySeat0 = playedBySeat0;
            PlayedBySeat1 = playedBySeat1;
            FinalTurn     = finalTurn;
            Cause         = cause;
        }

        public IReadOnlyList<HeistEligibleCard> PlayedBy(int seat) => seat == 0 ? PlayedBySeat0 : PlayedBySeat1;

        public bool IsDraw => Outcome == MatchOutcome.Draw;

        public override string ToString() => $"{Outcome} on turn {FinalTurn} ({Cause})";
    }
}
