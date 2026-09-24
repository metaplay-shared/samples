using Game.Logic;
using Game.Server.Match;
using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// One player's place in the ranked queue: the seat the account would take, frozen at enqueue, plus the
    /// facts only the queue reads. It rides the enqueue cast as it is.
    /// <para>
    /// <b>Every field is frozen at enqueue</b> (<c>Docs/matchmaking.md</c>, "The queue service"): a
    /// Power Score the client asserted would make every band and stakes tier a client-side claim, a deck chosen
    /// after pairing would let a player queue light and play heavy, and a padlock toggled after pairing would
    /// withdraw a card the pre-match screen had shown as at risk.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public sealed class MatchmakingTicket
    {
        /// <summary> The seat this account takes if formed: identity, deck, ranks, locks and rating. </summary>
        [MetaMember(1)] public MatchSeatSetup Seat                 { get; private set; }

        /// <summary> The queued deck's Power Score, computed server-side. </summary>
        [MetaMember(2)] public int            PowerScore           { get; private set; }

        /// <summary> The newcomer shield's input. </summary>
        [MetaMember(3)] public int            RankedMatchesPlayed  { get; private set; }

        /// <summary> Whether this account's own shield is waived. The other seat's shield still shields the match. </summary>
        [MetaMember(4)] public bool           NewcomerShieldWaived { get; private set; }

        /// <summary> Which deck this is, so the seating cast can tell the account what it was seated with. </summary>
        [MetaMember(5)] public DeckChoice     DeckChoice           { get; private set; }

        /// <summary>
        /// When this ticket arrived, stamped by the player's own actor. Every band and the fill wait are
        /// measured against it, and a released ticket is re-queued with the stamp it came with.
        /// </summary>
        [MetaMember(6)] public MetaTime       ArrivedAt            { get; private set; }

        public EntityId PlayerId => Seat.PlayerId;

        /// <summary> The pairing axis, read off <c>PlayerRecord.Rating</c> at enqueue. </summary>
        public int      Rating   => Seat.Rating;

        MatchmakingTicket() { }

        public MatchmakingTicket(MatchSeatSetup seat, int powerScore, int rankedMatchesPlayed, bool newcomerShieldWaived, DeckChoice deckChoice, MetaTime arrivedAt)
        {
            Seat                 = seat;
            PowerScore           = powerScore;
            RankedMatchesPlayed  = rankedMatchesPlayed;
            NewcomerShieldWaived = newcomerShieldWaived;
            DeckChoice           = deckChoice;
            ArrivedAt            = arrivedAt;
        }
    }
}
