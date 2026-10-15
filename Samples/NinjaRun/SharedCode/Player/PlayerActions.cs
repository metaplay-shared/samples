using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Game-specific player action base class, which attaches all game-specific actions to <see cref="PlayerModel"/>.
    /// </summary>
    public abstract class PlayerAction : PlayerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Game-specific <see cref="PlayerSynchronizedServerActionCore{TModel}"/>
    /// </summary>
    public abstract class PlayerSynchronizedServerAction : PlayerSynchronizedServerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Registry for game-specific ActionCodes, used by the individual PlayerAction classes. Action codes must
    /// be unique within the game.
    /// </summary>
    public static class ActionCodes
    {
        public const int PlayerStartPracticeTimeTrial = 5001;
        public const int PlayerAcceptTimeTrialResult  = 5002;
        public const int PlayerRejectTimeTrialResult  = 5003;

        // 5004 is reserved and must not be reused.
        public const int PlayerUnlockSkill            = 5005;
        public const int PlayerEquipSkill             = 5006;
        public const int PlayerOpenTimeTrial          = 5007;
        public const int PlayerSetName            = 5008;
        public const int PlayerStartDraftPartTimeTrial = 5009;
    }

    /// <summary>
    /// Game-specific <see cref="MetaActionResult"/> values. Descriptive names help diagnose failures in logs
    /// and the LiveOps Dashboard.
    /// </summary>
    public static class ActionResults
    {
        public static readonly MetaActionResult NoSuchCourse         = new MetaActionResult(nameof(NoSuchCourse));
        public static readonly MetaActionResult NoActiveTimeTrial    = new MetaActionResult(nameof(NoActiveTimeTrial));
        public static readonly MetaActionResult TimeTrialTicketMismatch = new MetaActionResult(nameof(TimeTrialTicketMismatch));
        public static readonly MetaActionResult NoSuchSkill          = new MetaActionResult(nameof(NoSuchSkill));
        public static readonly MetaActionResult SkillAlreadyUnlocked = new MetaActionResult(nameof(SkillAlreadyUnlocked));
        public static readonly MetaActionResult SkillPrerequisiteMissing = new MetaActionResult(nameof(SkillPrerequisiteMissing));
        public static readonly MetaActionResult NotEnoughOrbs        =new MetaActionResult(nameof(NotEnoughOrbs));
        public static readonly MetaActionResult SkillNotUnlocked     = new MetaActionResult(nameof(SkillNotUnlocked));
        public static readonly MetaActionResult SkillNotEquippable   = new MetaActionResult(nameof(SkillNotEquippable));
        public static readonly MetaActionResult NameTagNotOwned      = new MetaActionResult(nameof(NameTagNotOwned));
        public static readonly MetaActionResult InvalidPlayerName      = new MetaActionResult(nameof(InvalidPlayerName));
        public static readonly MetaActionResult InvalidDraftPart     = new MetaActionResult(nameof(InvalidDraftPart));
    }

    /// <summary>
    /// Buy a Skill with Orbs. A Skill is bought once and kept, and an equippable one is equipped as it is bought.
    /// </summary>
    [ModelAction(ActionCodes.PlayerUnlockSkill)]
    public class PlayerUnlockSkill : PlayerAction
    {
        public SkillId SkillId { get; private set; }

        public PlayerUnlockSkill() { }
        public PlayerUnlockSkill(SkillId skillId) { SkillId = skillId; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            SkillInfo skill = player.GetSkill(SkillId);
            if (skill == null)
                return ActionResults.NoSuchSkill;
            if (player.HasSkill(SkillId))
                return ActionResults.SkillAlreadyUnlocked;
            if (skill.Prerequisite != null && !player.HasSkill(skill.Prerequisite))
                return ActionResults.SkillPrerequisiteMissing;
            if (player.Orbs < skill.OrbCost)
                return ActionResults.NotEnoughOrbs;

            if (commit)
            {
                player.UnlockSkill(skill);
                // Replaces whatever was in the slot, so what was just paid for is what the next run is played
                // with. The Name Tag is not equippable and leaves the slot as it was.
                if (skill.IsEquippable)
                    player.EquipSkill(skill.Id);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Put an owned Skill in the player's one slot, or empty the slot with a null id. The slot is only read
    /// when an attempt opens (the ticket carries what was equipped then) so equipping during a run cannot
    /// change what that run is replayed against.
    /// </summary>
    [ModelAction(ActionCodes.PlayerEquipSkill)]
    public class PlayerEquipSkill : PlayerAction
    {
        /// <summary>The Skill to equip, or null to leave the slot empty.</summary>
        public SkillId SkillId { get; private set; }

        public PlayerEquipSkill() { }
        public PlayerEquipSkill(SkillId skillId) { SkillId = skillId; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (SkillId != null)
            {
                SkillInfo skill = player.GetSkill(SkillId);
                if (skill == null)
                    return ActionResults.NoSuchSkill;
                if (!player.HasSkill(SkillId))
                    return ActionResults.SkillNotUnlocked;
                if (!skill.IsEquippable)
                    return ActionResults.SkillNotEquippable;
            }

            if (commit)
                player.EquipSkill(SkillId);

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Set the player's name, which is also the name their ghost is known by. Requires the Name Tag. The name is
    /// trimmed, then checked with <see cref="PlayerModel.IsValidPlayerName"/>.
    /// </summary>
    [ModelAction(ActionCodes.PlayerSetName)]
    public class PlayerSetName : PlayerAction
    {
        public string Name { get; private set; }

        public PlayerSetName() { }
        public PlayerSetName(string name) { Name = name; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (!player.HasSkill(TimeTrialSkills.NameTag))
                return ActionResults.NameTagNotOwned;

            string name = Name?.Trim();
            if (!PlayerModel.IsValidPlayerName(name))
                return ActionResults.InvalidPlayerName;

            if (commit)
                player.SetPlayerName(name);

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Open an attempt on the map the server picked, with the field it selected for it, so the client gets the
    /// ticket and the opponents together. With <see cref="IsPractice"/> set it opens a developer's race on a
    /// named course (<see cref="TimeTrialStartOnMapRequest"/>), which counts for nothing. Synchronized, because
    /// the open ticket is checksummed state.
    /// </summary>
    [ModelAction(ActionCodes.PlayerOpenTimeTrial)]
    public class PlayerOpenTimeTrial : PlayerSynchronizedServerAction
    {
        /// <summary>The map to run: drawn from the registry's active set, or named by a developer for practice.</summary>
        public CourseId CourseId { get; private set; }

        /// <summary>The opponents to run against, fastest expected finish first. May be empty or null.</summary>
        public List<TimeTrialGhost> Lineup { get; private set; }

        /// <summary>Whether the ticket opened is a practice ticket, which is the case for a developer's race.</summary>
        public bool IsPractice { get; private set; }

        public PlayerOpenTimeTrial() { }

        public PlayerOpenTimeTrial(CourseId courseId, List<TimeTrialGhost> lineup, bool isPractice = false)
        {
            CourseId   = courseId;
            Lineup     = lineup;
            IsPractice = isPractice;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            CourseDefinition course = CourseResolver.TryResolve(CourseId, player.GameConfig);
            if (course == null)
                return ActionResults.NoSuchCourse;

            if (commit)
            {
                List<TimeTrialGhost> lineup = Lineup ?? new List<TimeTrialGhost>();
                TimeTrialTicket      ticket = player.IssueTimeTrialTicket(CourseId, IsPractice);
                player.SetActiveLineup(ticket.TicketId, lineup);
                player.ClientListener.OnTimeTrialStarted(ticket, lineup);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Open a practice attempt on a named map, with no opponents: what offline mode plays, which has no
    /// registry to ask, and what the developer course tools send to practise on a chosen map online.
    /// Development-only, and accepting the run records nothing.
    /// </summary>
    [ModelAction(ActionCodes.PlayerStartPracticeTimeTrial)]
    [DevelopmentOnlyAction]
    public class PlayerStartPracticeTimeTrial : PlayerAction
    {
        /// <summary>The map to practise on.</summary>
        public CourseId CourseId { get; private set; }

        public PlayerStartPracticeTimeTrial() { }
        public PlayerStartPracticeTimeTrial(CourseId courseId) { CourseId = courseId; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            CourseDefinition course = CourseResolver.TryResolve(CourseId, player.GameConfig);
            if (course == null)
                return ActionResults.NoSuchCourse;

            if (commit)
            {
                // The client names its own map here, so the run is practice and counts for nothing.
                TimeTrialTicket ticket = player.IssueTimeTrialTicket(CourseId, isPractice: true);
                player.ClientListener.OnTimeTrialStarted(ticket, null);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Open a practice attempt on a part editor draft, composed alone, with no opponents. The draft is in no
    /// published config, so it travels by value on the ticket (<see cref="TimeTrialTicket.DraftPart"/>), and
    /// the run screen and the server's recomputation both compose the course from there. Development-only,
    /// and accepting the run records nothing.
    /// </summary>
    [ModelAction(ActionCodes.PlayerStartDraftPartTimeTrial)]
    [DevelopmentOnlyAction]
    public class PlayerStartDraftPartTimeTrial : PlayerAction
    {
        /// <summary>The draft to play, composed alone.</summary>
        public DraftCoursePart Part { get; private set; }

        public PlayerStartDraftPartTimeTrial() { }
        public PlayerStartDraftPartTimeTrial(DraftCoursePart part) { Part = part; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            CourseDefinition course = Part?.TryComposeAlone(player.GameConfig.Global);
            if (course == null)
                return ActionResults.InvalidDraftPart;

            if (commit)
            {
                // The client names its own course here, so the run is practice and counts for nothing.
                TimeTrialTicket ticket = player.IssueTimeTrialTicket(course.Id, isPractice: true, draftPart: Part);
                player.ClientListener.OnTimeTrialStarted(ticket, null);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Record the result the server computed for an attempt. Server-only: the times are the server's own run of
    /// the recording, never numbers the client reported, and placements are worked out from them against the
    /// lineup the model already holds. Synchronized, because it writes checksummed state.
    /// </summary>
    [ModelAction(ActionCodes.PlayerAcceptTimeTrialResult)]
    public class PlayerAcceptTimeTrialResult : PlayerSynchronizedServerAction
    {
        public int             TicketId { get; private set; }
        public TimeTrialResult Result   { get; private set; }

        /// <summary>
        /// Where the run stands on its course, from the course's own pool of results. Null when the run was not
        /// recorded into the pool or the pool could not be reached. The result is recorded either way.
        /// </summary>
        public TimeTrialStanding Standing { get; private set; }

        public PlayerAcceptTimeTrialResult() { }

        public PlayerAcceptTimeTrialResult(int ticketId, TimeTrialResult result, TimeTrialStanding standing)
        {
            TicketId = ticketId;
            Result   = result;
            Standing = standing;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            TimeTrialTicket ticket = player.ActiveTimeTrial;
            if (ticket == null)
                return ActionResults.NoActiveTimeTrial;
            if (ticket.TicketId != TicketId)
                return ActionResults.TimeTrialTicketMismatch;

            CourseId courseId = ticket.CourseId;

            // The open ticket says whether the attempt is practice. A practice run only closes the attempt and
            // reports the time.
            if (commit && ticket.IsPractice)
            {
                player.ClosePracticeTimeTrial(ticket.TicketId);
                player.ClientListener.OnTimeTrialAccepted(courseId, Result, false, null);
            }
            else if (commit)
            {
                bool isNewBest = player.CommitTimeTrialResult(courseId, Result, Standing);
                player.ClientListener.OnTimeTrialAccepted(courseId, Result, isNewBest, Standing);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Close an attempt the server would not count, with the reason the client shows the player. Nothing is
    /// awarded and no time is recorded. Synchronized for the same reason as
    /// <see cref="PlayerAcceptTimeTrialResult"/>: it clears the open ticket, which is checksummed state.
    /// </summary>
    [ModelAction(ActionCodes.PlayerRejectTimeTrialResult)]
    public class PlayerRejectTimeTrialResult : PlayerSynchronizedServerAction
    {
        public int                   TicketId { get; private set; }
        public TimeTrialRejectReason Reason   { get; private set; }

        public PlayerRejectTimeTrialResult() { }

        public PlayerRejectTimeTrialResult(int ticketId, TimeTrialRejectReason reason)
        {
            TicketId = ticketId;
            Reason   = reason;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.RejectTimeTrialResult(TicketId);
                player.ClientListener.OnTimeTrialRejected(TicketId, Reason);
            }

            return MetaActionResult.Success;
        }
    }
}
