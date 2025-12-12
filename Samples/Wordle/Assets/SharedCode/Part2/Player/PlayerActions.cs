// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Game-specific player action class, which attaches all game-specific actions to <see cref="PlayerModel"/>.
    /// </summary>
    public abstract class PlayerAction : PlayerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Registry for game-specific ActionCodes, used by the individual PlayerAction classes.
    /// </summary>
    public static class ActionCodes
    {
        public const int PlayerAddLetter = 5000;
        public const int PlayerDeleteLetter = 5001;
        public const int PlayerSubmitGuess = 5002;
        public const int PlayerAdvanceRound = 5003;
    }

    /// <summary>
    /// Game-specific results returned from <see cref="PlayerActionCore.Execute(PlayerModel, bool)"/>.
    /// </summary>
    public static class ActionResult
    {
        // Shadow success result
        public static readonly MetaActionResult Success = MetaActionResult.Success;

        // Game-specific results
        public static readonly MetaActionResult WordEmpty = new MetaActionResult(nameof(WordEmpty));
        public static readonly MetaActionResult WordFull = new MetaActionResult(nameof(WordFull));
        public static readonly MetaActionResult WordIncomplete = new MetaActionResult(nameof(WordIncomplete));
        public static readonly MetaActionResult InvalidLetter = new MetaActionResult(nameof(InvalidLetter));
        public static readonly MetaActionResult TooManyGuesses = new MetaActionResult(nameof(TooManyGuesses));
        public static readonly MetaActionResult StillPlaying = new MetaActionResult(nameof(StillPlaying));
    }

    // Game Actions
    [ModelAction(ActionCodes.PlayerAddLetter)]
    public class PlayerAddLetter : PlayerAction
    {
        // Letter sent by the player
        public char Letter { get; private set; }

        private PlayerAddLetter() { }
        public PlayerAddLetter(char letter) { Letter = letter; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (Letter < 'A' || Letter > 'Z')
                return ActionResult.InvalidLetter;
            if (player.CurrentWord.Length == PlayerModel.WordSize)
                return ActionResult.WordFull;

            if (commit)
            {
                player.CurrentWord = player.CurrentWord + Letter;
            }

            return ActionResult.Success;
        }
    }

    [ModelAction(ActionCodes.PlayerDeleteLetter)]
    public class PlayerDeleteLetter : PlayerAction
    {
        public PlayerDeleteLetter() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (player.CurrentWord.Length == 0)
                return ActionResult.WordEmpty;

            if (commit)
            {
                player.CurrentWord = player.CurrentWord.Substring(0, player.CurrentWord.Length - 1);
            }

            return ActionResult.Success;
        }
    }

    [ModelAction(ActionCodes.PlayerSubmitGuess)]
    public class PlayerSubmitGuess : PlayerAction
    {
        public PlayerSubmitGuess() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (player.CurrentWord.Length != PlayerModel.WordSize)
                return ActionResult.WordIncomplete;
            if (player.GuessedWords.Count == PlayerModel.MaxGuesses)
                return ActionResult.TooManyGuesses;

            if (commit)
            {
                // Evaluate the guess result
                string solution = player.CurrentSolution;
                bool isSolved = player.CurrentWord == solution;
                GuessResult[] result = EvaluateWordGuess(player.CurrentWord, solution);

                // Remember guessed word, evaluated result & clear current word
                player.GuessedWords.Add(player.CurrentWord);
                player.GuessResults.Add(result);
                player.CurrentWord = "";

                // Game is finished with the correct guess, or if guesses ran out
                if (isSolved || (player.GuessedWords.Count == PlayerModel.MaxGuesses))
                    RoundFinished(player, isSolved, solution);
            }

            return ActionResult.Success;
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

        private void RoundFinished(PlayerModel player, bool gameWon, string solution)
        {
            player.RoundStatus = gameWon ? RoundStatus.Win : RoundStatus.Loss;
            if (gameWon)
            {
                player.RoundsWon++;
                player.WinStreak++;
                player.MaxWinStreak = Math.Max(player.MaxWinStreak, player.WinStreak);
            }
            else
            {
                player.WinStreak = 0;
            }
            player.RoundsPlayed++;
        }
    }
    #region snippet1
    [ModelAction(ActionCodes.PlayerAdvanceRound)]
    public class PlayerAdvanceRound : PlayerAction
    {
        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (player.RoundStatus == RoundStatus.Playing)
                return ActionResult.StillPlaying;

            if (commit)
            {
                // Bump to next round & clear game state
                player.RoundIndex++;
                player.CurrentSolution = player.GameConfig.GetSolutionForRound(player.RoundIndex);
                player.RoundStatus = RoundStatus.Playing;
                player.CurrentWord = "";
                player.GuessedWords.Clear();
                player.GuessResults.Clear();
            }

            return ActionResult.Success;
        }
    }
    #endregion snippet1
}
