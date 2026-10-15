using Metaplay.Core.Config;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The game's configuration: the global tunables, the movement values the time trial simulation runs on,
    /// the Skill tree Orbs are spent on, and the course parts maps are composed from.
    /// <para>
    /// Every entry is built from the CSV sheet of the same name in <c>GameConfigSource/</c>, so adding an entry
    /// here means adding <c>GameConfigSource/&lt;EntryName&gt;.csv</c> beside it. Use
    /// <c>GameConfigLibrary&lt;TKey, TInfo&gt;</c> for item libraries and <c>GameConfigKeyValue</c> for
    /// single-instance tunables. The exception is <see cref="GameConfigBuild.JsonSourcedEntries"/>, which are
    /// read from <c>GameConfigSource/&lt;EntryName&gt;.json</c> instead.
    /// </para>
    /// </summary>
    public class SharedGameConfig : SharedGameConfigBase
    {
        /// <summary>
        /// Global, singleton game configuration values (game-wide tunables).
        /// </summary>
        [GameConfigEntry("Global")]
        public GlobalConfig Global { get; private set; } = new GlobalConfig();

        /// <summary>
        /// Designer-tunable movement values for the time trial simulation.
        /// </summary>
        [GameConfigEntry("TimeTrialRules")]
        public TimeTrialRules TimeTrialRules { get; private set; } = new TimeTrialRules();

        /// <summary>
        /// The Skill tree Orbs are spent on: what each node costs, what it needs bought first, and how it
        /// moves the movement values above.
        /// </summary>
        [GameConfigEntry("Skills")]
        public GameConfigLibrary<SkillId, SkillInfo> Skills { get; private set; }

        /// <summary>
        /// The obstacle sections a map is composed from. Authored in <c>GameConfigSource/CourseParts.json</c>,
        /// read through <see cref="GameBuildParameters.CoursePartsSource"/> and parsed by
        /// <see cref="CoursePartJson"/> in <see cref="GameConfigBuild"/>.
        /// </summary>
        [GameConfigEntry(nameof(CourseParts), configBuildSource: nameof(GameBuildParameters.CoursePartsSource))]
        public GameConfigLibrary<CoursePartId, CoursePartInfo> CourseParts { get; private set; }

        /// <summary>
        /// Rejects config content that would build cleanly but play broken, reporting each failure against the
        /// sheet and row that caused it.
        /// <para>
        /// A check belongs here when the failure it catches would otherwise show up as a valid-looking screen
        /// that nothing at run time reports.
        /// </para>
        /// <para>
        /// The SDK treats a reported error as part of the build report, not as a build failure. Refusing to
        /// write an archive that carries one is <c>tools/GameConfigGen</c>'s gate.
        /// </para>
        /// </summary>
        public override void BuildTimeValidate(GameConfigValidationResult validationResult)
        {
            base.BuildTimeValidate(validationResult);

            if (TimeTrialRules.RunSpeed <= 0)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.RunSpeed),
                    $"RunSpeed is {TimeTrialRules.RunSpeed}, so the runner would never reach the finish. It must be greater than zero.",
                    columnHint: "Value");
            }

            if (TimeTrialRules.MaxLaunchSpeed < TimeTrialRules.MinLaunchSpeed)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.MaxLaunchSpeed),
                    $"MaxLaunchSpeed is {TimeTrialRules.MaxLaunchSpeed}, below MinLaunchSpeed {TimeTrialRules.MinLaunchSpeed}. Charging must not weaken the jump.",
                    columnHint: "Value");
            }

            // The simulation divides the charge held by this, and a full-charge jump has to be reachable.
            if (TimeTrialRules.MaxChargeSteps < 1)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.MaxChargeSteps),
                    $"MaxChargeSteps is {TimeTrialRules.MaxChargeSteps}. It must be at least 1 step.",
                    columnHint: "Value");
            }

            if (TimeTrialRules.Gravity <= 0)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.Gravity),
                    $"Gravity is {TimeTrialRules.Gravity}, so a jump would never come back down.",
                    columnHint: "Value");
            }

            if (TimeTrialRules.LedgeGrabHangSteps < 1)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.LedgeGrabHangSteps),
                    $"LedgeGrabHangSteps is {TimeTrialRules.LedgeGrabHangSteps}. It must be at least 1 step so the catch is readable.",
                    columnHint: "Value");
            }

            if (TimeTrialRules.LedgeGrabRiseSpeed <= 0)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.LedgeGrabRiseSpeed),
                    $"LedgeGrabRiseSpeed is {TimeTrialRules.LedgeGrabRiseSpeed}, so a mantle would never reach the lip. It must be greater than zero.",
                    columnHint: "Value");
            }

            if (TimeTrialRules.LedgeGrabTopTransitionSteps < 1)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.LedgeGrabTopTransitionSteps),
                    $"LedgeGrabTopTransitionSteps is {TimeTrialRules.LedgeGrabTopTransitionSteps}. It must be at least 1 step.",
                    columnHint: "Value");
            }

            if (TimeTrialRules.LedgeGrabClearanceHeight <= 0)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.LedgeGrabClearanceHeight),
                    $"LedgeGrabClearanceHeight is {TimeTrialRules.LedgeGrabClearanceHeight}. It must be greater than zero so horizontal motion starts above the lip.",
                    columnHint: "Value");
            }

            // A mantle that sets the runner down above the running pace pays the player for missing a jump.
            if (TimeTrialRules.LedgeGrabResumeSpeed <= 0)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.LedgeGrabResumeSpeed),
                    $"LedgeGrabResumeSpeed is {TimeTrialRules.LedgeGrabResumeSpeed}, so a completed mantle would set the runner down at a standstill. It must be greater than zero.",
                    columnHint: "Value");
            }
            else if (TimeTrialRules.LedgeGrabResumeSpeed > TimeTrialRules.RunSpeed)
            {
                validationResult.Error("TimeTrialRules", nameof(Game.Logic.TimeTrialRules.LedgeGrabResumeSpeed),
                    $"LedgeGrabResumeSpeed is {TimeTrialRules.LedgeGrabResumeSpeed}, above the RunSpeed {TimeTrialRules.RunSpeed}. Climbing out of a pit must not be faster than running.",
                    columnHint: "Value");
            }

            if (Global.TimeTrialTicketLifetimeSeconds < 1)
            {
                validationResult.Error("Global", nameof(GlobalConfig.TimeTrialTicketLifetimeSeconds),
                    $"TimeTrialTicketLifetimeSeconds is {Global.TimeTrialTicketLifetimeSeconds}, so every run would expire before it could be submitted. It must be at least 1 second.",
                    columnHint: "Value");
            }

            if (Global.MapRotationPeriodSeconds < 1)
            {
                validationResult.Error("Global", nameof(GlobalConfig.MapRotationPeriodSeconds),
                    $"MapRotationPeriodSeconds is {Global.MapRotationPeriodSeconds}, so the active set would rotate on every tick. It must be at least 1 second.",
                    columnHint: "Value");
            }

            // The connective climbs between a map's parts are built from these three, and composing a map throws
            // on a climb that lacks any of them.
            if (Global.CourseClimbRise <= 0)
            {
                validationResult.Error("Global", nameof(GlobalConfig.CourseClimbRise),
                    $"CourseClimbRise is {Global.CourseClimbRise}, so a climb would arrive at the height it left. It must be greater than zero.",
                    columnHint: "Value");
            }

            if (Global.CourseClimbSlopeLength <= 0)
            {
                validationResult.Error("Global", nameof(GlobalConfig.CourseClimbSlopeLength),
                    $"CourseClimbSlopeLength is {Global.CourseClimbSlopeLength}, so a climb would have no slope to be carried up. It must be greater than zero.",
                    columnHint: "Value");
            }

            if (Global.CourseClimbRunOutLength <= 0)
            {
                validationResult.Error("Global", nameof(GlobalConfig.CourseClimbRunOutLength),
                    $"CourseClimbRunOutLength is {Global.CourseClimbRunOutLength}, so a climb's gate would sit on its top rather than past it. It must be greater than zero.",
                    columnHint: "Value");
            }

            ValidateSkills(validationResult);
            ValidateCourseComposition(validationResult);
        }

        /// <summary>
        /// Checks the Skill tree's shape, and each Skill's price and stat trade.
        /// </summary>
        void ValidateSkills(GameConfigValidationResult validationResult)
        {
            int[] expectedPerTier = { 1, 2, 4, 4 };
            int[] countPerTier    = new int[expectedPerTier.Length];

            foreach ((SkillId id, SkillInfo skill) in Skills)
            {
                if (skill.Tier < 1 || skill.Tier > expectedPerTier.Length)
                {
                    validationResult.Error("Skills", id.ToString(),
                        $"{id} is tier {skill.Tier}. The tree has {expectedPerTier.Length} tiers, numbered from 1.",
                        columnHint: nameof(SkillInfo.Tier));
                    continue;
                }

                countPerTier[skill.Tier - 1]++;

                if (skill.Tier == 1)
                {
                    if (skill.Prerequisite != null)
                    {
                        validationResult.Error("Skills", id.ToString(),
                            $"{id} is the root of the tree, so it must have no prerequisite.",
                            columnHint: nameof(SkillInfo.Prerequisite));
                    }
                }
                else if (skill.Prerequisite == null)
                {
                    validationResult.Error("Skills", id.ToString(),
                        $"{id} is tier {skill.Tier} and names no prerequisite, so nothing would ever open it.",
                        columnHint: nameof(SkillInfo.Prerequisite));
                }
                else
                {
                    SkillInfo prerequisite = Skills.GetValueOrDefault(skill.Prerequisite);
                    if (prerequisite == null)
                    {
                        validationResult.Error("Skills", id.ToString(),
                            $"{id} requires {skill.Prerequisite}, which is not a Skill in this sheet.",
                            columnHint: nameof(SkillInfo.Prerequisite));
                    }
                    else if (prerequisite.Tier != skill.Tier - 1)
                    {
                        validationResult.Error("Skills", id.ToString(),
                            $"{id} is tier {skill.Tier} but requires {skill.Prerequisite}, which is tier {prerequisite.Tier}. A prerequisite must sit exactly one tier lower.",
                            columnHint: nameof(SkillInfo.Prerequisite));
                    }
                }

                if (skill.OrbCost < 1)
                {
                    validationResult.Error("Skills", id.ToString(),
                        $"{id} costs {skill.OrbCost} Orbs. Every Skill must cost at least 1, or it is not something the gates pay for.",
                        columnHint: nameof(SkillInfo.OrbCost));
                }

                ValidatePermille(validationResult, id, skill.RunSpeedPermille,        nameof(SkillInfo.RunSpeedPermille));
                ValidatePermille(validationResult, id, skill.RunAccelerationPermille, nameof(SkillInfo.RunAccelerationPermille));
                ValidatePermille(validationResult, id, skill.MaxLaunchSpeedPermille,  nameof(SkillInfo.MaxLaunchSpeedPermille));
                ValidatePermille(validationResult, id, skill.FootingPermille,         nameof(SkillInfo.FootingPermille));

                ValidateSkillTrade(validationResult, id, skill);
            }

            for (int tier = 0; tier < expectedPerTier.Length; tier++)
            {
                if (countPerTier[tier] != expectedPerTier[tier])
                {
                    validationResult.Error("Skills", $"tier {tier + 1}",
                        $"Tier {tier + 1} holds {countPerTier[tier]} Skills. The tree is {string.Join("-", expectedPerTier)}.",
                        columnHint: nameof(SkillInfo.Tier));
                }
            }

            // The root is what the Name Tag is, and the model asks for it by id when a name is set.
            if (Skills.GetValueOrDefault(TimeTrialSkills.NameTag) == null)
            {
                validationResult.Error("Skills", TimeTrialSkills.NameTag.ToString(),
                    $"The tree has no {TimeTrialSkills.NameTag} row, so no player could ever set a name.",
                    columnHint: nameof(SkillInfo.Id));
            }
        }

        /// <summary>
        /// Checks the benefits and penalties a Skill trades against what its tier allows.
        /// </summary>
        void ValidateSkillTrade(GameConfigValidationResult validationResult, SkillId id, SkillInfo skill)
        {
            List<SkillEffect> effects = skill.GetAllEffects();
            int benefits  = 0;
            int penalties = 0;
            foreach (SkillEffect effect in effects)
            {
                if (effect.IsBenefit)
                    benefits++;
                else
                    penalties++;

                if (effect.Stat == SkillStat.Jump && !effect.IsBenefit)
                {
                    validationResult.Error("Skills", id.ToString(),
                        $"{id} makes Jump a penalty. Course parts are checked only under the base rules and no gap is checked against a shortened jump, so a shorter jump could leave a published gap uncrossable.",
                        columnHint: SkillInfo.ColumnOf(SkillStat.Jump));
                }
            }

            string firstColumn = effects.Count > 0 ? SkillInfo.ColumnOf(effects[0].Stat) : nameof(SkillInfo.RunSpeedPermille);

            switch (skill.Tier)
            {
                case 1:
                    if (effects.Count != 0)
                    {
                        validationResult.Error("Skills", id.ToString(),
                            $"{id} is the root of the tree and moves {SkillInfo.ColumnOf(effects[0].Stat)}. The root buys a name and no movement.",
                            columnHint: firstColumn);
                    }
                    break;

                case 2:
                    if (effects.Count != 1 || benefits != 1)
                    {
                        validationResult.Error("Skills", id.ToString(),
                            $"{id} is tier 2 with {benefits} benefits and {penalties} penalties. A tier-2 node is exactly one benefit and nothing else.",
                            columnHint: firstColumn);
                    }
                    break;

                case 3:
                    if (benefits != 1 || penalties != 1)
                    {
                        validationResult.Error("Skills", id.ToString(),
                            $"{id} is tier 3 with {benefits} benefits and {penalties} penalties. A tier-3 node is exactly one benefit and one penalty.",
                            columnHint: firstColumn);
                    }
                    break;

                case 4:
                    if (benefits != 1 || penalties != 2)
                    {
                        validationResult.Error("Skills", id.ToString(),
                            $"{id} is tier 4 with {benefits} benefits and {penalties} penalties. A tier-4 node is exactly one benefit and two penalties.",
                            columnHint: firstColumn);
                    }

                    SkillInfo parent = skill.Prerequisite != null ? Skills.GetValueOrDefault(skill.Prerequisite) : null;
                    if (parent != null)
                        ValidateTierFourAgainstParent(validationResult, id, skill, parent);
                    break;
            }
        }

        /// <summary>
        /// Checks a tier-4 Skill builds on its tier-3 prerequisite.
        /// </summary>
        static void ValidateTierFourAgainstParent(GameConfigValidationResult validationResult, SkillId id, SkillInfo skill, SkillInfo parent)
        {
            int newStats = 0;

            foreach (SkillStat stat in SkillInfo.AllStats)
            {
                int    change       = skill.PermilleOf(stat)  - SkillInfo.UnchangedPermille;
                int    parentChange = parent.PermilleOf(stat) - SkillInfo.UnchangedPermille;
                string column       = SkillInfo.ColumnOf(stat);

                if (parentChange == 0)
                {
                    if (change == 0)
                        continue;

                    newStats++;
                    if (change > 0)
                    {
                        validationResult.Error("Skills", id.ToString(),
                            $"{id} raises {column}, which its prerequisite {parent.Id} leaves alone. The one stat a tier-4 node adds is a penalty.",
                            columnHint: column);
                    }
                }
                else if (Math.Sign(change) != Math.Sign(parentChange) || Math.Abs(change) <= Math.Abs(parentChange))
                {
                    validationResult.Error("Skills", id.ToString(),
                        $"{id}'s {column} does not move the way its prerequisite {parent.Id} moves it and further. A tier-4 node strengthens every stat its parent moves.",
                        columnHint: column);
                }
            }

            if (newStats != 1)
            {
                validationResult.Error("Skills", id.ToString(),
                    $"{id} moves {newStats} stats its prerequisite {parent.Id} leaves alone. A tier-4 node adds exactly one, a penalty.",
                    columnHint: nameof(SkillInfo.Prerequisite));
            }
        }

        /// <summary>
        /// Checks a Skill's stat modifier is usable as a share of the base value.
        /// </summary>
        static void ValidatePermille(GameConfigValidationResult validationResult, SkillId id, int permille, string memberName)
        {
            if (permille <= 0)
            {
                validationResult.Error("Skills", id.ToString(),
                    $"{id}'s {memberName} is {permille}. A modifier is a share of the base value, so it must be greater than zero; 1000 leaves the value alone.",
                    columnHint: memberName);
            }
        }

        /// <summary>
        /// Proves every course part, and every map they compose, playable.
        /// </summary>
        void ValidateCourseComposition(GameConfigValidationResult validationResult)
        {
            if (CourseParts.Count == 0)
            {
                validationResult.Error(nameof(CourseParts), "",
                    "There are no course parts, so no map could be composed and no run could be started.",
                    columnHint: nameof(CoursePartInfo.Ground));
                return;
            }

            bool reported = false;

            foreach ((CoursePartId id, CoursePartInfo part) in CourseParts)
            {
                foreach (string message in part.Validate())
                {
                    validationResult.Error(nameof(CourseParts), id.ToString(), message,
                        columnHint: nameof(CoursePartInfo.Ground));
                    reported = true;
                }
            }

            foreach (CoursePartPosition position in CourseComposer.PositionsInOrder)
            {
                bool anyEligible = false;
                foreach ((CoursePartId id, CoursePartInfo part) in CourseParts)
                {
                    if (part.MayOccupy(position))
                    {
                        anyEligible = true;
                        break;
                    }
                }

                if (!anyEligible)
                {
                    validationResult.Error(nameof(CourseParts), position.ToString(),
                        $"No part may be composed into the {position} position, so no map could be composed.",
                        columnHint: nameof(CoursePartInfo.Positions));
                    reported = true;
                }
            }

            IReadOnlyList<CourseId> mapIds = CourseComposer.LegalMapIds(this);
            if (mapIds.Count == 0)
            {
                validationResult.Error(nameof(CourseParts), "",
                    "No legal map can be composed from these parts, so there would be nothing to play.",
                    columnHint: nameof(CoursePartInfo.Positions));
                reported = true;
            }

            // Composing anything at all needs sound parts and a legal triplet, and both throw rather than
            // report, so there is nothing further to say until what has been reported is fixed.
            if (reported)
                return;

            // A sound part composed on its own has no climb in it, so nothing beyond the rules already checked
            // above can make this throw.
            foreach ((CoursePartId id, CoursePartInfo part) in CourseParts)
            {
                CourseDefinition alone = CourseInspection.PartAlone(part, Global);
                ValidateFinishesWithNoInput(validationResult, id.ToString(), alone, "on its own");
            }

            // Cubic in the number of parts.
            foreach (CourseId mapId in mapIds)
            {
                CourseDefinition map;
                try
                {
                    map = CourseComposer.Compose(mapId, this);
                }
                catch (ArgumentException exception)
                {
                    validationResult.Error(nameof(CourseParts), mapId.ToString(), exception.Message,
                        columnHint: nameof(CoursePartInfo.Ground));
                    continue;
                }

                ValidateFinishesWithNoInput(validationResult, mapId.ToString(), map, "as a map");
            }

            // Offline practice has no registry to ask, so this one id is the whole of what it can play.
            if (CourseResolver.TryResolve(Global.OfflinePracticeMapId, this) == null)
            {
                validationResult.Error("Global", nameof(GlobalConfig.OfflinePracticeMapId),
                    $"OfflinePracticeMapId is '{Global.OfflinePracticeMapId}', which names no map these parts compose, so offline practice would have nothing to open.",
                    columnHint: "Value");
            }
        }

        /// <summary>
        /// Checks a player who never presses the button still finishes the course, with room to spare.
        /// </summary>
        void ValidateFinishesWithNoInput(GameConfigValidationResult validationResult, string configKey, CourseDefinition course, string what)
        {
            // An empty recording is the slowest way through a course: no jump is made, every pit is fallen into
            // and every spike bed is walked through.
            TimeTrialResult result = TimeTrialSimulation.Replay(course, TimeTrialRules, new TimeTrialRecording());

            if (result == null)
            {
                validationResult.Error(nameof(CourseParts), configKey,
                    $"{configKey} never reaches the finish {what} with no input, so a run on it would never end.",
                    columnHint: nameof(CoursePartInfo.Ground));
                return;
            }

            // Half the budget spent this way leaves no room for a player who plays worse than doing nothing.
            if (result.Steps >= TimeTrialConstants.MaxRunSteps / 2)
            {
                validationResult.Error(nameof(CourseParts), configKey,
                    $"{configKey} takes {result.Steps} steps to finish {what} with no input, over half of the {TimeTrialConstants.MaxRunSteps}-step budget a run is cut off at.",
                    columnHint: nameof(CoursePartInfo.Ground));
            }
        }
    }
}
