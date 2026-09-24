using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// One card entered this client's hand.
    /// <para>
    /// The public timeline has already moved the counts — a hand grew by one, a deck shrank by one, and every
    /// rule that reads those has run. This says <em>which card</em>, which only its owner may know, and it
    /// rides the timeline so it lands ordered against the operation that changed the counts rather than racing
    /// it (<c>Docs/protocol.md</c>).
    /// </para>
    /// <para>
    /// One card, not a batch: what the SDK executes is one mutation, and a batch would be describing a
    /// sequence. And no seat, because the only client that receives this is the one it is for.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchOwnCardGained)]
    public class MatchOwnCardGained : MatchAddressedAction
    {
        public CardInstanceId Instance { get; private set; }
        public CardId         Card     { get; private set; }
        public int            Rank     { get; private set; }

        public MatchOwnCardGained() { }

        public MatchOwnCardGained(HandCard card)
        {
            Instance = card.Instance;
            Card     = card.Card;
            Rank     = card.Rank;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            match.OwnHand?.Add(new HandCard(Instance, Card, Rank));
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// One card left this client's hand. The counterpart of <see cref="MatchOwnCardGained"/>, and it needs
    /// only the instance: the hand already knows what it is.
    /// <para>
    /// Departures are delivered rather than inferred from a confirmed play. A card leaves a hand for reasons
    /// its owner did not cause — a discard, an overflow to the deck bottom, a mulligan's replacement — and a
    /// client that removed cards only when its own play was confirmed would keep every one of those.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchOwnCardLost)]
    public class MatchOwnCardLost : MatchAddressedAction
    {
        public CardInstanceId Instance { get; private set; }

        public MatchOwnCardLost() { }

        public MatchOwnCardLost(CardInstanceId instance)
        {
            Instance = instance;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            // A card that is not held is ignored: the baseline this applies to may post-date the departure.
            match.OwnHand?.RemoveAll(card => card.Instance == Instance);
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }
}
