using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Game.Logic
{
    /// <summary>
    /// The lifecycle of a table. <see cref="Ended"/> is a finished game with results. <see cref="Abandoned"/> has
    /// no results and happens only when play never started (see <c>docs/match.md</c>, "Phases").
    /// </summary>
    [MetaSerializable]
    public enum MatchPhase
    {
        Playing   = 0,
        Ended     = 1,
        Abandoned = 2,
    }

    /// <summary>
    /// Who plays a seat. A seat in <see cref="HumanCoveredByBot"/> keeps its owner's identity, and the client
    /// marks it as covered (<c>docs/match.md</c>, "When players stop playing").
    /// </summary>
    [MetaSerializable]
    public enum MatchSeatOccupancy
    {
        Human             = 0,
        Bot               = 1,
        HumanCoveredByBot = 2,
    }

    /// <summary>
    /// One seat at the table. It is the only place that stores a player identity. The engine, the play history
    /// and the standings use seat indices, so changing who plays a seat changes only this object.
    /// </summary>
    [MetaSerializable]
    public class MatchSeat
    {
        [MetaMember(1)] public int Seat { get; private set; }

        /// <summary>
        /// The seat's public identity: the name and the cosmetics the player wore when the table formed
        /// (<c>docs/cosmetics.md</c>). The client renders players everywhere from <see cref="PlayerPublicIdentity"/>.
        /// <para>
        /// It is a copy taken when the table is dealt and is not refreshed during the game, which lasts only
        /// minutes. The seasonal tournament, whose standings are shown for a whole season, does refresh its copies
        /// (<c>docs/seasonal-tournament.md</c>).
        /// </para>
        /// </summary>
        [MetaMember(9)] public PlayerPublicIdentity Identity { get; private set; }

        /// <summary>
        /// The seat's owner and name in a schema version 1 table, which has no <see cref="Identity"/>. Only
        /// <see cref="MatchModel.MigrateSeatsToPublicIdentity"/> reads them, and it clears them. They are
        /// <c>internal</c> so the migration test can build a version 1 seat.
        /// <para>
        /// A persisted table must survive a server redeploy during a game (<c>docs/match.md</c>, "Persistence").
        /// Tagged serialization skips unknown members without an error, so without these members a version 1
        /// table would load with no owner on any seat, and nothing would report the problem.
        /// </para>
        /// </summary>
        [MetaMember(2)] internal EntityId LegacyPlayerId;
        [MetaMember(3)] internal string   LegacyDisplayName;

        /// <summary>The seat's owner, or <see cref="EntityId.None"/> for a bot seat with no owner.</summary>
        public EntityId PlayerId => Identity?.PlayerId ?? EntityId.None;

        /// <summary>The name shown at the seat. A covered seat shows its owner's name.</summary>
        public string DisplayName => Identity?.DisplayName;

        [MetaMember(4)] public MatchSeatOccupancy Occupancy { get; set; }

        [MetaMember(5)] public bool IsConnected { get; set; }

        /// <summary>
        /// Whether the seat's owner has ever subscribed to the table. The join window rules use it. It differs
        /// from <see cref="IsConnected"/> after a restore from the database, which clears every connected flag but
        /// must not make a table in progress look like nobody ever joined (<c>docs/match.md</c>, "Turn flow").
        /// </summary>
        [MetaMember(6)] public bool HasEverConnected { get; set; }

        /// <summary>
        /// How many move deadlines in a row this seat has missed. Any move from the owner resets it, whether or not
        /// it is accepted. At <see cref="MatchTimings.StrikesBeforeCover"/>, a bot covers the seat.
        /// </summary>
        [MetaMember(7)] public int ConsecutiveMissedDeadlines { get; set; }

        /// <summary>
        /// When this seat's disconnect grace ends, or <see cref="MetaTime.Epoch"/> when no grace timer is running.
        /// It is an absolute time because the match does not tick, and a table restored from the database
        /// reschedules its timers from the stored times.
        /// </summary>
        [MetaMember(8)] public MetaTime GraceEndsAt { get; set; }

        public MatchSeat() { }

        /// <summary>
        /// Create a seat for a new table. <paramref name="hasArrived"/> sets both <see cref="IsConnected"/> and
        /// <see cref="HasEverConnected"/>. A server table is created before its players subscribe, so it passes
        /// false and the join window waits for them. A host that creates the table for a session it already
        /// serves passes true.
        /// <para>
        /// A bot seat gets only a name here. <see cref="MatchHost.SetupNewMatch"/> draws its cosmetics from the
        /// table seed.
        /// </para>
        /// </summary>
        public MatchSeat(int seat, PlayerPublicIdentity identity, MatchSeatOccupancy occupancy, bool hasArrived)
        {
            Seat             = seat;
            Identity         = identity;
            Occupancy        = occupancy;
            IsConnected      = hasArrived;
            HasEverConnected = hasArrived;
            GraceEndsAt      = MetaTime.Epoch;
        }

        /// <summary>
        /// Create a seat for a new server table whose player has not subscribed yet. The seat is
        /// <see cref="MatchSeatOccupancy.Human"/> when <paramref name="identity"/> has a valid player id, otherwise
        /// <see cref="MatchSeatOccupancy.Bot"/>. The seat is not connected, because clients learn which table they
        /// are in only after it is created. A seat marked connected would end the join window on the first update,
        /// before any client subscribed (<c>docs/match.md</c>, "Turn flow").
        /// </summary>
        public static MatchSeat NotYetArrived(int seat, PlayerPublicIdentity identity)
        {
            MatchSeatOccupancy occupancy = identity != null && identity.PlayerId.IsValid ? MatchSeatOccupancy.Human : MatchSeatOccupancy.Bot;
            return new MatchSeat(seat, identity, occupancy, hasArrived: false);
        }

        /// <summary>
        /// Replace the seat's identity with one that has drawn cosmetics. Only <see cref="MatchHost.SetupNewMatch"/>
        /// calls this, and only for seats with no owner. A player's cosmetics come from their own model.
        /// </summary>
        internal void ReplaceIdentity(PlayerPublicIdentity identity)
        {
            Identity = identity;
        }

        /// <summary>
        /// Build <see cref="Identity"/> from <see cref="LegacyPlayerId"/> and <see cref="LegacyDisplayName"/>, then
        /// clear them. The identity has no cosmetics because a version 1 table did not store any, and the client
        /// shows defaults for empty cosmetic slots.
        /// </summary>
        internal void AdoptLegacyIdentity()
        {
            if (Identity == null)
                Identity = PlayerPublicIdentity.ForSeat(LegacyPlayerId, LegacyDisplayName);

            LegacyPlayerId    = EntityId.None;
            LegacyDisplayName = null;
        }

        /// <summary>Whether a bot plays this seat's turns, either as a bot seat or covering the owner.</summary>
        public bool IsPlayedByBot => Occupancy != MatchSeatOccupancy.Human;

        /// <summary>Whether a player owns this seat. A bot seat filled at formation has no owner.</summary>
        public bool HasOwner => PlayerId.IsValid;

        /// <summary>Whether a grace timer is running for this seat.</summary>
        public bool IsInGrace => GraceEndsAt > MetaTime.Epoch;

        public override string ToString() => $"seat {Seat}: {DisplayName} ({Occupancy})";
    }

    /// <summary>
    /// The hand delivered to one client, and the play index at which it was read.
    /// <para>
    /// It exists only on a client. The host reads hands from the engine and has no own seat.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MatchOwnHand
    {
        [MetaMember(1)] public int Seat { get; private set; }

        /// <summary>The cards as delivered. The current hand is these minus the cards the board shows as played.</summary>
        [MetaMember(2)] List<Card> _cards;

        /// <summary>The play index at which the hand was read. The client applies it only when its board reaches this index.</summary>
        [MetaMember(3)] public int PlayIndex { get; private set; }

        MatchOwnHand() { }

        public MatchOwnHand(int seat, IReadOnlyList<Card> cards, int playIndex)
        {
            Seat      = seat;
            _cards    = new List<Card>(cards);
            PlayIndex = playIndex;
        }

        public IReadOnlyList<Card> Cards => _cards;
    }

    /// <summary>
    /// Client callbacks for match timeline events. The host uses <see cref="EmptyMatchModelClientListener"/>.
    /// </summary>
    public interface IMatchModelClientListener
    {
        void OnCardPlayed(int seat, Card card);
        void OnTrickResolved(int winnerSeat);
        void OnMatchEnded();
    }

    public sealed class EmptyMatchModelClientListener : IMatchModelClientListener
    {
        public static readonly EmptyMatchModelClientListener Instance = new EmptyMatchModelClientListener();

        public void OnCardPlayed(int seat, Card card) { }
        public void OnTrickResolved(int winnerSeat) { }
        public void OnMatchEnded() { }
    }

    /// <summary>
    /// Copies <see cref="MatchModel.ClientListener"/> to the model copies that the SDK's client journal makes. The
    /// property is <see cref="IgnoreDataMemberAttribute"/>, so without this the first copy would lose it and the
    /// UI would stop receiving events.
    /// </summary>
    public class MatchModelRuntimeData : MultiplayerModelRuntimeDataBase<MatchModel>
    {
        readonly IMatchModelClientListener _clientListener;

        public MatchModelRuntimeData(MatchModel instance) : base(instance)
        {
            _clientListener = instance.ClientListener;
        }

        public override void CopySideEffectListenersTo(MatchModel instance)
        {
            base.CopySideEffectListenersTo(instance);
            instance.ClientListener = _clientListener;
        }
    }

    /// <summary>
    /// The replicated state of one table. Every subscriber has the same checksummed timeline, which holds the public
    /// board and the seats. The engine and other host data are server-only members, which the SDK neither sends nor
    /// checksums. Only the host changes this model, through a <see cref="MatchAction"/>.
    /// <para>
    /// The match does not tick. Messages and host timers drive it, so model time does not advance, and every client
    /// countdown compares the current time against an absolute time on <see cref="Board"/>.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(10)]
    [SupportedSchemaVersions(1, 2)]
    [MetaBlockedMembers(5)]
    public class MatchModel : MultiplayerModelBase<MatchModel>
    {
        // Tags 200-299 are reserved by MultiplayerModelBase.
        [MetaMember(1)] public MatchPhase Phase { get; private set; }

        [MetaMember(2)] List<MatchSeat> _seats;

        [MetaMember(3)] MatchBoard _board;

        /// <summary>
        /// The authoritative game: every hand, the undealt cards, the deck order and the RNG state. It is null on every
        /// client, so an action must never read it.
        /// <para>
        /// It must be <see cref="ServerOnlyAttribute"/> (Hidden plus NoChecksum), not only Hidden. A Hidden-only member
        /// is still checksummed, and the SDK's desync report serializes every checksummed member, so the report would
        /// send the whole deck to a client (<c>docs/match.md</c>).
        /// </para>
        /// </summary>
        [MetaMember(4), ServerOnly] MatchEngine _engine;

        // Member id 5 is blocked with [MetaBlockedMembers] because a persisted table may still contain data under
        // it. A new member reusing the id would read that old data. The attribute makes reuse a build error.

        /// <summary>
        /// The bot profile drawn for each seat, or null for a seat a human plays. Server-only so clients cannot
        /// see how strong each bot is.
        /// <para>
        /// It stores the profile's values, not a config reference, so a config update during the match does not
        /// change a bot, and removing a profile from config cannot break a table in progress
        /// (<c>docs/bots.md</c>, "Profiles").
        /// </para>
        /// </summary>
        [MetaMember(14), ServerOnly] List<BotProfile> _seatBotProfiles;

        /// <summary>
        /// The viewing client's own hand. It exists only on a client, set from the subscribe's private state and
        /// from <see cref="MatchHandDelivered"/> messages.
        /// <para>
        /// It is marked ServerOnly even though only clients set it, because that keeps it out of the checksum.
        /// Each client holds a different hand but must compute the same checksum.
        /// </para>
        /// </summary>
        [MetaMember(6), ServerOnly] MatchOwnHand _ownHand;

        /// <summary>
        /// When the match entered a terminal phase, or <see cref="MetaTime.Epoch"/> while it is being played.
        /// Written once, by the action that ends the match.
        /// </summary>
        [MetaMember(7)] public MetaTime EndedAt { get; private set; }

        /// <summary>
        /// Whether play has begun. A table is dealt when it is created, but play begins only when every human seat
        /// has subscribed or the join window has ended, so a slow-loading client does not miss the first cards
        /// (<c>docs/match.md</c>, "Turn flow").
        /// </summary>
        [MetaMember(8)] public bool PlayHasBegun { get; private set; }

        /// <summary>
        /// When the join window ends. Not used once <see cref="PlayHasBegun"/> is true.
        /// </summary>
        [MetaMember(9)] public MetaTime JoinWindowEndsAt { get; private set; }

        /// <summary>
        /// When a card was last played. Only an accepted move sets it. A snapshot or a reconnect does not, so a
        /// table where nobody plays does not look active (<c>docs/match.md</c>, "Persistence").
        /// </summary>
        [MetaMember(10)] public MetaTime LastActivityAt { get; private set; }

        /// <summary>
        /// Each human seat's result, captured when the match enters <see cref="MatchPhase.Ended"/>, with a
        /// delivered flag per seat. Null while the game is played and for an abandoned match.
        /// <para>
        /// It is server-only. It is persisted, so after a restart the actor knows which seats still need their
        /// result delivered. Clients read the standings from <see cref="Board"/> instead. Because it is not
        /// checksummed, the host writes it directly rather than through an action.
        /// </para>
        /// </summary>
        [MetaMember(11), ServerOnly] List<MatchSeatResult> _seatResults;

        /// <summary>
        /// Why each seat's owner is not playing it, or <see cref="MatchSeatLossReason.None"/>. A reclaim clears
        /// the reason, so at the end of the game it holds the current reason, which the captured results send to
        /// analytics. Server-only because no client needs it.
        /// </summary>
        [MetaMember(12), ServerOnly] List<MatchSeatLossReason> _seatLossReasons;

        /// <summary>
        /// When the table was dealt. With <see cref="EndedAt"/> it gives the game's duration, which the finished
        /// match reports (<c>docs/player.md</c>, "Delivery from the match").
        /// </summary>
        [MetaMember(13)] public MetaTime DealtAt { get; private set; }

        [IgnoreDataMember] public IMatchModelClientListener ClientListener { get; set; } = EmptyMatchModelClientListener.Instance;

        /// <summary>One rather than zero because the SDK divides by it to compute model time. The model does not tick.</summary>
        [IgnoreDataMember] public override int TicksPerSecond => 1;

        public MatchModel() { }

        public override IModelRuntimeData<MatchModel> GetRuntimeData() => new MatchModelRuntimeData(this);

        /// <summary>Does nothing. Messages and host timers drive the match.</summary>
        public override void OnTick() { }

        /// <summary>Does nothing, because no state depends on elapsed model time.</summary>
        public override void OnFastForwardTime(MetaDuration elapsedTime) { }

        /// <summary>
        /// Schema version 1 to 2: build each seat's <see cref="MatchSeat.Identity"/> from its legacy owner and name
        /// fields (<c>docs/cosmetics.md</c>). The migrated seats have no cosmetics, because version 1 did not store any.
        /// <para>
        /// A persisted table must survive a server redeploy during a game. Without this migration, a version 1 table
        /// would load with no owner on any seat, so it would accept no move, give no subscriber a seat and deliver no
        /// result, without reporting an error.
        /// </para>
        /// </summary>
        [MigrationFromVersion(1)]
        void MigrateSeatsToPublicIdentity()
        {
            foreach (MatchSeat seat in _seats ?? new List<MatchSeat>())
                seat.AdoptLegacyIdentity();
        }

        public override string GetDisplayNameForDashboard() => $"Table Stakes {EntityId}";

        #region Public state

        public MatchBoard Board => _board;

        public IReadOnlyList<MatchSeat> Seats => _seats;

        public MatchSeat GetSeat(int seat)
        {
            MatchRules.ThrowIfInvalidSeat(seat);
            return _seats[seat];
        }

        public bool IsFinished => Phase != MatchPhase.Playing;

        /// <summary>The seat <paramref name="playerId"/> occupies, or -1 if that player is not at this table.</summary>
        public int FindSeatOfPlayer(EntityId playerId)
        {
            if (_seats == null || !playerId.IsValid)
                return -1;
            foreach (MatchSeat seat in _seats)
            {
                if (seat.PlayerId == playerId)
                    return seat.Seat;
            }
            return -1;
        }

        #endregion

        #region The viewer's own hand (client side only)

        /// <summary>This client's seat, or -1 before a hand has been delivered.</summary>
        public int OwnSeat => _ownHand?.Seat ?? -1;

        /// <summary>The play index of the applied hand delivery, or -1 if none has been applied.</summary>
        public int OwnHandDeliveredAtPlayIndex => _ownHand?.PlayIndex ?? -1;

        /// <summary>
        /// The cards this client still holds: the delivered hand minus the cards the board shows as played.
        /// Computing it this way means applying the same delivery twice gives the same hand.
        /// </summary>
        public List<Card> GetOwnHand()
        {
            List<Card> hand = new List<Card>(MatchRules.CardsPerSeat);
            if (_ownHand == null)
                return hand;

            foreach (Card card in _ownHand.Cards)
            {
                if (!_board.HasBeenPlayed(card))
                    hand.Add(card);
            }
            return hand;
        }

        /// <summary>This client's seat view: the replicated board plus its own hand. Null before a hand has been delivered.</summary>
        public MatchSeatView GetOwnSeatView()
        {
            int seat = OwnSeat;
            if (seat < 0)
                return null;
            return MatchSeatView.FromBoard(_board, seat, GetOwnHand());
        }

        /// <summary>
        /// Apply a hand from the subscribe's private state or a <see cref="MatchHandDelivered"/> message, once the
        /// board has reached the hand's play index. An entity message can arrive before the timeline update that
        /// caused it (see <see cref="MatchHandDelivered"/>), so the play index decides, not the arrival order.
        /// </summary>
        /// <returns>
        /// Whether the hand was applied. When it returns false, the caller keeps the hand and retries as the board
        /// advances.
        /// </returns>
        public bool TryDeliverOwnHand(MatchOwnHand hand)
        {
            if (hand == null)
                throw new ArgumentNullException(nameof(hand));
            MatchRules.ThrowIfInvalidSeat(hand.Seat);

            if (_board == null || _board.PlayIndex < hand.PlayIndex)
                return false;

            // Replace the local hand completely, so a wrong local hand is corrected rather than merged.
            _ownHand = hand;
            return true;
        }

        #endregion

        #region Host side only

        /// <summary>
        /// The authoritative game. Null on every client. Only the host reads it, never an action.
        /// </summary>
        public MatchEngine Engine => _engine;

        /// <summary>The bot profile for seat <paramref name="seat"/>, or null for a seat a human plays.</summary>
        public BotProfile GetSeatBotProfile(int seat)
        {
            MatchRules.ThrowIfInvalidSeat(seat);
            if (_seatBotProfiles == null)
                return null;
            return _seatBotProfiles[seat];
        }

        /// <summary>
        /// Set up a new table around a dealt engine. The board is built from the engine once here, and after that
        /// only actions change it.
        /// </summary>
        public void Setup(MatchEngine engine, List<MatchSeat> seats, List<BotProfile> seatBotProfiles, MetaTime now)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));
            if (seats == null || seats.Count != MatchRules.NumSeats)
                throw new ArgumentException($"A table has exactly {MatchRules.NumSeats} seats", nameof(seats));
            if (seatBotProfiles == null || seatBotProfiles.Count != MatchRules.NumSeats)
                throw new ArgumentException($"A table has exactly {MatchRules.NumSeats} seats", nameof(seatBotProfiles));

            _engine           = engine;
            _seats            = seats;
            _seatBotProfiles  = seatBotProfiles;
            _board            = MatchBoard.Build(engine);
            Phase             = MatchPhase.Playing;
            JoinWindowEndsAt  = now + engine.Timings.JoinWindow;
            LastActivityAt    = now;
            DealtAt           = now;
        }

        /// <summary>
        /// Each human seat's result, or null until the match ends. An abandoned match never has results.
        /// </summary>
        public IReadOnlyList<MatchSeatResult> SeatResults => _seatResults;

        /// <summary>Whether the results have been captured. Once true, it stays true.</summary>
        public bool HasCapturedResults => _seatResults != null;

        /// <summary>
        /// Store each human seat's result. Host-only. Throws <see cref="InvalidOperationException"/> on a second
        /// call, because a later capture from a restored table would read seat state that
        /// <see cref="MatchHost.NoteColdWake"/> has cleared.
        /// </summary>
        public void CaptureSeatResults(List<MatchSeatResult> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));
            if (_seatResults != null)
                throw new InvalidOperationException("This table's results have already been captured");

            _seatResults = results;
        }

        /// <summary>Why the owner of <paramref name="seat"/> is not playing it, or <see cref="MatchSeatLossReason.None"/>.</summary>
        public MatchSeatLossReason GetSeatLossReason(int seat)
        {
            MatchRules.ThrowIfInvalidSeat(seat);
            if (_seatLossReasons == null)
                return MatchSeatLossReason.None;
            return _seatLossReasons[seat];
        }

        /// <summary>
        /// Record why the owner stopped playing a seat, or pass <see cref="MatchSeatLossReason.None"/> when the owner
        /// reclaims it. Host-only. The first reason is kept until the seat is reclaimed, so a player who taps Leave and
        /// then closes the page keeps the Leave reason instead of a disconnect. <see cref="MatchSeatLossReason.None"/>
        /// is always applied, so a loss after a reclaim is recorded again.
        /// </summary>
        /// <returns>Whether this call changed the stored reason.</returns>
        public bool NoteSeatLossReason(int seat, MatchSeatLossReason reason)
        {
            MatchRules.ThrowIfInvalidSeat(seat);
            if (_seatLossReasons == null)
            {
                _seatLossReasons = new List<MatchSeatLossReason>(MatchRules.NumSeats);
                for (int index = 0; index < MatchRules.NumSeats; index++)
                    _seatLossReasons.Add(MatchSeatLossReason.None);
            }

            if (reason != MatchSeatLossReason.None && _seatLossReasons[seat] != MatchSeatLossReason.None)
                return false;

            _seatLossReasons[seat] = reason;
            return true;
        }

        /// <summary>
        /// The hand for a subscribing member. The SDK calls this for each subscriber and sends the result only to
        /// that client, which delivers the hand at the deal and on every reconnect without a custom message.
        /// </summary>
        public override MultiplayerMemberPrivateStateBase GetMemberPrivateState(EntityId memberId)
        {
            int seat = FindSeatOfPlayer(memberId);
            if (seat < 0 || _engine == null)
                return null;

            return new MatchMemberPrivateState(memberId, seat, _engine.GetHand(seat), _board.PlayIndex);
        }

        #endregion

        #region Written only by the match actions

        /// <summary>
        /// Set the phase, and set <see cref="EndedAt"/> when the phase is terminal.
        /// </summary>
        internal void ApplyPhase(MatchPhase phase, MetaTime endedAt)
        {
            Phase = phase;
            if (phase != MatchPhase.Playing)
                EndedAt = endedAt;
        }

        /// <summary>Set the end of the join window. Only the test-only force-expire uses this.</summary>
        internal void ApplyJoinWindowEndsAt(MetaTime endsAt)
        {
            JoinWindowEndsAt = endsAt;
        }

        /// <summary>Mark play as begun. It is never reset.</summary>
        internal void ApplyPlayHasBegun()
        {
            PlayHasBegun = true;
        }

        /// <summary>Set <see cref="LastActivityAt"/>. Only <see cref="MatchCardPlayed"/> calls this.</summary>
        internal void ApplyActivity(MetaTime at)
        {
            LastActivityAt = at;
        }

        /// <summary>
        /// Replace every changeable field of one seat, so an action cannot leave a seat partly updated.
        /// </summary>
        internal void ApplySeatState(int seat, in MatchSeatState state)
        {
            MatchRules.ThrowIfInvalidSeat(seat);
            MatchSeat target = _seats[seat];
            target.Occupancy                  = state.Occupancy;
            target.IsConnected                = state.IsConnected;
            target.HasEverConnected           = state.HasEverConnected;
            target.ConsecutiveMissedDeadlines = state.ConsecutiveMissedDeadlines;
            target.GraceEndsAt                = state.GraceEndsAt;
        }

        #endregion

    }

    /// <summary>
    /// One seat's hand, sent by the SDK only to that seat's client when it subscribes. The client applies it
    /// before the SDK checks the model checksum.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class MatchMemberPrivateState : MultiplayerMemberPrivateStateBase
    {
        [MetaMember(1)] public int Seat { get; private set; }

        [MetaMember(2)] List<Card> _hand;

        /// <summary>The play index at which the hand was read. See <see cref="MatchModel.TryDeliverOwnHand"/>.</summary>
        [MetaMember(3)] public int PlayIndex { get; private set; }

        MatchMemberPrivateState() { }

        public MatchMemberPrivateState(EntityId memberId, int seat, IReadOnlyList<Card> hand, int playIndex) : base(memberId)
        {
            Seat      = seat;
            _hand     = new List<Card>(hand);
            PlayIndex = playIndex;
        }

        public IReadOnlyList<Card> Hand => _hand;

        public MatchOwnHand ToOwnHand() => new MatchOwnHand(Seat, _hand, PlayIndex);

        public override void ApplyToModel(IModel model)
        {
            ((MatchModel)model).TryDeliverOwnHand(ToOwnHand());
        }
    }
}
