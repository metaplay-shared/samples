using Game.Logic;
using System;
using System.Collections.Generic;
using UnityEngine;

public class GameLogic : MonoBehaviour
{
    public List<Row>    GuessRows;
    public GameObject   ResultDialog;

    // Start is called before the first frame update
    void Start()
    {
        ResultDialog.SetActive(false);
    }

    void Update()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        // Update old guesses in UI
        for (int guessNdx = 0; guessNdx < PlayerModel.MaxGuesses; guessNdx++)
        {
            Row row = GuessRows[guessNdx].GetComponent<Row>();
            for (int letterNdx = 0; letterNdx < PlayerModel.WordSize; letterNdx++)
            {
                if (guessNdx < player.GuessedWords.Count)
                {
                    row.chars[letterNdx].text = player.GuessedWords[guessNdx].Substring(letterNdx, 1);
                    row.images[letterNdx].color = GetResultColor(player.GuessResults[guessNdx][letterNdx]);
                }
                else
                {
                    row.chars[letterNdx].text = "";
                    row.images[letterNdx].color = GetResultColor(GuessResult.Empty);
                }
            }
        }

        #region snippet2
        // Update current word in UI (if still guessing)
        if (player.GuessedWords.Count < PlayerModel.MaxGuesses)
        {
            Row row = GuessRows[player.GuessedWords.Count].GetComponent<Row>();
            for (int letterNdx = 0; letterNdx < PlayerModel.WordSize; letterNdx++)
            {
                string letter = (letterNdx < player.CurrentWord.Length) ? player.CurrentWord.Substring(letterNdx, 1) : "";
                row.chars[letterNdx].text = letter;
                row.images[letterNdx].color = GetResultColor(GuessResult.Empty);
            }
        }
        #endregion snippet2
        #region snippet3
        // Show stats (when round is not active)
        if (player.RoundStatus != RoundStatus.Playing)
        {
            PlayerStats playerStats = ResultDialog.GetComponent<PlayerStats>();
            bool isWin = player.RoundStatus == RoundStatus.Win;
            playerStats.ResultLabel.text = isWin ? "Congratulations!" : $"Solution: {player.CurrentSolution}";
            playerStats.PlayedLabel.text = player.RoundsPlayed.ToString();
            playerStats.WinPercentLabel.text = (player.RoundsWon * 100 / player.RoundsPlayed).ToString();
            playerStats.CurrentStreakLabel.text = player.WinStreak.ToString();
            playerStats.MaxStreakLabel.text = player.MaxWinStreak.ToString();

            ResultDialog.SetActive(true);
        }
        else
        {
            ResultDialog.SetActive(false);
        }
        #endregion snippet3
    }

    private Color GetResultColor(GuessResult result)
    {
        switch (result)
        {
            case GuessResult.Empty:     return new Color(0.74f, 0.74f, 0.74f);
            case GuessResult.Correct:   return new Color(0.51f, 0.78f, 0.12f, 1.0f);
            case GuessResult.Partial:   return new Color(0.88f, 0.67f, 0.0f, 1.0f);
            case GuessResult.Wrong:     return new Color(0.46f, 0.46f, 0.46f, 1.0f);
            default:
                throw new ArgumentException(nameof(result));
        }
    }

#region snippet1
    public void OnAddLetter(string message)
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerAddLetter(message[0]));
    }
#endregion snippet1
    public void OnDeleteLetter()
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerDeleteLetter());
    }

    public void OnSubmitGuess()
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerSubmitGuess());
    }

    public void OnAdvanceRound()
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerAdvanceRound());
    }
}
