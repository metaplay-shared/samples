using Game.Logic;
using Metaplay.Core.Player;
using Metaplay.Core.Model;
using System;


namespace Game.Logic
{
    #region snippet1
    [ModelAction(5004)]
    public class ServerUpdateGuess : PlayerSynchronizedServerActionCore<PlayerModel>
    {
        public GuessResult[] Result { get; private set; }
        public ServerUpdateGuess() { }
        public ServerUpdateGuess(GuessResult[] result) { Result = result; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.GuessedWords.Add(player.CurrentWord);
                player.GuessResults.Add(Result);
                player.CurrentWord = "";
                player.ServerEvaluatePending = false;
            }
            return ActionResult.Success;
        }
    }

    [ModelAction(5005)]
    public class ServerRevealSolution : PlayerSynchronizedServerActionCore<PlayerModel>
    {
        public string Solution { get; private set; }
        public GuessResult[] Result { get; private set; }
        public ServerRevealSolution() { }
        public ServerRevealSolution(string solution, GuessResult[] result) { Solution = solution; Result = result; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            bool isSolved = player.CurrentWord == Solution;

            if (commit)
            {
                player.GuessedWords.Add(player.CurrentWord);
                player.GuessResults.Add(Result);
                player.CurrentWord = "";

                if (isSolved || (player.GuessedWords.Count == PlayerModel.MaxGuesses))
                {
                    player.RoundStatus = isSolved ? RoundStatus.Win : RoundStatus.Loss;
                    if (isSolved)
                    {
                        player.RoundsWon++;
                        player.WinStreak++;
                        player.MaxWinStreak = Math.Max(player.MaxWinStreak, player.WinStreak);
                    }
                    else
                    {
                        player.WinStreak = 0;
                    }
                    player.CurrentSolution = Solution;
                    player.RoundsPlayed++;
                }
                player.ServerEvaluatePending = false;
            }
            return ActionResult.Success;
        }
    }
    #endregion snippet1
}
