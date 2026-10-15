using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

namespace Game.Logic
{
    /// <summary>
    /// State and per-player logic for a single player. This is the heart of a Metaplay game: it is
    /// kept in sync between client and server, persisted in the database, and evolved over time via
    /// <see cref="PlayerAction"/>s and per-tick logic in <see cref="GameTick"/>.
    /// <para>
    /// What lives here are the durable facts about a player's time trials: the attempt currently open and the
    /// field it is racing, the best accepted time on each course, how often each course has been completed,
    /// and what the last run's segment placements paid into the Orb balance. The 60 Hz state inside a run does
    /// not. That belongs to <see cref="TimeTrialSimulation"/> and is gone when the run is.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(1)]
    [SupportedSchemaVersions(1, 1)]
    [MetaBlockedMembers(124, 125, 126)]
    public class PlayerModel : PlayerModelBase<PlayerModel, PlayerStatisticsCore>
    {
        public const int TicksPerSecond = 10;
        protected override int GetTicksPerSecond() => TicksPerSecond;

        // External services, not serialized or PrettyPrinted.
        [IgnoreDataMember] public new SharedGameConfig       GameConfig => GetGameConfig<SharedGameConfig>();
        [IgnoreDataMember] public IPlayerModelServerListener ServerListener { get; set; } = EmptyPlayerModelServerListener.Instance;
        [IgnoreDataMember] public IPlayerModelClientListener ClientListener { get; set; } = EmptyPlayerModelClientListener.Instance;

        // Player profile.
        [MetaMember(100)] public sealed override EntityId           PlayerId    { get; set; }
        /// <summary>
        /// The player's one name: the account name the dashboard shows and the name their ghost is known by.
        /// Every player starts as <see cref="TimeTrialSkills.MaskedNinjaName"/>, which no name the player can
        /// set is equal to, and renames themselves with <see cref="PlayerSetName"/> once they own the Name Tag.
        /// </summary>
        [MetaMember(101), NoChecksum] public sealed override string PlayerName  { get; set; }
        [MetaMember(102)] public sealed override int                PlayerLevel { get; set; }

        // Member ids start at 120 rather than reusing 110-112, which a player stored before the time trial
        // existed still carries with entirely different types behind them. Leaving those ids unclaimed lets an
        // old blob's members be skipped as unknown instead of being decoded into these.

        /// <summary>
        /// The attempt the player has open, or null between runs. Starting a run replaces whatever was here,
        /// so an abandoned attempt costs nothing; submitting one consumes it.
        /// </summary>
        [MetaMember(120)] public TimeTrialTicket ActiveTimeTrial { get; private set; }

        /// <summary>Counts up so every ticket this player is issued has its own id.</summary>
        [MetaMember(121)] public int NextTimeTrialTicketId { get; private set; }

        /// <summary>The fastest accepted time on each course. Equal times are a tie and the earlier one stands.</summary>
        [MetaMember(122)] public MetaDictionary<CourseId, TimeTrialResult> BestResults { get; private set; } = new MetaDictionary<CourseId, TimeTrialResult>();

        /// <summary>How many accepted runs the player has on each course. Nothing is paid for a completion.</summary>
        [MetaMember(123)] public MetaDictionary<CourseId, int> CompletedRunCounts { get; private set; } = new MetaDictionary<CourseId, int>();

