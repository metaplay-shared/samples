using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Server-issued developer shortcut. Authorization belongs to the actor's authenticated seat handler.
    /// <para>
    /// The starting-deck lists are what make the Heist usable even from the mulligan, and they are read off
    /// the secret in <see cref="ServerPrepare"/> — this is the second action that turns hidden identities
    /// into public ones, and the only one that does it wholesale. That is the point of it: a developer ends
    /// the game early and the Heist still has a lineup to pick from. Locks still protect cards.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchDebugWin)]
    public class MatchDebugWin : MatchHostAction
    {
        public int Winner { get; private set; }
        public List<HeistEligibleCard> Cards0 { get; private set; }
        public List<HeistEligibleCard> Cards1 { get; private set; }
        public MatchDebugWin() { }

        /// <summary>
        /// The lineups are deliberately not parameters: they come off the secret, so they are read where the
        /// secret is readable — <see cref="ServerPrepare"/>.
        /// </summary>
        public MatchDebugWin(int winner)
        {
            Winner = winner;
        }

        public override MatchIntentResult ServerPrepare(MatchModel match)
        {
            if (!MatchSeats.IsValid(Winner) || match.Result != null || match.Rules.Phase == MatchPhase.Complete)
                return MatchIntentResults.InvalidHostAction;

            Cards0 = StartingDeckOf(match, 0);
            Cards1 = StartingDeckOf(match, 1);
            return MatchIntentResults.Success;
        }

        /// <summary>
        /// Everything a seat brought, at the rank it brought it, in canonical card order. Reads through
        /// <see cref="CardLookup"/>, which falls back to the secret — so on the server this names cards that
        /// are still in a hand or a deck, which is exactly what the shortcut is for.
        /// </summary>
        static List<HeistEligibleCard> StartingDeckOf(MatchModel match, int seat)
        {
            List<HeistEligibleCard> cards = new List<HeistEligibleCard>();
            foreach (CardInstance instance in match.Rules.Instances)
            {
                if (instance.Owner != seat || !instance.FromStartingDeck)
                    continue;

                CardId id = CardLookup.CardId(match, instance.Id);
                if (id != null)
                    cards.Add(new HeistEligibleCard(id, CardLookup.Rank(match, instance.Id)));
            }

            cards.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.Card.Value, b.Card.Value));
            return cards;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            // This deliberate developer override bypasses practice/shield/favourite stakes for one real pick.
            match.Stakes = new MatchStakes(true, StakesTier.Even, match.Stakes.PowerScores, match.Stakes.LockedCards);
            MatchResult result = new MatchResult(Winner == 0 ? MatchOutcome.Seat0Wins : MatchOutcome.Seat1Wins,
                Winner, Cards0, Cards1, match.Rules.Turn, MatchEndCause.DeveloperWin);
            ResolutionRules.EndGame(match, result);
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }
}
