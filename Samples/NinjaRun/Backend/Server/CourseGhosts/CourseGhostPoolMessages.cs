using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Server.CourseGhosts
{
    /// <summary>
    /// Ask a course's pool for the opponents to put in front of a player who is opening a run. The player's
    /// own best time on the course is what the selection is paced against; it is sent along rather than looked
    /// up, because the player actor already holds it and the pool keeps no player state of its own.
    /// </summary>
    [MetaMessage(ServerMessageCodes.CourseGhostLineupRequest, MessageDirection.ServerInternal)]
    public class CourseGhostLineupRequest : EntityAskRequest<CourseGhostLineupResponse>
    {
        public CourseId CourseId { get; private set; }
        public EntityId PlayerId { get; private set; }

        /// <summary>The player's best accepted time on this course, or 0 if they have none.</summary>
        public int PlayerBestMs { get; private set; }

        public int MaxGhosts { get; private set; }

        public CourseGhostLineupRequest() { }

        public CourseGhostLineupRequest(CourseId courseId, EntityId playerId, int playerBestMs, int maxGhosts)
        {
            CourseId     = courseId;
            PlayerId     = playerId;
            PlayerBestMs = playerBestMs;
            MaxGhosts    = maxGhosts;
        }
    }

    /// <summary>The opponents the pool selected. Empty when the course has no pool it could seed.</summary>
    [MetaMessage(ServerMessageCodes.CourseGhostLineupResponse, MessageDirection.ServerInternal)]
    public class CourseGhostLineupResponse : EntityAskResponse
    {
        public List<TimeTrialGhost> Lineup { get; private set; }

        public CourseGhostLineupResponse() { }
        public CourseGhostLineupResponse(List<TimeTrialGhost> lineup) { Lineup = lineup; }
    }

    /// <summary>
    /// Hand a validated run to its course's pool: count it into the course's distribution, and keep it as an
    /// opponent if it is the player's best. The recording has already been replayed by the player actor, so
    /// the pool takes it as validated rather than checking it again.
    /// </summary>
    [MetaMessage(ServerMessageCodes.CourseGhostRunAccepted, MessageDirection.ServerInternal)]
    public class CourseGhostRunAccepted : EntityAskRequest<CourseGhostRunAcceptedResult>
    {
        public CourseId CourseId     { get; private set; }
        public EntityId PlayerId     { get; private set; }
        public string   DisplayName  { get; private set; }
        public int      FinishTimeMs { get; private set; }
        public bool     IsNewBest    { get; private set; }

        /// <summary>The player's best before this run, or 0 if this is their first accepted run on the course.</summary>
        public int PreviousBestMs { get; private set; }

        public TimeTrialRecording Recording { get; private set; }

        /// <summary>The run's crossing time at each of the course's gates, from the replay that accepted it.</summary>
        public List<int> GateTimesMs { get; private set; }

        /// <summary>The run's crossing time at each climb's bottom torii, from the same replay. A segment ends there.</summary>
        public List<int> ClimbEntryTimesMs { get; private set; }

        /// <summary>
        /// The Skill on the run's ticket, or null for none. The pool keeps it with the recording, because the
        /// recording only replays as the run it was under the Skill it was run with.
        /// </summary>
        public SkillId SkillId { get; private set; }

        public CourseGhostRunAccepted() { }

        public CourseGhostRunAccepted(CourseId courseId, EntityId playerId, string displayName, int finishTimeMs, bool isNewBest, int previousBestMs, TimeTrialRecording recording, List<int> gateTimesMs, List<int> climbEntryTimesMs, SkillId skillId)
        {
            CourseId          = courseId;
            PlayerId          = playerId;
            DisplayName       = displayName;
            FinishTimeMs      = finishTimeMs;
            IsNewBest         = isNewBest;
            PreviousBestMs    = previousBestMs;
            Recording         = recording;
            GateTimesMs       = gateTimesMs;
            ClimbEntryTimesMs = climbEntryTimesMs;
            SkillId           = skillId;
        }
    }

    /// <summary>Where the run stands on its course, for the player to be shown.</summary>
    [MetaMessage(ServerMessageCodes.CourseGhostRunAcceptedResult, MessageDirection.ServerInternal)]
    public class CourseGhostRunAcceptedResult : EntityAskResponse
    {
        public TimeTrialStanding Standing { get; private set; }

        public CourseGhostRunAcceptedResult() { }
        public CourseGhostRunAcceptedResult(TimeTrialStanding standing) { Standing = standing; }
    }
}
