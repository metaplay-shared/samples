using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The Skill tree as the game config ships it, the movement values a Skill derives, and the two actions a
    /// player spends Orbs and fills their one slot with. All of it is shared logic and published config, so it
    /// runs without a server.
    /// </summary>
    [TestFixture]
    public class SkillTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        #region Helpers

        /// <summary>The map offline practice plays: a real composition of the committed parts.</summary>
        static CourseId PracticeMapId => _gameConfig.Global.OfflinePracticeMapId;

        static CourseDefinition PracticeMap => CourseComposer.Compose(PracticeMapId, _gameConfig);

        PlayerModel CreatePlayerModel(ulong playerIdValue = 12345)
        {
            PlayerModel player = PlayerModelUtil.CreateNewPlayerModel<PlayerModel>(
                MetaTime.FromDateTime(new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc)),
                _gameConfig,
                playerId: EntityId.Create(EntityKindCore.Player, playerIdValue),
                name: "Example name");

            player.LogicVersion = IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options.SupportedLogicVersions.MaxVersion;
            player.OnInitialLogin();
            return player;
        }

        static MetaActionResult Execute(PlayerModel player, ModelAction action) => action.InvokeExecute(player, commit: true);

        /// <summary>
        /// Win every segment of one run against a field that is slower everywhere. Segment placements are the
        /// only route Orbs take onto the model, so this is what a test with something to spend has to do.
        /// </summary>
        static int EarnOrbsFromOneWinningRun(PlayerModel player)
        {
            Execute(player, new PlayerOpenTimeTrial(PracticeMapId, lineup: null));

            TimeTrialTicket  ticket       = player.ActiveTimeTrial;
            CourseDefinition course       = CourseResolver.TryResolve(ticket.CourseId, _gameConfig);
            int              segmentCount = TimeTrialSegments.CountFor(course.Climbs.Count);

            TimeTrialResult mine  = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 20_000), climbMs: 5_000);
            TimeTrialResult their = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(segmentCount, 30_000), climbMs: 5_000);

            player.SetActiveLineup(ticket.TicketId, new List<TimeTrialGhost>
            {
                TimeTrialRuns.GhostOf(course.Id, "Slowcoach", their),
            });

            int before = player.Orbs;

            Execute(player, new PlayerAcceptTimeTrialResult(ticket.TicketId, mine, standing: null));

            Assert.That(player.Orbs, Is.GreaterThan(before), "a run that wins every segment has to pay something");
            return player.Orbs;
        }

        /// <summary>Win runs until the player can afford <paramref name="orbs"/>.</summary>
        static void EarnAtLeast(PlayerModel player, int orbs)
        {
            while (player.Orbs < orbs)
                EarnOrbsFromOneWinningRun(player);
        }

        /// <summary>What buying every one of <paramref name="skills"/> costs at the published prices.</summary>
        static int CostOf(params SkillId[] skills)
        {
            int total = 0;
            foreach (SkillId skill in skills)
                total += _gameConfig.Skills[skill].OrbCost;
            return total;
        }

        #endregion

        #region The tree as it is published

        /// <summary>
        /// A deeper node costs more than the one that opens it, so the tree is climbed rather than cherry-picked
        /// and the Orbs a plateau pays keep having somewhere to go.
        /// </summary>
        [Test]
        public void CostRisesWithEveryTier()
        {
            foreach ((SkillId id, SkillInfo skill) in _gameConfig.Skills)
            {
                if (skill.Prerequisite == null)
                    continue;

                Assert.That(skill.OrbCost, Is.GreaterThan(_gameConfig.Skills[skill.Prerequisite].OrbCost),
                    $"{id} does not cost more than the Skill that opens it");
            }
        }

        /// <summary>
        /// The Name Tag is the root, and every Skill names what it is shown as: a display name and the icon its
        /// medallion is drawn with. A blank <c>Icon</c> cell reads as the first enum value, which is the Name
        /// Tag's own icon, so any other Skill carrying it is a row left unfilled.
        /// </summary>
        [Test]
        public void EverySkillNamesWhatItIsShownAs()
        {
            SkillInfo nameTag = _gameConfig.Skills[TimeTrialSkills.NameTag];
            Assert.That(nameTag.Tier, Is.EqualTo(1), "the Name Tag is the root");
            Assert.That(nameTag.Icon, Is.EqualTo(SkillIcon.Tag));

            foreach ((SkillId id, SkillInfo skill) in _gameConfig.Skills)
            {
                Assert.That(skill.DisplayName, Is.Not.Empty, $"{id} needs a name to show");
                Assert.That(Enum.IsDefined(typeof(SkillIcon), skill.Icon), Is.True, $"{id} names an icon the client cannot draw");

                if (skill.Id == TimeTrialSkills.NameTag)
                    continue;

                Assert.That(skill.Icon, Is.Not.EqualTo(SkillIcon.Tag), $"{id} has no Icon cell filled in");
            }
        }

        #endregion

        #region The tree the config build accepts

        [Test]
        public async Task TheShippedSkillsSheetBuildsWithoutASkillError()
        {
            List<string> errors = await SkillErrorsOfABuildAsync(rowId: null, cellEdits: null);
            Assert.That(errors, Is.Empty);
        }

        /// <summary>
        /// The shipped Skills sheet with one row edited to break one rule of the tree's shape is reported against
        /// that rule by the config build. An edit may break a neighbouring rule as well (a tier-4 node checked
        /// against a parent that no longer has the parent's shape), so only the intended error is asserted.
        /// </summary>
        [TestCase("wind-stride", "Tier=5",                              "The tree has 4 tiers, numbered from 1.",                  TestName = "ATierOutsideTheTree")]
        [TestCase("stone-guard", "Tier=3;Prerequisite=shield",          "Tier 4 holds 3 Skills. The tree is 1-2-4-4.",             TestName = "ATierHoldingTheWrongNumberOfSkills")]
        [TestCase("name-tag",    "Prerequisite=shield",                 "is the root of the tree, so it must have no prerequisite.", TestName = "ARootWithAPrerequisite")]
        [TestCase("wind-stride", "Prerequisite=",                       "names no prerequisite, so nothing would ever open it.",   TestName = "ANodeWithNoPrerequisite")]
        [TestCase("wind-stride", "Prerequisite=no-such-skill",          "which is not a Skill in this sheet.",                     TestName = "APrerequisiteNotInTheSheet")]
        [TestCase("wind-stride", "Prerequisite=shoe-a",                 "A prerequisite must sit exactly one tier lower.",         TestName = "APrerequisiteTwoTiersLower")]
        [TestCase("name-tag",    "RunSpeedPermille=1100",               "The root buys a name and no movement.",                   TestName = "ARootThatMovesAStat")]
        [TestCase("shoe-a",      "FootingPermille=900",                 "A tier-2 node is exactly one benefit and nothing else.",  TestName = "ATierTwoNodeWithAPenalty")]
        [TestCase("swift-step",  "FootingPermille=1000",                "A tier-3 node is exactly one benefit and one penalty.",   TestName = "ATierThreeNodeWithNoPenalty")]
        [TestCase("wind-stride", "RunAccelerationPermille=1000",        "A tier-4 node is exactly one benefit and two penalties.", TestName = "ATierFourNodeWithOnePenalty")]
        [TestCase("wind-stride", "RunSpeedPermille=1000",               "A tier-4 node strengthens every stat its parent moves.",  TestName = "ATierFourNodeThatDropsAStatItsParentMoves")]
        [TestCase("sky-vault",   "FootingPermille=1100",                "The one stat a tier-4 node adds is a penalty.",           TestName = "ATierFourNodeThatAddsABenefit")]
        [TestCase("sky-vault",   "RunSpeedPermille=900",                "A tier-4 node adds exactly one, a penalty.",              TestName = "ATierFourNodeThatAddsTwoStats")]
        [TestCase("quick-start", "MaxLaunchSpeedPermille=900",          "makes Jump a penalty.",                                   TestName = "AJumpPenalty")]
        public async Task ASkillsSheetThatBreaksTheTreeIsReported(string rowId, string cellEdits, string expectedError)
        {
            List<string> errors = await SkillErrorsOfABuildAsync(rowId, cellEdits);
            Assert.That(errors, Has.Some.Contains(expectedError), $"the build reported: {string.Join(" / ", errors)}");
        }

        /// <summary>
        /// The error-level messages the config build reports against the Skills sheet, built from the shipped
        /// sources with <paramref name="cellEdits"/> (<c>Column=value</c> pairs joined by <c>;</c>) applied to
        /// the row <paramref name="rowId"/>. A null row builds the sources as they ship.
        /// </summary>
        static async Task<List<string>> SkillErrorsOfABuildAsync(string rowId, string cellEdits)
        {
            string sourcesDir = GameConfigBuildTests.CopyOfTheSources($"skills-{TestContext.CurrentContext.Test.Name}");

            if (rowId != null)
            {
                string   sheetPath = Path.Combine(sourcesDir, "Skills.csv");
                string[] lines     = await File.ReadAllLinesAsync(sheetPath);
                string[] header    = lines[0].Split(',');
                bool     edited    = false;

                for (int ndx = 1; ndx < lines.Length; ndx++)
                {
                    string[] cells = lines[ndx].Split(',');
                    if (cells[0] != rowId)
                        continue;

                    foreach (string edit in cellEdits.Split(';'))
                    {
                        string[] columnAndValue = edit.Split('=');
                        int      column         = Array.IndexOf(header, columnAndValue[0]);
                        Assert.That(column, Is.GreaterThanOrEqualTo(0), $"the Skills sheet has no {columnAndValue[0]} column");
                        cells[column] = columnAndValue[1];
                    }

                    lines[ndx] = string.Join(",", cells);
                    edited     = true;
                }

                Assert.That(edited, Is.True, $"the Skills sheet has no {rowId} row");
                await File.WriteAllLinesAsync(sheetPath, lines);
            }

            ConfigArchive archive = await GameConfigBuildTests.BuildArchiveAsync(sourcesDir);

            List<string> errors = new List<string>();
            foreach (GameConfigValidationMessage message in GameConfigMetaData.FromArchive(archive).BuildReport.ValidationMessages)
            {
                if (message.MessageLevel == GameConfigLogLevel.Error && message.SheetName == "Skills")
                    errors.Add(message.Message);
            }
            return errors;
        }

        #endregion

        #region The rules a Skill derives

        [Test]
        public void NoSkillLeavesEveryMovementValueWhereTheSheetPutIt()
        {
            TimeTrialRules baseRules = _gameConfig.TimeTrialRules;
            TimeTrialRules derived   = TimeTrialSkills.EffectiveRules(baseRules, null);

            Assert.That(derived, Is.Not.SameAs(baseRules), "the published rules must never be handed out to be written on");
            Assert.Multiple(() =>
            {
                Assert.That(derived.RunSpeed,        Is.EqualTo(baseRules.RunSpeed));
                Assert.That(derived.RunAcceleration, Is.EqualTo(baseRules.RunAcceleration));
                Assert.That(derived.MaxLaunchSpeed,  Is.EqualTo(baseRules.MaxLaunchSpeed));
                Assert.That(derived.MaxChargeSteps,  Is.EqualTo(baseRules.MaxChargeSteps));
                Assert.That(derived.StumbleSteps,    Is.EqualTo(baseRules.StumbleSteps));
                Assert.That(derived.Gravity,         Is.EqualTo(baseRules.Gravity));
                Assert.That(derived.MinLaunchSpeed,  Is.EqualTo(baseRules.MinLaunchSpeed));
                Assert.That(derived.LaunchAngleDegrees, Is.EqualTo(baseRules.LaunchAngleDegrees));
                Assert.That(derived.LedgeGrabHangSteps, Is.EqualTo(baseRules.LedgeGrabHangSteps));
                Assert.That(derived.LedgeGrabRiseSpeed, Is.EqualTo(baseRules.LedgeGrabRiseSpeed));
                Assert.That(derived.LedgeGrabTopTransitionSteps, Is.EqualTo(baseRules.LedgeGrabTopTransitionSteps));
                Assert.That(derived.LedgeGrabClearanceHeight,    Is.EqualTo(baseRules.LedgeGrabClearanceHeight));
                Assert.That(derived.LedgeGrabResumeSpeed,        Is.EqualTo(baseRules.LedgeGrabResumeSpeed));
            });
        }

        [Test]
        public void ASkillMovesTheValuesItNamesAndLeavesTheSheetAlone()
        {
            TimeTrialRules baseRules   = _gameConfig.TimeTrialRules;
            F32            baseSpeed   = baseRules.RunSpeed;
            int            baseStumble = baseRules.StumbleSteps;
            int            baseHang    = baseRules.LedgeGrabHangSteps;

            TimeTrialRules faster   = TimeTrialSkills.EffectiveRules(_gameConfig, SkillId.FromString("wind-stride"));
            TimeTrialRules steadier = TimeTrialSkills.EffectiveRules(_gameConfig, SkillId.FromString("stone-guard"));

            Assert.That(faster.RunSpeed, Is.GreaterThan(baseSpeed), "a Speed Skill has to make the runner faster");
            Assert.That(faster.MaxLaunchSpeed, Is.EqualTo(baseRules.MaxLaunchSpeed), "and must not touch what it does not name");
            Assert.That(steadier.StumbleSteps, Is.LessThan(baseStumble), "a Footing Skill has to shorten a stumble");
            Assert.That(steadier.LedgeGrabHangSteps, Is.LessThan(baseHang), "and has to shorten the fixed phases of a mantle");
            Assert.That(steadier.LedgeGrabTopTransitionSteps, Is.LessThan(baseRules.LedgeGrabTopTransitionSteps));
            Assert.That(steadier.LedgeGrabRiseSpeed, Is.EqualTo(baseRules.LedgeGrabRiseSpeed), "the climb speed itself is left unscaled");
            Assert.That(steadier.MaxLaunchSpeed, Is.EqualTo(baseRules.MaxLaunchSpeed), "a stat the sheet leaves alone stays alone");
            Assert.That(steadier.MaxChargeSteps, Is.EqualTo(baseRules.MaxChargeSteps), "a stat the sheet leaves alone stays alone");

            Assert.That(_gameConfig.TimeTrialRules.RunSpeed, Is.EqualTo(baseSpeed), "the published rules were written on");
            Assert.That(_gameConfig.TimeTrialRules.StumbleSteps, Is.EqualTo(baseStumble), "the published rules were written on");
        }

        [Test]
        public void EffectsListBenefitsBeforePenalties()
        {
            foreach ((SkillId id, SkillInfo skill) in _gameConfig.Skills)
            {
                bool seenPenalty = false;
                foreach (SkillEffect effect in skill.GetAllEffects())
                {
                    Assert.That(effect.Permille, Is.Not.EqualTo(SkillInfo.UnchangedPermille), $"{id} lists a stat it leaves alone");

                    if (!effect.IsBenefit)
                        seenPenalty = true;
                    else
                        Assert.That(seenPenalty, Is.False, $"{id} lists a benefit after a penalty");
                }
            }
        }

        /// <summary>
        /// Footing is a rate, so a permille above <see cref="SkillInfo.UnchangedPermille"/> on
        /// <see cref="SkillInfo.FootingPermille"/> is a shorter stumble and quicker fixed mantle phases, and one
        /// below is longer ones.
        /// </summary>
        [Test]
        public void AHigherFootingRateShortensTheStumbleAndALowerOneLengthensIt()
        {
            TimeTrialRules baseRules = _gameConfig.TimeTrialRules;

            TimeTrialRules steadier = TimeTrialSkills.EffectiveRules(baseRules, SkillWith(nameof(SkillInfo.FootingPermille), SkillInfo.UnchangedPermille * 2));
            TimeTrialRules clumsier = TimeTrialSkills.EffectiveRules(baseRules, SkillWith(nameof(SkillInfo.FootingPermille), SkillInfo.UnchangedPermille / 2));

            Assert.Multiple(() =>
            {
                Assert.That(steadier.StumbleSteps,                Is.LessThan(baseRules.StumbleSteps));
                Assert.That(steadier.LedgeGrabHangSteps,          Is.LessThan(baseRules.LedgeGrabHangSteps));
                Assert.That(steadier.LedgeGrabTopTransitionSteps, Is.LessThan(baseRules.LedgeGrabTopTransitionSteps));

                Assert.That(clumsier.StumbleSteps,                Is.GreaterThan(baseRules.StumbleSteps));
                Assert.That(clumsier.LedgeGrabHangSteps,          Is.GreaterThan(baseRules.LedgeGrabHangSteps));
                Assert.That(clumsier.LedgeGrabTopTransitionSteps, Is.GreaterThan(baseRules.LedgeGrabTopTransitionSteps));
            });

            // The published tree's footing penalties lengthen the stumble the same way.
            foreach ((SkillId id, SkillInfo skill) in _gameConfig.Skills)
            {
                if (skill.FootingPermille < SkillInfo.UnchangedPermille)
                    Assert.That(TimeTrialSkills.EffectiveRules(baseRules, skill).StumbleSteps, Is.GreaterThan(baseRules.StumbleSteps), $"{id}");
            }
        }

        /// <summary>
        /// The charge a jump builds up to is no Skill's stat, so every Skill leaves it exactly where the base
        /// rules have it.
        /// </summary>
        [Test]
        public void NoSkillChangesTheChargeSteps()
        {
            TimeTrialRules baseRules = _gameConfig.TimeTrialRules;

            foreach ((SkillId id, SkillInfo skill) in _gameConfig.Skills)
                Assert.That(TimeTrialSkills.EffectiveRules(baseRules, skill).MaxChargeSteps, Is.EqualTo(baseRules.MaxChargeSteps), $"{id}");
        }

        /// <summary>A Skill that moves one member by <paramref name="permille"/> and leaves the rest alone.</summary>
        static SkillInfo SkillWith(string memberName, int permille)
        {
            SkillInfo skill = new SkillInfo();
            typeof(SkillInfo).GetProperty(memberName).SetValue(skill, permille);
            return skill;
        }

        /// <summary>A runner playing the same course with a Speed Skill on sets a better time for it.</summary>
        [Test]
        public void ASpeedSkillShowsInTheTime()
        {
            CourseDefinition course      = PracticeMap;
            TimeTrialRules   baseRules   = TimeTrialSkills.EffectiveRules(_gameConfig, null);
            TimeTrialRules   skilledRules = TimeTrialSkills.EffectiveRules(_gameConfig, SkillId.FromString("wind-stride"));

            TimeTrialResult plain   = TimeTrialSimulation.Replay(course, baseRules, TimeTrialBotDriver.Drive(course, baseRules));
            TimeTrialResult skilled = TimeTrialSimulation.Replay(course, skilledRules, TimeTrialBotDriver.Drive(course, skilledRules));

            Assert.That(plain, Is.Not.Null);
            Assert.That(skilled, Is.Not.Null);
            Assert.That(skilled.FinishTimeMs, Is.LessThan(plain.FinishTimeMs), "more running speed has to show in the time");
        }

        /// <summary>
        /// A Skill changes what an input does, not what an input is: the recording format does not move, and a
        /// recording replayed under the wrong Skill still replays: it simply comes out as a different run. That
        /// is the whole reason every stored ghost and every ticket carries the Skill it was made with.
        /// </summary>
        [Test]
        public void ARecordingReplayedUnderTheWrongSkillIsADifferentRun()
        {
            CourseDefinition   course    = PracticeMap;
            TimeTrialRules     baseRules = TimeTrialSkills.EffectiveRules(_gameConfig, null);
            TimeTrialRecording recording = TimeTrialBotDriver.Drive(course, baseRules);

            TimeTrialResult asItWasRun = TimeTrialSimulation.Replay(course, baseRules, recording);
            TimeTrialResult asSomebodyElse = TimeTrialSimulation.Replay(
                course,
                TimeTrialSkills.EffectiveRules(_gameConfig, SkillId.FromString("wind-stride")),
                recording);

            Assert.That(asItWasRun, Is.Not.Null);
            Assert.That(asSomebodyElse, Is.Not.Null);
            Assert.That(asSomebodyElse.FinishTimeMs, Is.Not.EqualTo(asItWasRun.FinishTimeMs),
                "the same presses under another Skill must not be assumed to reproduce the run");
        }

        #endregion

        #region Unlocking and equipping

        [Test]
        public void ASkillCannotBeBoughtWithoutItsPrerequisite()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, CostOf(SkillId.FromString("shoe-a")));

            Assert.That(
                Execute(player, new PlayerUnlockSkill(SkillId.FromString("shoe-a"))),
                Is.EqualTo(ActionResults.SkillPrerequisiteMissing));
            Assert.That(player.UnlockedSkills, Is.Empty);
        }

        [Test]
        public void ASkillCannotBeBoughtWithoutTheOrbs()
        {
            PlayerModel player = CreatePlayerModel();

            Assert.That(player.Orbs, Is.Zero, "a new player has earned nothing yet");
            Assert.That(
                Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag)),
                Is.EqualTo(ActionResults.NotEnoughOrbs));
            Assert.That(player.UnlockedSkills, Is.Empty);
        }

        [Test]
        public void BuyingASkillSpendsItsOrbsOnceAndKeepsIt()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag));

            int       before  = player.Orbs;
            SkillInfo nameTag = _gameConfig.Skills[TimeTrialSkills.NameTag];

            Assert.That(Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Orbs, Is.EqualTo(before - nameTag.OrbCost));
            Assert.That(player.HasSkill(TimeTrialSkills.NameTag), Is.True);

            Assert.That(
                Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag)),
                Is.EqualTo(ActionResults.SkillAlreadyUnlocked));
            Assert.That(player.Orbs, Is.EqualTo(before - nameTag.OrbCost), "a second purchase must not be charged for");
        }

        [Test]
        public void AnUnknownSkillIsNeverBoughtOrEquipped()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag));

            SkillId nonsense = SkillId.FromString("no-such-skill");
            Assert.That(Execute(player, new PlayerUnlockSkill(nonsense)), Is.EqualTo(ActionResults.NoSuchSkill));
            Assert.That(Execute(player, new PlayerEquipSkill(nonsense)), Is.EqualTo(ActionResults.NoSuchSkill));
        }

        [Test]
        public void OnlyAnOwnedMovementSkillGoesInTheSlot()
        {
            PlayerModel player = CreatePlayerModel();
            SkillId sandals = SkillId.FromString("shoe-a");
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag, sandals));

            Assert.That(Execute(player, new PlayerEquipSkill(sandals)), Is.EqualTo(ActionResults.SkillNotUnlocked));

            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));
            Assert.That(Execute(player, new PlayerEquipSkill(TimeTrialSkills.NameTag)), Is.EqualTo(ActionResults.SkillNotEquippable),
                "the Name Tag is a name, not a loadout");

            Execute(player, new PlayerUnlockSkill(sandals));
            Assert.That(Execute(player, new PlayerEquipSkill(sandals)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.EquippedSkillId, Is.EqualTo(sandals));
            Assert.That(player.ActiveSkillId, Is.EqualTo(sandals));

            Assert.That(Execute(player, new PlayerEquipSkill(null)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.EquippedSkillId, Is.Null, "a null id empties the slot");
            Assert.That(player.ActiveSkillId, Is.Null);
        }

        [Test]
        public void BuyingAMovementSkillPutsItInTheSlot()
        {
            PlayerModel player = CreatePlayerModel();
            SkillId sandals = SkillId.FromString("shoe-a");
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag, sandals));

            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));
            Assert.That(Execute(player, new PlayerUnlockSkill(sandals)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.EquippedSkillId, Is.EqualTo(sandals), "what was just bought is what the next run is played with");
            Assert.That(player.ActiveSkillId, Is.EqualTo(sandals));
        }

        [Test]
        public void BuyingASecondMovementSkillReplacesTheFirstInTheSlot()
        {
            PlayerModel player = CreatePlayerModel();
            SkillId sandals = SkillId.FromString("shoe-a");
            SkillId guard   = SkillId.FromString("shield");
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag, sandals, guard));

            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));
            Execute(player, new PlayerUnlockSkill(sandals));
            Assert.That(Execute(player, new PlayerUnlockSkill(guard)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.ActiveSkillId, Is.EqualTo(guard));
            Assert.That(player.HasSkill(sandals), Is.True, "the replaced Skill stays owned");
            Assert.That(Execute(player, new PlayerEquipSkill(sandals)), Is.EqualTo(MetaActionResult.Success),
                "an owned Skill can still be switched back into the slot");
            Assert.That(player.ActiveSkillId, Is.EqualTo(sandals));
        }

        [Test]
        public void BuyingTheNameTagLeavesAnEmptySlotEmpty()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag));

            Assert.That(Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.EquippedSkillId, Is.Null, "the Name Tag is a name, not a loadout");
            Assert.That(player.ActiveSkillId, Is.Null);
        }

        /// <summary>
        /// The Name Tag is the root of the tree, so it is always owned before anything could be in the slot. A
        /// player can still reach an occupied slot without it (a published tree may move) and the Name Tag's
        /// purchase must leave that slot alone.
        /// </summary>
        [Test]
        public void BuyingTheNameTagLeavesAnOccupiedSlotAlone()
        {
            PlayerModel player = CreatePlayerModel();
            SkillId sandals = SkillId.FromString("shoe-a");
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag, sandals));

            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));
            Execute(player, new PlayerUnlockSkill(sandals));
            player.UnlockedSkills.Remove(TimeTrialSkills.NameTag);
            Assert.That(player.ActiveSkillId, Is.EqualTo(sandals));

            Assert.That(Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.EquippedSkillId, Is.EqualTo(sandals));
            Assert.That(player.ActiveSkillId, Is.EqualTo(sandals));
        }

        /// <summary>
        /// The slot is read when the attempt opens and never again, so the server recomputes the recording
        /// against the Skill the run was played on.
        /// </summary>
        [Test]
        public void TheTicketCarriesTheSkillTheRunWasOpenedWith()
        {
            PlayerModel player = CreatePlayerModel();
            SkillId sandals = SkillId.FromString("shoe-a");
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag, sandals));

            Execute(player, new PlayerStartPracticeTimeTrial(PracticeMapId));
            Assert.That(player.ActiveTimeTrial.SkillId, Is.Null, "nothing is equipped yet");

            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));
            Execute(player, new PlayerUnlockSkill(sandals));
            Execute(player, new PlayerEquipSkill(sandals));

            Execute(player, new PlayerStartPracticeTimeTrial(PracticeMapId));
            TimeTrialTicket ticket = player.ActiveTimeTrial;
            Assert.That(ticket.SkillId, Is.EqualTo(sandals));

            // Changing the slot mid-run leaves the open attempt's Skill exactly where it was.
            Execute(player, new PlayerEquipSkill(null));
            Assert.That(player.ActiveTimeTrial.SkillId, Is.EqualTo(sandals));

            TimeTrialRules ticketRules = TimeTrialSkills.EffectiveRules(player.GameConfig, ticket.SkillId);
            Assert.That(ticketRules.RunSpeed, Is.GreaterThan(_gameConfig.TimeTrialRules.RunSpeed));
        }

        #endregion

        #region The Name Tag and the player name

        [Test]
        public void BuyingTheNameTagLeavesThePlayerAMaskedNinjaUntilTheyTypeAName()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag));

            Assert.That(player.PlayerName, Is.EqualTo(TimeTrialSkills.MaskedNinjaName), "every player starts as a Masked Ninja, whatever name the account was created with");
            Assert.That(player.HasName, Is.False);
            Assert.That(player.GhostDisplayName, Is.EqualTo(TimeTrialSkills.MaskedNinjaName));

            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));

            Assert.That(player.PlayerName, Is.EqualTo(TimeTrialSkills.MaskedNinjaName), "the Name Tag hands out no name of its own");
            Assert.That(player.HasName, Is.False);
            Assert.That(player.GhostDisplayName, Is.EqualTo(TimeTrialSkills.MaskedNinjaName));
        }

        [Test]
        public void ANameCannotBeSetWithoutTheNameTag()
        {
            PlayerModel player = CreatePlayerModel();

            Assert.That(Execute(player, new PlayerSetName("Kaito")), Is.EqualTo(ActionResults.NameTagNotOwned));
            Assert.That(player.PlayerName, Is.EqualTo(TimeTrialSkills.MaskedNinjaName));
            Assert.That(player.GhostDisplayName, Is.EqualTo(TimeTrialSkills.MaskedNinjaName));
        }

        [Test]
        public void ANameTagOwnerIsKnownByTheNameTheyTyped()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag));
            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));

            Assert.That(Execute(player, new PlayerSetName("  Kaito  ")), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.PlayerName, Is.EqualTo("Kaito"), "the name is stored trimmed");
            Assert.That(player.HasName, Is.True);
            Assert.That(player.GhostDisplayName, Is.EqualTo("Kaito"));
        }

        [Test]
        public void ANameCanBeSetAgainAndChanged()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag));
            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));

            Assert.That(Execute(player, new PlayerSetName("Kaito")), Is.EqualTo(MetaActionResult.Success));
            Assert.That(Execute(player, new PlayerSetName("Kaito")), Is.EqualTo(MetaActionResult.Success), "the same name twice");
            Assert.That(Execute(player, new PlayerSetName("Old Crow")), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.GhostDisplayName, Is.EqualTo("Old Crow"));
        }

        [Test]
        public void AnEmojiNameIsAccepted()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag));
            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));

            Assert.That(Execute(player, new PlayerSetName("🦊 Kaito 🇫🇮")), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.PlayerName, Is.EqualTo("🦊 Kaito 🇫🇮"));
            Assert.That(player.GhostDisplayName, Is.EqualTo("🦊 Kaito 🇫🇮"));
        }

        [TestCase("   ",                 TestName = "OnlySpaces")]
        [TestCase("Masked Ninja",        TestName = "TheHouseName")]
        public void AnInvalidPlayerNameIsRefused(string name)
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, CostOf(TimeTrialSkills.NameTag));
            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));

            Assert.That(Execute(player, new PlayerSetName(name)), Is.EqualTo(ActionResults.InvalidPlayerName));
            Assert.That(player.PlayerName, Is.EqualTo(TimeTrialSkills.MaskedNinjaName));
            Assert.That(player.GhostDisplayName, Is.EqualTo(TimeTrialSkills.MaskedNinjaName));
        }

        [Test]
        public void ThePlayerNameRulesAreTheOnesTheClientChecksInputAgainst()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PlayerModel.IsValidPlayerName("K"), Is.True, "one character");
                Assert.That(PlayerModel.IsValidPlayerName("ABCDEFGHIJKLMNOPQR"), Is.True, "eighteen characters");
                Assert.That(PlayerModel.IsValidPlayerName("O'Neil_the-2nd. Jr"), Is.True, "every allowed punctuation mark");
                Assert.That(PlayerModel.IsValidPlayerName("Ylläpitäjä"), Is.True, "letters beyond ASCII");
                Assert.That(PlayerModel.IsValidPlayerName("🦊"), Is.True, "one emoji");
                Assert.That(PlayerModel.IsValidPlayerName("Kaito ⚡"), Is.True, "an emoji from the Basic Multilingual Plane");
                Assert.That(PlayerModel.IsValidPlayerName("🇫🇮 Kaito"), Is.True, "a flag, two regional indicators");
                Assert.That(PlayerModel.IsValidPlayerName("👨‍👩‍👧"), Is.True, "a family joined with zero-width joiners");
                Assert.That(PlayerModel.IsValidPlayerName("👍🏽"), Is.True, "a skin tone");
                Assert.That(PlayerModel.IsValidPlayerName("☠️"), Is.True, "an emoji presentation selector");
                Assert.That(PlayerModel.IsValidPlayerName("1️⃣"), Is.True, "a keycap on a digit");
                Assert.That(PlayerModel.IsValidPlayerName("🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊"), Is.True, "eighteen emoji, which is far more than eighteen code units");
                Assert.That(PlayerModel.PlayerNameLength("👨‍👩‍👧 🇫🇮"), Is.EqualTo(3), "a family, a space and a flag are three characters");

                Assert.That(PlayerModel.IsValidPlayerName(""), Is.False);
                Assert.That(PlayerModel.IsValidPlayerName(null), Is.False);
                Assert.That(PlayerModel.IsValidPlayerName("ABCDEFGHIJKLMNOPQRS"), Is.False, "nineteen characters");
                Assert.That(PlayerModel.IsValidPlayerName("🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊🦊"), Is.False, "nineteen emoji");
                Assert.That(PlayerModel.IsValidPlayerName(" Kaito"), Is.False, "a leading space");
                Assert.That(PlayerModel.IsValidPlayerName("Kaito "), Is.False, "a trailing space");
                Assert.That(PlayerModel.IsValidPlayerName("Kai<b>"), Is.False, "a disallowed character");
                Assert.That(PlayerModel.IsValidPlayerName("MASKED NINJA"), Is.False, "the house name in any case");
                Assert.That(PlayerModel.IsValidPlayerName("‍"), Is.False, "a zero-width joiner alone is invisible");
                Assert.That(PlayerModel.IsValidPlayerName("Kai‍to"), Is.False, "a joiner after a letter joins nothing");
                Assert.That(PlayerModel.IsValidPlayerName("🏽"), Is.False, "a skin tone with nothing to tint");
                Assert.That(PlayerModel.IsValidPlayerName("Kai​to"), Is.False, "a zero-width space");
                Assert.That(PlayerModel.IsValidPlayerName("Kai‮to"), Is.False, "a bidirectional override");
                Assert.That(PlayerModel.IsValidPlayerName("\uD83E"), Is.False, "half of a surrogate pair");
            });
        }

        #endregion

        #region The Name Tag tutorial

        [Test]
        public void APlayerWhoCannotAffordTheNameTagIsLeftAlone()
        {
            PlayerModel player = CreatePlayerModel();

            Assert.That(player.Orbs, Is.Zero, "a new player has earned nothing yet");
            Assert.That(player.NameTagTutorialStep, Is.EqualTo(NameTagTutorialStep.None));
        }

        [Test]
        public void OrbsThatCoverTheNameTagPointThePlayerAtIt()
        {
            PlayerModel player  = CreatePlayerModel();
            SkillInfo   nameTag = _gameConfig.Skills[TimeTrialSkills.NameTag];

            EarnAtLeast(player, nameTag.OrbCost);

            Assert.That(player.NameTagTutorialStep, Is.EqualTo(NameTagTutorialStep.BuyNameTag));
        }

        [Test]
        public void OwningTheNameTagWithoutANameAsksForOne()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, _gameConfig.Skills[TimeTrialSkills.NameTag].OrbCost);

            Assert.That(Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.NameTagTutorialStep, Is.EqualTo(NameTagTutorialStep.SetName));
        }

        [Test]
        public void ANamedPlayerHasFinishedTheTutorial()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, _gameConfig.Skills[TimeTrialSkills.NameTag].OrbCost);
            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));

            Assert.That(Execute(player, new PlayerSetName("Kaito")), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.NameTagTutorialStep, Is.EqualTo(NameTagTutorialStep.None));
        }

        [Test]
        public void ARefusedNameLeavesThePlayerOnTheNamingStep()
        {
            PlayerModel player = CreatePlayerModel();
            EarnAtLeast(player, _gameConfig.Skills[TimeTrialSkills.NameTag].OrbCost);
            Execute(player, new PlayerUnlockSkill(TimeTrialSkills.NameTag));

            Assert.That(Execute(player, new PlayerSetName(TimeTrialSkills.MaskedNinjaName)), Is.EqualTo(ActionResults.InvalidPlayerName));

            Assert.That(player.NameTagTutorialStep, Is.EqualTo(NameTagTutorialStep.SetName));
        }

        #endregion

        #region The pool best

        /// <summary>
        /// Runs a nameless player made were never recorded into a pool, so the best the server reports to the
        /// pool is kept apart from the personal best: it moves only when a run comes back with a standing.
        /// </summary>
        [Test]
        public void OnlyARunRecordedIntoThePoolMovesThePoolBest()
        {
            PlayerModel player = CreatePlayerModel();
            CourseId    courseId = PracticeMapId;

            Execute(player, new PlayerOpenTimeTrial(courseId, lineup: null));
            TimeTrialResult nameless = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(TimeTrialSegments.CountFor(PracticeMap.Climbs.Count), 20_000), climbMs: 5_000);
            Execute(player, new PlayerAcceptTimeTrialResult(player.ActiveTimeTrial.TicketId, nameless, standing: null));

            Assert.That(player.GetBestResult(courseId), Is.Not.Null);
            Assert.That(player.GetPoolBestTimeMs(courseId), Is.Zero, "a run with no standing never reached the pool");
            Assert.That(player.IsNewPoolBest(courseId, nameless.FinishTimeMs + 1_000), Is.True,
                "a slower run than the personal best is still the player's first on the board");

            Execute(player, new PlayerOpenTimeTrial(courseId, lineup: null));
            TimeTrialResult named = TimeTrialRuns.ResultOf(TimeTrialRuns.EvenSegments(TimeTrialSegments.CountFor(PracticeMap.Climbs.Count), 25_000), climbMs: 5_000);
            TimeTrialStanding standing = new TimeTrialStanding(courseId, named.FinishTimeMs, named.FinishTimeMs);
            Execute(player, new PlayerAcceptTimeTrialResult(player.ActiveTimeTrial.TicketId, named, standing));

            Assert.That(player.GetBestResult(courseId).FinishTimeMs, Is.EqualTo(nameless.FinishTimeMs), "the personal best stands");
            Assert.That(player.GetPoolBestTimeMs(courseId), Is.EqualTo(named.FinishTimeMs));
            Assert.That(player.IsNewPoolBest(courseId, named.FinishTimeMs), Is.False, "an equal time is not a new best");
        }

        #endregion
    }
}
