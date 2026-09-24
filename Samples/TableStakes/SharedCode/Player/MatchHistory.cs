using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// The player's lifetime counters shown on the menu.
    /// <para>
    /// They count every completed game for every human who was dealt in. A table whose players all leave is
    /// played out to a result by bots, so leaving a game still records it (<c>docs/player.md</c>, "Lifetime
    /// record and match history").
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PlayerRecord
    {
        [MetaMember(1)] public int GamesPlayed { get; private set; }
        [MetaMember(2)] public int GamesWon    { get; private set; }
        [MetaMember(3)] public int TricksWon   { get; private set; }

        public PlayerRecord() { }

        public PlayerRecord(int gamesPlayed, int gamesWon, int tricksWon)
        {
            GamesPlayed = gamesPlayed;
            GamesWon    = gamesWon;
            TricksWon   = tricksWon;
        }

        /// <summary>Adds one finished game to the counters.</summary>
        public void Add(MatchHistoryEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            GamesPlayed += 1;
            GamesWon    += entry.IsWin ? 1 : 0;
            TricksWon   += entry.TricksWon;
        }

        public override string ToString() => $"{GamesWon}/{GamesPlayed} games, {TricksWon} tricks";
    }

    /// <summary>
    /// One finished game on a player's record.
    /// <para>
    /// <see cref="HumanOpponents"/> and <see cref="FinishedByPlayer"/> do not affect the counters. They are
    /// recorded so that a future leaderboard can decide whether to count games against bots, or games a bot
    /// finished after the player left (<c>docs/player.md</c>, "Lifetime record and match history").
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MatchHistoryEntry
    {
        /// <summary>
        /// The table this entry is for. A result can be delivered more than once, and a repeat is recognized by
        /// this id (<see cref="MatchHistoryRules.HasRecorded"/>).
        /// </summary>
        [MetaMember(1)] public EntityId MatchId { get; private set; }

        [MetaMember(2)] public MetaTime EndedAt { get; private set; }

        [MetaMember(3)] public int TricksWon { get; private set; }

        /// <summary>
        /// The player's zero-based position in the final standings, 0 for the winner, as ranked by the table
        /// after tie-breaks (<c>docs/game-rules.md</c>, "End of the game and standings").
        /// </summary>
        [MetaMember(4)] public int Position { get; private set; }

        [MetaMember(5)] public bool IsWin { get; private set; }

        /// <summary>
        /// The number of other seats held by a human who joined the table. A seat assigned to a player who never
        /// joined is played by a bot for the whole game and is not counted.
        /// </summary>
        [MetaMember(6)] public int HumanOpponents { get; private set; }

        /// <summary>Whether this player was still at the table when the last trick resolved.</summary>
        [MetaMember(7)] public bool FinishedByPlayer { get; private set; }

        public MatchHistoryEntry() { }

        public MatchHistoryEntry(EntityId matchId, MetaTime endedAt, int tricksWon, int position, bool isWin, int humanOpponents, bool finishedByPlayer)
        {
            MatchId          = matchId;
            EndedAt          = endedAt;
            TricksWon        = tricksWon;
            Position         = position;
            IsWin            = isWin;
            HumanOpponents   = humanOpponents;
            FinishedByPlayer = finishedByPlayer;
        }

        public override string ToString() => $"{MatchId}: #{Position + 1}, {TricksWon} tricks, {HumanOpponents} human opponents";
    }

    /// <summary>
    /// The match history and recording rules, as pure functions. They are outside <see cref="PlayerModel"/> so
    /// they can be tested without a model or a server.
    /// </summary>
    public static class MatchHistoryRules
    {
        /// <summary>
        /// The number of finished games the history keeps. <see cref="Append"/> removes the oldest entries.
        /// <para>
        /// A repeated delivery is detected only while its game is still in the history. See
        /// <see cref="HasRecorded"/>.
        /// </para>
        /// </summary>
        public const int HistoryCapacity = 20;

        /// <summary>
        /// The number of declined tables a player remembers (<see cref="NoteDeclined"/>). A table is declined only
        /// when a seat reservation timed out and the player was seated elsewhere, so a player rarely has more
        /// than one.
        /// </summary>
        public const int DeclinedCapacity = 8;

        /// <summary>
        /// Whether <paramref name="matchId"/> is in <paramref name="history"/>. This check is what records each
        /// match only once: a table re-sends its result until the player actor acknowledges it, so the same result
        /// can arrive again after a lost reply or a restart. The history is bounded, so a repeat that arrives after
        /// <see cref="HistoryCapacity"/> further games is recorded again. A player whose
        /// <see cref="PlayerModel.CurrentMatchId"/> is set cannot enter matchmaking, which normally prevents that.
        /// The paths that clear it without recording are in <c>docs/player.md</c>, "Idempotence and its limit".
        /// </summary>
        public static bool HasRecorded(IReadOnlyList<MatchHistoryEntry> history, EntityId matchId)
        {
            if (history == null || !matchId.IsValid)
                return false;

            foreach (MatchHistoryEntry entry in history)
            {
                if (entry.MatchId == matchId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a delivered result should be added to the record: the table reached <see cref="MatchPhase.Ended"/>,
        /// <paramref name="result"/> is not null, the player did not decline the table (<see cref="NoteDeclined"/>),
        /// and the match is not already recorded (<see cref="HasRecorded"/>). A table that never started has no game
        /// to count, but its result is still delivered so the player's <see cref="PlayerModel.CurrentMatchId"/> is
        /// cleared (<c>docs/match.md</c>, "Phases").
        /// </summary>
        public static bool ShouldRecord(MatchPhase phase, MatchSeatResult result, IReadOnlyList<MatchHistoryEntry> history, IReadOnlyList<EntityId> declinedMatchIds, EntityId matchId)
        {
            if (phase != MatchPhase.Ended || result == null)
                return false;
            if (HasDeclined(declinedMatchIds, matchId))
                return false;

            return !HasRecorded(history, matchId);
        }

        /// <summary>Whether <paramref name="matchId"/> is a table this player declined (<see cref="NoteDeclined"/>).</summary>
        public static bool HasDeclined(IReadOnlyList<EntityId> declinedMatchIds, EntityId matchId) =>
            declinedMatchIds != null && declinedMatchIds.Contains(matchId);

        /// <summary>
        /// Adds a table whose seat this player never took, so its result is not recorded for them.
        /// <para>
        /// The list keeps at most <see cref="DeclinedCapacity"/> entries and drops the oldest first. An entry is
        /// kept after the table's result arrives, because the table re-sends the result if the reply is lost,
        /// and a repeat with no entry would be recorded.
        /// </para>
        /// </summary>
        public static void NoteDeclined(List<EntityId> declinedMatchIds, EntityId matchId)
        {
            if (declinedMatchIds == null)
                throw new ArgumentNullException(nameof(declinedMatchIds));
            if (HasDeclined(declinedMatchIds, matchId))
                return;

            declinedMatchIds.Add(matchId);
            while (declinedMatchIds.Count > DeclinedCapacity)
                declinedMatchIds.RemoveAt(0);
        }

        /// <summary>
        /// Appends <paramref name="entry"/> and removes the oldest entries beyond <see cref="HistoryCapacity"/>.
        /// The newest entry is last, and the menu reverses the list to show the latest first.
        /// </summary>
        public static void Append(List<MatchHistoryEntry> history, MatchHistoryEntry entry)
        {
            if (history == null)
                throw new ArgumentNullException(nameof(history));
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            history.Add(entry);

            // A loop rather than a single removal, because a stored history can be longer than a lowered
            // HistoryCapacity.
            while (history.Count > HistoryCapacity)
                history.RemoveAt(0);
        }
    }
}
