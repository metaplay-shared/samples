using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// Action codes for the seasonal tournament's player actions.
    /// </summary>
    public static partial class ActionCodes
    {
        // 5200-5299: the seasonal tournament, on the player timeline.
        public const int PlayerTournamentJoined          = 5200;
        public const int PlayerTournamentMilestoneClaim  = 5201;
        public const int PlayerTournamentPlacementClaim  = 5202;
        public const int PlayerTournamentSeasonConcluded = 5203;
    }

    /// <summary>The seasonal tournament's <see cref="MetaActionResult"/> values.</summary>
    public static partial class ActionResults
    {
        public static readonly MetaActionResult NotInTournament                = new MetaActionResult(nameof(NotInTournament));
        public static readonly MetaActionResult TournamentAlreadyJoined        = new MetaActionResult(nameof(TournamentAlreadyJoined));
        public static readonly MetaActionResult NoSuchTournamentMilestone      = new MetaActionResult(nameof(NoSuchTournamentMilestone));
        public static readonly MetaActionResult TournamentMilestoneNotReached  = new MetaActionResult(nameof(TournamentMilestoneNotReached));
        public static readonly MetaActionResult TournamentRewardAlreadyClaimed = new MetaActionResult(nameof(TournamentRewardAlreadyClaimed));
        public static readonly MetaActionResult NoSuchTournamentResult         = new MetaActionResult(nameof(NoSuchTournamentResult));
        public static readonly MetaActionResult NoTournamentReward             = new MetaActionResult(nameof(NoTournamentReward));
        public static readonly MetaActionResult TournamentResultAlreadyReported = new MetaActionResult(nameof(TournamentResultAlreadyReported));
    }

    /// <summary>
    /// Game-specific <see cref="PlayerSynchronizedServerActionCore{TModel}"/>. Tournament claims use it because
    /// they change the checksummed wallet, which an unsynchronized action may not do. A client action would not
    /// work either: its check reads progress that the match-completion observer writes, which the client may not
    /// have received yet. The server must send these actions with <c>EnqueueServerAction</c>, so they run at the
    /// same model position on both sides and create the same correlation id. <c>ExecuteServerActionImmediately</c>
    /// sends them on the unsynchronized path, where a wallet change is an illegal modification that turns off the
    /// client's journal checker for the rest of the session (<c>docs/seasonal-tournament.md</c>).
    /// </summary>
    public abstract class PlayerSynchronizedServerAction : PlayerSynchronizedServerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// The player joined a tournament season. The action carries the division and season times that the league
    /// manager decided, because the client's copy of the league state arrives separately and may be late. It
    /// refuses to rejoin the season the player is in, so a duplicate action never resets a season in progress.
    /// </summary>
    [ModelAction(ActionCodes.PlayerTournamentJoined)]
    public class PlayerTournamentJoined : PlayerSynchronizedServerAction
    {
        public int                     Season        { get; private set; }
        public EntityId                DivisionId    { get; private set; }
        public int                     Group         { get; private set; }
        public MetaTime                SeasonStartsAt { get; private set; }
        public MetaTime                SeasonEndsAt  { get; private set; }
        public TournamentRewardTableId RewardTable   { get; private set; }

        public PlayerTournamentJoined() { }

        public PlayerTournamentJoined(int season, EntityId divisionId, int group, MetaTime seasonStartsAt, MetaTime seasonEndsAt, TournamentRewardTableId rewardTable)
        {
            Season         = season;
            DivisionId     = divisionId;
            Group          = group;
            SeasonStartsAt = seasonStartsAt;
            SeasonEndsAt   = seasonEndsAt;
            RewardTable    = rewardTable;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (!DivisionId.IsValid)
                return ActionResults.NotInTournament;

            PlayerTournamentState tournament = player.Tournament;
            if (tournament.HasJoined && tournament.Season == Season)
                return ActionResults.TournamentAlreadyJoined;

            if (commit)
            {
                player.JoinTournament(Season, DivisionId, SeasonEndsAt, RewardTable);

                player.EventStream.Event(new PlayerEventTournamentJoined(
                    Season, Group, ElapsedTenths(player.CurrentTime), RewardTable));

                player.ClientListener.OnTournamentChanged();
            }

            return MetaActionResult.Success;
        }

        /// <summary>How far into the season the player joined, in tenths of the season from 0 to 10.</summary>
        int ElapsedTenths(MetaTime now)
        {
            long seasonLengthMs = (SeasonEndsAt - SeasonStartsAt).Milliseconds;
            if (seasonLengthMs <= 0)
                return 0;

            long elapsedMs = (now - SeasonStartsAt).Milliseconds;
            if (elapsedMs <= 0)
                return 0;
            if (elapsedMs >= seasonLengthMs)
                return 10;

            return (int)(elapsedMs * 10 / seasonLengthMs);
        }
    }

    /// <summary>
    /// Pay one participation milestone.
    /// <para>
    /// The wallet settlement and the tournament event both happen on the commit pass and share one correlation
    /// id. The wallet is checked on both passes, so a wallet refusal becomes the action's result
    /// (<c>docs/economy.md</c>).
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerTournamentMilestoneClaim)]
    public class PlayerTournamentMilestoneClaim : PlayerSynchronizedServerAction
    {
        public int MilestoneIndex { get; private set; }

        public PlayerTournamentMilestoneClaim() { }

        public PlayerTournamentMilestoneClaim(int milestone)
        {
            MilestoneIndex = milestone;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit) =>
            player.ClaimTournamentMilestone(MilestoneIndex, commit);
    }

    /// <summary>Pay the placement reward of one concluded division.</summary>
    [ModelAction(ActionCodes.PlayerTournamentPlacementClaim)]
    public class PlayerTournamentPlacementClaim : PlayerSynchronizedServerAction
    {
        public EntityId DivisionId { get; private set; }

        public PlayerTournamentPlacementClaim() { }

        public PlayerTournamentPlacementClaim(EntityId divisionId)
        {
            DivisionId = divisionId;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit) =>
            player.ClaimTournamentPlacement(DivisionId, commit);
    }

    /// <summary>
    /// Record that the player's season has concluded, and write the event that records their placement.
    /// <para>
    /// It is separate from the placement claim because every placement has a result but not every placement has
    /// a reward, and a player may never claim a reward. The player's list of reported results prevents a second
    /// event when a reconnect runs this again.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerTournamentSeasonConcluded)]
    public class PlayerTournamentSeasonConcluded : PlayerSynchronizedServerAction
    {
        public EntityId DivisionId { get; private set; }

        public PlayerTournamentSeasonConcluded() { }

        public PlayerTournamentSeasonConcluded(EntityId divisionId)
        {
            DivisionId = divisionId;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit) =>
            player.RecordTournamentResult(DivisionId, commit);
    }
}
