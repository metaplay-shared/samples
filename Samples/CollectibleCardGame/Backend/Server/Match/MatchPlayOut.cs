using Game.Logic;
using Metaplay.Core;

namespace Game.Server.Match
{
    public sealed partial class MatchActor
    {
        /// <summary>
        /// Play the rest of a game by submitting ordinary rule actions on the seats' behalf, chosen by the
        /// strongest profile. Choosing needs each seat's secret hand, so it runs here; every follower re-runs
        /// the moves. A whole game is about 200 small timeline operations.
        /// </summary>
        void PlayOutRemainder()
        {
            // Every legal action spends mana, spends an attack or ends a turn, and Tuckered Out ends the game.
            const int MaxIterations = 100000;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                if (Model.Rules.Phase == MatchPhase.Complete)
                    return;

                if (!TakeOneStep())
                    throw new MatchEngineException("The play-out is stuck: neither the policy's move nor ending the turn applied");
            }

            throw new MatchEngineException($"The play-out did not terminate within {MaxIterations} steps");
        }

        /// <summary> The rest of one turn, then its end: what a lapsed turn deadline does. </summary>
        void PlayOutTurn()
        {
            int turn = Model.Rules.Turn;

            const int MaxActionsPerTurn = 1000;

            for (int guard = 0; guard < MaxActionsPerTurn; guard++)
            {
                if (Model.Rules.Phase != MatchPhase.Playing || Model.Rules.Turn != turn)
                    return;

                if (!TakeOneStep())
                    throw new MatchEngineException("A lapsed turn deadline is stuck: neither the policy's move nor ending the turn applied");
            }

            throw new MatchEngineException($"A lapsed turn deadline played {MaxActionsPerTurn} actions without ending turn {turn}");
        }

        /// <summary>
        /// One step of a play-out: answer a held choice with the default, resolve the mulligan, or ask the
        /// policy and submit. Ending the turn is the fallback, since it is always legal for the seat on turn.
        /// </summary>
        bool TakeOneStep()
        {
            if (Model.Rules.PendingChoice != null)
                return SubmitDefaultChoice().IsSuccess;

            if (Model.Rules.Phase == MatchPhase.Mulligan)
                return ExecuteMatchAction(new MatchMulliganResolve(), refusalIsBug: false).IsSuccess;

            int         seat   = Model.Rules.SeatOnTurn;
            MatchIntent intent = StrongestPolicy.ChooseAction(SeatView.Build(Model, seat), seat)
                                 ?? new EndTurnIntent();

            if (SubmitIntent(seat, intent).IsSuccess)
                return true;

            return SubmitIntent(seat, new EndTurnIntent()).IsSuccess;
        }
    }
}
