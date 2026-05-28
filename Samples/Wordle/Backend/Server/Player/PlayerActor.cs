// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Server;
using System;
using static System.FormattableString;

namespace Game.Server.Player
{
    [EntityConfig]
    public class PlayerConfig : PlayerConfigBase
    {
        public override Type EntityActorType => typeof(PlayerActor);
    }

#if TUTORIAL_PART3
    /// <summary>
    /// Entity actor class representing a player.
    /// </summary>
    #region snippet1
    #region snippet2
    public sealed class PlayerActor : PlayerActorBase<PlayerModel>, IPlayerModelServerListener
    {
        protected override void OnSwitchedToModel(PlayerModel model)
        {
            model.ServerListener = this;
        }
    #endregion snippet2
        protected override string RandomNewPlayerName()
        {
            return Invariant($"Guest {Random.Shared.Next(100_000)}");
        }

        public void ServerEvaluateGuess()
        {
            bool isLastRound = (Model.GuessedWords.Count == (PlayerModel.MaxGuesses - 1));
            if (Model.GuessedWords.Count == 0)
            {
                ServerGameConfig serverConfig = (ServerGameConfig)_specializedGameConfig.ServerConfig;  
                Model.Solution = serverConfig.GetSolutionForRound(Model.RoundIndex);
            }
            GuessResult[] result = EvaluateWordGuess(Model.CurrentWord, Model.Solution);
            bool isSolved = (Model.CurrentWord == Model.Solution);

            if (isLastRound || isSolved)
                EnqueueServerAction(new ServerRevealSolution(Model.Solution, result));
            else
                EnqueueServerAction(new ServerUpdateGuess(result));

        }


        private GuessResult[] EvaluateWordGuess(string guess, string solution)
        {
            char[] remaining = solution.ToCharArray();
            GuessResult[] results = new GuessResult[guess.Length];

            // Find all correct letters in correct place
            for (int i = 0; i < guess.Length; i++)
            {
                if (guess[i] == remaining[i])
                {
                    results[i] = GuessResult.Correct;
                    remaining[i] = '\0';
                }
            }

            // Find all correct letters in incorrect places
            for (int i = 0; i < guess.Length; i++)
            {
                // Only handle non-matched letters
                if (results[i] == GuessResult.Empty)
                {
                    int foundNdx = Array.IndexOf(remaining, guess[i]);
                    if (foundNdx != -1)
                    {
                        results[i] = GuessResult.Partial;
                        remaining[foundNdx] = '\0';
                    }
                    else
                        results[i] = GuessResult.Wrong;
                }
            }

            return results;
        }
    }
    #endregion snippet1
#else 
    /// <summary>
    /// Entity actor class representing a player.
    /// </summary>
    public sealed class PlayerActor : PlayerActorBase<PlayerModel>
    {
        protected override string RandomNewPlayerName()
        {
            return Invariant($"Guest {Random.Shared.Next(100_000)}");
        }
    }

    #endif
}
