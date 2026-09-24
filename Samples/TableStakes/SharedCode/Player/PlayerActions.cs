using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// Game-specific <see cref="PlayerUnsynchronizedServerActionCore{TModel}"/>
    /// </summary>
    public abstract class PlayerUnsynchronizedServerAction : PlayerUnsynchronizedServerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Game-specific <see cref="PlayerActionCore{TModel}"/>: an action the player takes. The client executes it
    /// immediately and the server executes it later at the same timeline position, so it may write checksummed
    /// members such as the wallet.
    /// </summary>
    public abstract class PlayerAction : PlayerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Registry for game-specific ActionCodes, used by the individual action classes. Action codes must
    /// be unique within the game.
    /// </summary>
    public static partial class ActionCodes
    {
        // Player timeline actions use 5001-5099. Match timeline actions use 5100-5199 and are declared in
        // SharedCode/Match/MatchActions.cs.
        //
        // 5001 and 5002 are retired. Never reuse a retired code.
        public const int PlayerRecordMatchResult  = 5003;
        public const int PlayerRenamed            = 5004;
        public const int PlayerDailyRewardClaimed = 5007;
        public const int PlayerTargetingFactsSynced = 5017;

        // Codes declared in other files of this partial class:
        //   5005-5006  PlayerObserveScreenViewed, PlayerObservePromotedEntrySelected (Analytics)
        //   5008       PlayerClaimMissionReward (Missions)
        //   5009       PlayerClaimFirstWeekReward (FirstWeek)
        //   5010-5011  PlayerWheelSpinResolved, PlayerAcknowledgeWheelSpin (SpinWheel)
        //   5012       PlayerSetPersonalizedOffersEnabled (Offers)
        //   5013       PlayerClaimWeeklyEventReward (WeeklyEvent)
        //   5014-5016  PlayerBuyCosmetic, PlayerEquipCosmetic, PlayerAcknowledgeCosmetics (Cosmetics)
        // Add every new code to this list. The serializer refuses a duplicate code only when both actions are
        // in the same build, so two features developed in parallel can otherwise pick the same free code.
    }

    /// <summary>
    /// Game-specific <see cref="MetaActionResult"/> values. Descriptive names help diagnose failures in logs
    /// and the LiveOps Dashboard.
    /// </summary>
    public static partial class ActionResults
    {
        public static readonly MetaActionResult MatchAlreadyRecorded = new MetaActionResult(nameof(MatchAlreadyRecorded));
        public static readonly MetaActionResult NoMatchId            = new MetaActionResult(nameof(NoMatchId));
    }

    /// <summary>
    /// Records one finished game on this player's record (<see cref="PlayerModel.TryRecordMatch"/>). It is a server
    /// action because the result comes from the table entity, and it runs on the timeline so the client's copy of
    /// the record is updated too (<c>docs/player.md</c>, "Recording a finished match"). A match already in the
    /// player's history is refused with <see cref="ActionResults.MatchAlreadyRecorded"/>, so a retried delivery is
    /// harmless.
    /// </summary>
    [ModelAction(ActionCodes.PlayerRecordMatchResult)]
    public class PlayerRecordMatchResult : PlayerUnsynchronizedServerAction
    {
        public EntityId MatchId          { get; private set; }
        public MetaTime EndedAt          { get; private set; }
        public int      Position         { get; private set; }
        public int      TricksWon        { get; private set; }
        public int      HumanOpponents   { get; private set; }
        public bool     FinishedByPlayer { get; private set; }

        public PlayerRecordMatchResult() { }

        public PlayerRecordMatchResult(EntityId matchId, MetaTime endedAt, int position, int tricksWon, int humanOpponents, bool finishedByPlayer)
        {
            MatchId          = matchId;
            EndedAt          = endedAt;
            Position         = position;
            TricksWon        = tricksWon;
            HumanOpponents   = humanOpponents;
            FinishedByPlayer = finishedByPlayer;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (!MatchId.IsValid)
                return ActionResults.NoMatchId;
            if (player.HasRecordedMatch(MatchId))
                return ActionResults.MatchAlreadyRecorded;

            if (commit)
            {
                // Position 0 is the winner after tie-breaks. Record the table's ranking rather than deriving a win
                // from trick counts (docs/game-rules.md, "End of the game and standings").
                player.TryRecordMatch(new MatchHistoryEntry(MatchId, EndedAt, TricksWon, Position, Position == 0, HumanOpponents, FinishedByPlayer));
                player.ClientListener.OnRecordChanged();
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Applies a rename the server accepted, with its time.
    /// <para>
    /// It is a server action because the server validates the name and then tells the client the result. The
    /// time is stored because the rename cooldown is measured from it (<c>docs/player.md</c>, "Renaming").
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerRenamed)]
    public class PlayerRenamed : PlayerUnsynchronizedServerAction
    {
        public string   Name { get; private set; }
        public MetaTime RenamedAt   { get; private set; }

        public PlayerRenamed() { }

        public PlayerRenamed(string name, MetaTime at)
        {
            Name      = name;
            RenamedAt = at;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            // PlayerModel.ApplyRename performs every state change and listener call of an accepted rename. The
            // dry-run pass has nothing to check.
            if (commit)
                player.ApplyRename(Name, RenamedAt);

            return MetaActionResult.Success;
        }
    }
}
