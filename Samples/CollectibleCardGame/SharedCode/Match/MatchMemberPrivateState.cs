using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One seat's own view of the match — the hand it holds, and what a peek held at subscribe time is showing
    /// it — delivered on the SDK's per-subscriber private-state channel. This covers the deal and every reconnect with no custom protocol and no request/response round trip, and the client applies
    /// it before the initial checksum check — so a hand never has to race the channel becoming known
    /// (<c>Docs/protocol.md</c>, "What replication actually guarantees").
    /// <para>
    /// This is the <b>baseline</b> and the only time a hand travels whole. Every change after it is addressed
    /// to this seat as a timeline operation — <see cref="MatchOwnCardGained"/>, <see cref="MatchOwnCardLost"/>
    /// and <see cref="MatchOwnPeekRevealed"/> — which is the hot path; this is the opening one.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(200)]
    public class MatchMemberPrivateState : MultiplayerMemberPrivateStateBase
    {
        [MetaMember(2)] public List<HandCard>    Hand { get; private set; }
        /// <summary> Only set when a resolution is already held on this seat as it subscribes. </summary>
        [MetaMember(3)] public PendingChoiceView Peek { get; private set; }

        MatchMemberPrivateState() { }

        public MatchMemberPrivateState(EntityId memberId, List<HandCard> hand, PendingChoiceView peek) : base(memberId)
        {
            Hand = hand;
            Peek = peek;
        }

        /// <summary>
        /// Install the baseline. Applied before the initial checksum check, so a client's own view is in place
        /// before the first addressed operation can arrive.
        /// </summary>
        public override void ApplyToModel(IModel model)
        {
            MatchModel match = (MatchModel)model;
            match.OwnHand = Hand;
            match.OwnPeek = Peek;
        }
    }
}
