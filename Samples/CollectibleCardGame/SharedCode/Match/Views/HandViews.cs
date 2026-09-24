using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary> What a held resolution is showing its owner. Private to that seat; never on the model's public half. </summary>
    [MetaSerializable]
    public class PendingChoiceView
    {
        [MetaMember(1)] public List<HandCard> Revealed  { get; private set; }
        [MetaMember(2)] public int                KeepCount { get; private set; }

        public PendingChoiceView() { }

        public PendingChoiceView(List<HandCard> revealed, int keepCount)
        {
            Revealed  = revealed;
            KeepCount = keepCount;
        }
    }

    /// <summary>
    /// Building one seat's peek payload. Server-side by construction: it reads the seat's secret, so on a
    /// follower it answers null rather than a wrong answer.
    /// </summary>
    public static class HandViews
    {
        /// <summary> What a held peek is showing this seat, or null when no resolution is held on it. </summary>
        public static PendingChoiceView BuildPendingChoice(MatchModel match, int seat)
        {
            PendingEffectChoice pending = match.Rules.PendingChoice;
            if (pending == null || pending.Seat != seat)
                return null;

            List<CardInstanceId> revealed     = SecretOps.PeekRevealed(match, seat);
            List<HandCard>   revealedView = new List<HandCard>(revealed.Count);

            foreach (CardInstanceId id in revealed)
            {
                HiddenCard? hidden = SecretOps.HiddenCardOf(match, id);
                if (hidden == null)
                    continue;

                revealedView.Add(new HandCard(id, hidden.Value.CardId, hidden.Value.Rank));
            }

            return new PendingChoiceView(revealedView, pending.KeepCount);
        }
    }
}