        /// <summary>
        /// Whether the player has ever had a run accepted, on any course. Until then the next run is the
        /// tutorial. A practice run is never counted, so it never flips this.
        /// </summary>
        public bool HasCompletedARun
        {
            get
            {
                foreach ((CourseId _, int count) in CompletedRunCounts)
                {
                    if (count > 0)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// The field the server put in front of the attempt currently open, or null when there is none: a
        /// practice run opened alone. It is what the attempt's segments are placed against, and it is dropped as
        /// soon as the attempt closes.
        /// </summary>
        [MetaMember(127)] public TimeTrialLineup ActiveLineup { get; private set; }

        /// <summary>The Orb balance: what segment placements pay and what Skills are bought with.</summary>
        [MetaMember(128)] public int Orbs { get; private set; }

        /// <summary>
        /// Where the last accepted run came in on each of its course's segments, 1 to 4, in course order. Zero
        /// in every place when the run had no lineup to be placed against.
        /// </summary>
        [MetaMember(129)] public List<int> LastSegmentPlacements { get; private set; } = new List<int>();

        /// <summary>Orbs the last accepted run paid, totalled over its segments.</summary>
        [MetaMember(130)] public int LastOrbsEarned { get; private set; }

        /// <summary>
        /// The Skills the player has bought, in the order they bought them. Owning one is permanent: Orbs are
        /// spent once and the node stays open behind whatever is equipped.
        /// </summary>
        [MetaMember(131)] public OrderedSet<SkillId> UnlockedSkills { get; private set; } = new OrderedSet<SkillId>();

        /// <summary>
        /// The Skill in the player's one slot, or null when the slot is empty. What actually reaches a run is
        /// <see cref="ActiveSkillId"/>, which also checks the Skill is still owned and still in the config.
        /// </summary>
        [MetaMember(132)] public SkillId EquippedSkillId { get; private set; }

        /// <summary>
        /// The map or course the last attempt was opened on, or null before the player has started one. It is
        /// what the menu shows a personal best for, and what the server leaves out when it draws the next map,
        /// so pressing Start twice is two different maps.
        /// </summary>
        [MetaMember(134)] public CourseId LastTimeTrialCourseId { get; private set; }

        /// <summary>
        /// The player's best time recorded into each course's pool, which is what the server tells the pool
        /// the player's previous best was. It differs from <see cref="BestResults"/> because runs made without
        /// a name are never recorded into a pool: a player who names themselves after setting a best
        /// enters each board with their next run there, not only once they beat that nameless best.
        /// </summary>
        [MetaMember(135)] public MetaDictionary<CourseId, int> PoolBestTimesMs { get; private set; } = new MetaDictionary<CourseId, int>();

        /// <summary>
        /// Whether the server this session is on runs with its development features enabled, which is one of the
        /// two things that let a player run developer-only actions, the other being <see cref="IPlayerModelBase.IsDeveloper"/>.
        /// The player actor sets it at every session start, so it describes the server, never the player, and
        /// is neither persisted nor checksummed. The client reads it only to decide whether to show developer
        /// tools; the server's own refusal of a developer-only request is what gates them.
        /// </summary>
        [MetaMember(136), Transient, NoChecksum] public bool ServerDevelopmentFeaturesEnabled { get; set; }

        /// <summary>
        /// Whether this session may run developer-only actions: the player is marked a developer, or the server
        /// runs with its development features on. Offline mode's in-process server always allows them, which the
        /// client knows from its own environment rather than from this.
        /// </summary>
        public bool AllowsDeveloperActions => IsDeveloper || ServerDevelopmentFeaturesEnabled;

        protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            PlayerId   = playerId;
            PlayerName = TimeTrialSkills.MaskedNinjaName;
        }

        protected override void GameTick(IChecksumContext checksumCtx)
        {
            // Nothing advances on its own: a time trial is played in its own simulation and its result arrives
            // through an action.
        }

        protected override void GameFastForwardTime(MetaDuration elapsedTime)
        {
            // Nothing accrues while the player is away, so there is nothing to catch up on.
        }

        /// <summary>The player's best accepted time on <paramref name="courseId"/>, or null if they have none.</summary>
        public TimeTrialResult GetBestResult(CourseId courseId)
            => courseId != null ? BestResults.GetValueOrDefault(courseId) : null;

        /// <summary>
        /// Open a new attempt on <paramref name="courseId"/> with the <see cref="ActiveSkillId"/>, replacing any
        /// attempt still open. The map becomes <see cref="LastTimeTrialCourseId"/> whether or not the run is
        /// ever finished.
        /// </summary>
        public TimeTrialTicket IssueTimeTrialTicket(CourseId courseId, bool isPractice = false, DraftCoursePart draftPart = null)
        {
            NextTimeTrialTicketId++;
            ActiveTimeTrial       = new TimeTrialTicket(NextTimeTrialTicketId, courseId, TimeTrialConstants.RulesetVersion, CurrentTime, ActiveSkillId, isPractice, draftPart);
            ActiveLineup          = null;
            LastTimeTrialCourseId = courseId;
            return ActiveTimeTrial;
        }

        /// <summary>The Skill <paramref name="skillId"/> names, or null for no Skill and for an unknown id.</summary>
        public SkillInfo GetSkill(SkillId skillId) => TimeTrialSkills.TryGetSkill(GameConfig, skillId);

        /// <summary>Whether the player has bought <paramref name="skillId"/>.</summary>
        public bool HasSkill(SkillId skillId) => skillId != null && UnlockedSkills.Contains(skillId);

        /// <summary>
        /// The Skill a run opened now would be played with: the equipped one, as long as it is still owned and
        /// still in the published config. A Skill retired from the sheet empties the slot for the next run
        /// rather than issuing a ticket against a definition nothing can resolve.
        /// </summary>
        public SkillId ActiveSkillId
        {
            get
            {
                SkillInfo skill = GetSkill(EquippedSkillId);
                return skill != null && skill.IsEquippable && HasSkill(EquippedSkillId) ? EquippedSkillId : null;
            }
        }

        /// <summary>
        /// The longest name <see cref="IsValidPlayerName"/> accepts, counted as <see cref="PlayerNameLength"/>
        /// counts: one per letter, digit or mark, and one per emoji however many code points it is made of.
        /// </summary>
        public const int MaxPlayerNameLength = 18;

        /// <summary>
        /// Whether the player has a name: they own the Name Tag and their <see cref="PlayerName"/> is no longer
        /// the <see cref="TimeTrialSkills.MaskedNinjaName"/> every player starts as. A player without one is a
        /// Masked Ninja, and the server records none of their runs into a course's ghost pool, so they are
        /// nobody's opponent and have no standing on the board.
        /// </summary>
        public bool HasName => HasSkill(TimeTrialSkills.NameTag)
            && !string.IsNullOrEmpty(PlayerName)
            && !string.Equals(PlayerName, TimeTrialSkills.MaskedNinjaName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// What this player's ghost is called wherever it is named: their <see cref="PlayerName"/> when
        /// <see cref="HasName"/>, and the anonymous house name otherwise.
        /// </summary>
        public string GhostDisplayName => HasName ? PlayerName : TimeTrialSkills.MaskedNinjaName;

        /// <summary>
        /// The step of the Name Tag tutorial the player is on. It is read off the model rather than stored, so
        /// reopening the game lands the player back on the same step.
        /// </summary>
        public NameTagTutorialStep NameTagTutorialStep
        {
            get
            {
                if (HasSkill(TimeTrialSkills.NameTag))
                    return HasName ? NameTagTutorialStep.None : NameTagTutorialStep.SetName;

                // A player who cannot yet afford the tag is left alone.
                SkillInfo nameTag = GetSkill(TimeTrialSkills.NameTag);
                return nameTag != null && Orbs >= nameTag.OrbCost ? NameTagTutorialStep.BuyNameTag : NameTagTutorialStep.None;
            }
        }

        /// <summary>
        /// Whether <paramref name="name"/> may be a player name as it stands. <see cref="PlayerSetName"/> trims
        /// its input and then applies this same check, so the client can validate trimmed input before sending it.
        /// </summary>
        public static bool IsValidPlayerName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name[0] == ' ' || name[name.Length - 1] == ' ')
                return false;

            bool               glueAllowed = false;
            ReadOnlySpan<char> rest        = name.AsSpan();
            while (!rest.IsEmpty)
            {
                if (Rune.DecodeFromUtf16(rest, out Rune rune, out int consumed) != OperationStatus.Done)
                    return false;
                rest = rest.Slice(consumed);

                // Emoji glue is allowed only directly after an emoji, a digit or more glue, so a name cannot be made of
                // invisible characters alone.
                if (IsEmojiGlue(rune))
                {
                    if (!glueAllowed)
                        return false;
                    continue;
                }

                // Every other format and control character is refused, which keeps bidirectional overrides and
                // zero-width spaces out of names.
                bool emoji = IsEmoji(rune);
                bool plain = Rune.IsLetterOrDigit(rune) || rune.Value == ' ' || rune.Value == '-' || rune.Value == '_' || rune.Value == '\'' || rune.Value == '.';
                if (!emoji && !plain)
                    return false;

                glueAllowed = emoji || Rune.IsDigit(rune);
            }

            if (PlayerNameLength(name) > MaxPlayerNameLength)
                return false;

            return !string.Equals(name, TimeTrialSkills.MaskedNinjaName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// How long <paramref name="name"/> is as the player sees it: one per letter, digit or mark, and one per
        /// emoji, however many code points join into it: a flag, a family or a thumbs-up with a skin tone each
        /// count once. This is the count <see cref="MaxPlayerNameLength"/> limits.
        /// </summary>
        public static int PlayerNameLength(string name)
            => string.IsNullOrEmpty(name) ? 0 : new StringInfo(name).LengthInTextElements;

        /// <summary>
        /// Whether <paramref name="rune"/> is an emoji, or one of the symbols the emoji keyboards offer beside
        /// them: the pictographs and symbols of the Basic Multilingual Plane that emoji fonts draw, and every
        /// supplementary emoji block from the playing cards and flags through the faces, food, transport and the
        /// extended pictographs. Nothing in ASCII is an emoji.
        /// </summary>
        static bool IsEmoji(Rune rune)
        {
            int value = rune.Value;
            if (value < 0x00A9)
                return false;

            return value == 0x00A9 || value == 0x00AE                  // copyright and registered
                || value == 0x203C || value == 0x2049                  // double exclamation, exclamation question
                || value == 0x2122 || value == 0x2139                  // trade mark, information
                || (value >= 0x2194 && value <= 0x21AA)                // arrows
                || (value >= 0x231A && value <= 0x23FF)                // watches, hourglasses, media controls
                || value == 0x24C2                                     // circled M
                || (value >= 0x25AA && value <= 0x25FE)                // geometric shapes
                || (value >= 0x2600 && value <= 0x27BF)                // miscellaneous symbols and dingbats
                || value == 0x2934 || value == 0x2935                  // curved arrows
                || (value >= 0x2B00 && value <= 0x2BFF)                // arrows, stars and squares
                || value == 0x3030 || value == 0x303D                  // wavy dash, part alternation mark
                || value == 0x3297 || value == 0x3299                  // circled ideographs
                || (value >= 0x1F000 && value <= 0x1FAFF);             // every supplementary emoji block
        }

        /// <summary>
        /// Whether <paramref name="rune"/> is one of the invisible code points that join emoji into one picture:
        /// the zero-width joiner, the text and emoji presentation selectors, the combining keycap, the skin
        /// tone modifiers and the tag characters that spell a subdivision flag.
        /// </summary>
        static bool IsEmojiGlue(Rune rune)
        {
            int value = rune.Value;
            return value == 0x200D                                     // zero-width joiner
                || value == 0xFE0E || value == 0xFE0F                  // text and emoji presentation selectors
                || value == 0x20E3                                     // combining enclosing keycap
                || (value >= 0x1F3FB && value <= 0x1F3FF)              // skin tone modifiers
                || (value >= 0xE0020 && value <= 0xE007F);             // tags
        }

        /// <summary>Buy <paramref name="skill"/>: the Orbs are spent and the node is open for good.</summary>
        public void UnlockSkill(SkillInfo skill)
        {
            Orbs -= skill.OrbCost;
            UnlockedSkills.Add(skill.Id);
        }

        /// <summary>Set the player's name, which is also the name their ghost is known by. The caller has already validated it.</summary>
        public void SetPlayerName(string name)
        {
            PlayerName = name;
        }

        /// <summary>Put <paramref name="skillId"/> in the one slot, or empty it when it is null.</summary>
        public void EquipSkill(SkillId skillId)
        {
            EquippedSkillId = skillId;
        }

        /// <summary>
        /// Keep the field the server selected for the attempt that is open, so the run's segments can be placed
        /// against the opponents it was actually raced against. A lineup for any other attempt is dropped.
        /// </summary>
        public void SetActiveLineup(int ticketId, IReadOnlyList<TimeTrialGhost> lineup)
        {
            ActiveLineup = ActiveTimeTrial != null && ActiveTimeTrial.TicketId == ticketId
                ? TimeTrialLineup.From(ticketId, lineup)
                : null;
        }

        /// <summary>
        /// Record an accepted run, place its segments against the lineup it was raced against, pay the Orbs
        /// those placements are worth, and close the attempt. Returns true if the run beat the player's
        /// previous best on that course.
        /// </summary>
        public bool CommitTimeTrialResult(CourseId courseId, TimeTrialResult result, TimeTrialStanding standing)
        {
            TimeTrialResult previousBest = BestResults.GetValueOrDefault(courseId);
            bool            isNewBest    = previousBest == null || result.FinishTimeMs < previousBest.FinishTimeMs;

            if (isNewBest)
                BestResults[courseId] = result;

            // A standing is the pool's answer, so its presence is what says the run was recorded there.
            if (standing != null && IsNewPoolBest(courseId, result.FinishTimeMs))
                PoolBestTimesMs[courseId] = result.FinishTimeMs;

            PlaceSegments(result);

            CompletedRunCounts[courseId] = CompletedRunCounts.GetValueOrDefault(courseId) + 1;
            ActiveTimeTrial              = null;
            ActiveLineup                 = null;

            return isNewBest;
        }

        /// <summary>The player's best time recorded into <paramref name="courseId"/>'s pool, or 0 if none is.</summary>
        public int GetPoolBestTimeMs(CourseId courseId)
            => courseId != null ? PoolBestTimesMs.GetValueOrDefault(courseId) : 0;

        /// <summary>Whether <paramref name="finishTimeMs"/> would beat the player's best recorded into the course's pool.</summary>
        public bool IsNewPoolBest(CourseId courseId, int finishTimeMs)
        {
            int poolBestMs = GetPoolBestTimeMs(courseId);
            return poolBestMs <= 0 || finishTimeMs < poolBestMs;
        }

        /// <summary>
        /// Close attempt <paramref name="ticketId"/> without recording anything. Any other open attempt is left
        /// as it is.
        /// </summary>
        public void RejectTimeTrialResult(int ticketId)
        {
            // A submission turned down because the player had already started a newer run must not close that
            // newer run nor drop the field it is racing.
            if (ActiveTimeTrial != null && ActiveTimeTrial.TicketId == ticketId)
            {
                ActiveTimeTrial = null;
                ActiveLineup    = null;
            }
        }

        /// <summary>
        /// Close practice attempt <paramref name="ticketId"/> without recording anything about it. The last
        /// counted run's placements and payout stay as they were.
        /// </summary>
        public void ClosePracticeTimeTrial(int ticketId)
        {
            if (ActiveTimeTrial == null || ActiveTimeTrial.TicketId != ticketId)
                return;

            ActiveTimeTrial = null;
            ActiveLineup    = null;
        }

        /// <summary>Orbs a placement in a field of four is worth, from game config.</summary>
        public int GetOrbsForPlacement(int placement)
        {
            switch (placement)
            {
                case 1:  return GameConfig.Global.OrbsForFirst;
                case 2:  return GameConfig.Global.OrbsForSecond;
                case 3:  return GameConfig.Global.OrbsForThird;
                case 4:  return GameConfig.Global.OrbsForFourth;
                default: return 0;
            }
        }

        /// <summary>
        /// Rank the run's segments against the lineup bound to the same attempt and pay what the placements are
        /// worth. A run with no usable lineup is placed nowhere and pays nothing.
        /// </summary>
        void PlaceSegments(TimeTrialResult result)
        {
            int  segmentCount = result.SegmentCount;
            // A held lineup is always the open attempt's: SetActiveLineup keeps one only for the open ticket,
            // and every change of ticket drops it.
            bool hasLineup    = ActiveLineup != null && ActiveLineup.Opponents.Count > 0;

            // Every opponent has to have timed both torii of every climb the run did, or a placement would
            // compare different stretches of course. One opponent short places the whole run nowhere.
            if (hasLineup)
            {
                foreach (TimeTrialLineupOpponent opponent in ActiveLineup.Opponents)
                {
                    if (!TimeTrialSegments.Covers(opponent.GateTimesMs, opponent.ClimbEntryTimesMs, segmentCount))
                    {
                        hasLineup = false;
                        break;
                    }
                }
            }

            List<int> placements = new List<int>();
            int       orbs       = 0;

            for (int segment = 0; segment < segmentCount; segment++)
            {
                if (!hasLineup)
                {
                    placements.Add(0);
                    continue;
                }

                // One plus however many opponents were quicker, so equal times share the better placement.
                int mine      = result.SegmentTimeMs(segment);
                int placement = 1;
                foreach (TimeTrialLineupOpponent opponent in ActiveLineup.Opponents)
                {
                    if (opponent.SegmentTimeMs(segment) < mine)
                        placement++;
                }

                placements.Add(placement);
                orbs += GetOrbsForPlacement(placement);
            }

            LastSegmentPlacements = placements;
            LastOrbsEarned        = orbs;
            Orbs                 += orbs;
        }
    }
}
