using Metaplay.Core.Config;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The shape of the Skill tree that the config build accepts: the shipped Skills sheet builds without a Skill
    /// error, and a sheet that breaks the tree is reported against the rule it breaks.
    /// </summary>
    [TestFixture]
    public class SkillsSheetTests
    {
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
        #region Helpers

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
    }
}
