using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests <see cref="DisplayNamePolicy"/>, the display-name rules that the server enforces, and the admin
    /// validator built on them. The rules are shared code so they can be tested without a server.
    /// </summary>
    [TestFixture]
    public class DisplayNamePolicyTests
    {
        static readonly MetaTime Now = MetaTime.FromDateTime(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

        /// <summary>The player's current name in the rename checks.</summary>
        const string Current = "Old Name";

        /// <summary>
        /// The bot names the checks reserve. Bot names are game config, so the policy takes a roster as an
        /// argument, and this fixture stands in for the published one.
        /// </summary>
        static readonly BotNameRoster Roster = TestBotConfig.Roster;

        static DisplayNameRefusal Validate(string name) => DisplayNamePolicy.Validate(name, Roster);

        static DisplayNameRefusal ValidateRename(string name, string currentName, MetaTime lastRenamedAt, MetaTime now) =>
            DisplayNamePolicy.ValidateRename(name, currentName, lastRenamedAt, now, Roster);

        #region Length

        [Test]
        public void ANameIsBoundedAtBothEnds()
        {
            Assert.That(Validate("ab"), Is.EqualTo(DisplayNameRefusal.TooShort));
            Assert.That(Validate(new string('a', DisplayNamePolicy.MaxLength + 1)), Is.EqualTo(DisplayNameRefusal.TooLong));

            Assert.That(Validate(new string('a', DisplayNamePolicy.MinLength)), Is.EqualTo(DisplayNameRefusal.None));
            Assert.That(Validate(new string('a', DisplayNamePolicy.MaxLength)), Is.EqualTo(DisplayNameRefusal.None));
        }

        [Test]
        public void LengthIsMeasuredAfterTrimming()
        {
            // Leading and trailing spaces are trimmed before the length check, so padding neither makes a short
            // name long enough nor makes a maximum-length name too long.
            Assert.That(Validate("  ab  "), Is.EqualTo(DisplayNameRefusal.TooShort));
            Assert.That(Validate("  " + new string('a', DisplayNamePolicy.MaxLength) + "  "), Is.EqualTo(DisplayNameRefusal.None));
        }

        [Test]
        public void ANameIsBoundedInCodeUnitsAsWellAsInCharacters()
        {
            // "Bob" followed by a thousand combining accents counts as three characters and every mark is
            // attached to a letter, but the string is over a thousand code units. MaxRawLength bounds the stored
            // string in code units.
            string padded = "Bob" + new string('́', 1_000);

            Assert.That(DisplayNamePolicy.CountCharacters(padded), Is.EqualTo(3), "this fixture is meant to pass the character bound");
            Assert.That(Validate(padded), Is.EqualTo(DisplayNameRefusal.TooLong));

            // A name at exactly MaxRawLength code units is accepted, and one code unit more is refused.
            string atTheBound = "Bob" + new string('́', DisplayNamePolicy.MaxRawLength - 3);
            Assert.That(atTheBound, Has.Length.EqualTo(DisplayNamePolicy.MaxRawLength));
            Assert.That(Validate(atTheBound), Is.EqualTo(DisplayNameRefusal.None));
            Assert.That(Validate(atTheBound + "́"), Is.EqualTo(DisplayNameRefusal.TooLong));
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("      ")]
        public void AnEmptyOrNullNameIsTooShortRatherThanACrash(string name)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.TooShort));
        }

        #endregion

        #region Character set

        [TestCase("Anna-Liisa")]
        [TestCase("O'Brien")]
        [TestCase("snake_case")]
        [TestCase("Player 7")]
        // Letters from any script are permitted, not only Latin letters.
        [TestCase("Ёлка")]
        [TestCase("さくら")]
        public void ThePermittedSetIsLettersDigitsAndFourPunctuationMarks(string name)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.None));
        }

        /// <summary>
        /// The rule is a permitted set rather than a forbidden list, so characters nobody listed are refused too.
        /// </summary>
        [TestCase("bad\u0007name", "a control character")]
        [TestCase("bad‮name", "a bidirectional override")]
        [TestCase("bad‍name", "a zero-width joiner")]
        [TestCase("drop;table", "a semicolon")]
        [TestCase("<script>x", "markup")]
        public void TheCharactersNobodyThinksOfAreRefused(string name, string what)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.IllegalCharacter), what);
        }

        [TestCase("  Two   Words  ", "Two Words")]
        [TestCase("solo", "solo")]
        [TestCase(null, "")]
        public void RunsOfSpacesAreCollapsedRatherThanRefused(string name, string stored)
        {
            Assert.That(DisplayNamePolicy.ToStoredForm(name), Is.EqualTo(stored));
        }

        /// <summary>
        /// Only U+0020 is trimmed. Every other whitespace character is outside the permitted set and refused, so a
        /// name cannot contain a character that looks like a space but compares differently.
        /// </summary>
        [TestCase("Two\tWords", "a tab")]
        [TestCase("Two Words", "a no-break space")]
        [TestCase("Two　Words", "an ideographic space")]
        public void OnlyTheOrdinarySpaceIsTrimmed(string name, string what)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.IllegalCharacter), what);
        }

        [Test]
        public void LengthIsCountedTheWayAReaderCountsIt()
        {
            // Length is counted in grapheme clusters, not UTF-16 code units. A letter with a combining mark takes
            // the width of one character on a seat plaque, and the length bound exists to fit the names on screen.
            Assert.That(DisplayNamePolicy.CountCharacters("abc"), Is.EqualTo(3));
            Assert.That(DisplayNamePolicy.CountCharacters("é"), Is.EqualTo(1), "a letter and its accent are one character");
            Assert.That(DisplayNamePolicy.CountCharacters(null), Is.Zero);
            Assert.That(DisplayNamePolicy.CountCharacters(string.Empty), Is.Zero);

            // MaxLength accented letters are accepted, although they take twice as many code units.
            string sixteenAccented = string.Concat(Enumerable.Repeat("é", DisplayNamePolicy.MaxLength));
            Assert.That(sixteenAccented, Has.Length.EqualTo(DisplayNamePolicy.MaxLength * 2), "this fixture is meant to be longer in code units than in characters");
            Assert.That(Validate(sixteenAccented), Is.EqualTo(DisplayNameRefusal.None));

            string seventeenAccented = string.Concat(Enumerable.Repeat("é", DisplayNamePolicy.MaxLength + 1));
            Assert.That(Validate(seventeenAccented), Is.EqualTo(DisplayNameRefusal.TooLong));
        }

        #endregion

        #region At least one letter

        [TestCase("12345", DisplayNameRefusal.MissingLetter)]
        [TestCase("1-2-3", DisplayNameRefusal.MissingLetter)]
        // One letter is enough to pass this rule.
        [TestCase("a12", DisplayNameRefusal.None)]
        public void ANameNeedsALetterInIt(string name, DisplayNameRefusal expected)
        {
            Assert.That(Validate(name), Is.EqualTo(expected));
        }

        /// <summary>Punctuation-only names are refused. Which rule refuses them does not matter here.</summary>
        [TestCase("---")]
        [TestCase("-'-'-'")]
        [TestCase("_ _ _")]
        public void PunctuationAloneNeverPasses(string name)
        {
            Assert.That(Validate(name), Is.Not.EqualTo(DisplayNameRefusal.None));
        }

        #endregion

        #region Where punctuation may sit

        [TestCase("-Bobby", DisplayNameRefusal.InvalidPunctuation)]
        [TestCase("Bobby-", DisplayNameRefusal.InvalidPunctuation)]
        [TestCase("_Bobby", DisplayNameRefusal.InvalidPunctuation)]
        [TestCase("Bobby'", DisplayNameRefusal.InvalidPunctuation)]
        [TestCase("Bob--by", DisplayNameRefusal.InvalidPunctuation)]
        [TestCase("Bob -by", DisplayNameRefusal.InvalidPunctuation)]
        // Common real names with punctuation still pass.
        [TestCase("Jean-Luc P", DisplayNameRefusal.None)]
        public void PunctuationGoesBetweenLettersOneAtATime(string name, DisplayNameRefusal expected)
        {
            Assert.That(Validate(name), Is.EqualTo(expected));
        }

        [Test]
        public void AReservedNameIsCalledReservedEvenWhenItsPunctuationIsAlsoWrong()
        {
            // The reservation is checked before punctuation placement, because fixing the punctuation would not
            // make a reserved name available.
            Assert.That(Validate("Cog--wheel"), Is.EqualTo(DisplayNameRefusal.ReservedName));
        }

        #endregion

        #region Combining marks

        [TestCase("José", "a decomposed accent")]
        [TestCase("José", "a precomposed one")]
        [TestCase("नमस्ते", "a script written with combining marks")]
        public void ALetterMayCarryItsMarks(string name, string what)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.None), what);
        }

        /// <summary>A combining mark that follows no letter renders on a dotted circle, so it is refused.</summary>
        [TestCase("́abc", "leading")]
        [TestCase("ab-́c", "after punctuation")]
        public void AMarkWithNothingToAttachToIsRefused(string name, string what)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.IllegalCharacter), what);
        }

        [Test]
        public void MarksDoNotGetRoundTheReservation()
        {
            // An accent on a letter looks almost the same but compares as a different string, so the reservation
            // check ignores combining marks.
            Assert.That(Validate("Cogwheél"), Is.EqualTo(DisplayNameRefusal.ReservedName));
        }

        [TestCase("Bob\U0001F600")]
        [TestCase("\U0001F600\U0001F600\U0001F600")]
        public void EmojiAreNotLetters(string name)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.IllegalCharacter));
        }

        #endregion

        #region The reserved roster

        [Test]
        public void ANameOnTheBotRosterIsReserved()
        {
            // A player must not be able to pose as a bot or be mistaken for one (docs/player.md, "Name rules").
            foreach (string reserved in TestBotConfig.Names)
                Assert.That(Validate(reserved), Is.EqualTo(DisplayNameRefusal.ReservedName), $"'{reserved}' was not reserved");
        }

        /// <summary>A reservation that matched only the exact string could be bypassed with one changed keystroke.</summary>
        [TestCase("cogwheel")]
        [TestCase("COGWHEEL")]
        [TestCase("Cog Wheel")]
        [TestCase("C o g w h e e l")]
        [TestCase("Cog-wheel")]
        [TestCase("Cog_wheel")]
        [TestCase("Cog'wheel")]
        // A roster name with its hyphen typed as a space.
        [TestCase("Ember 9")]
        public void TheReservationSurvivesCaseAndPunctuation(string name)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.ReservedName));
        }

        /// <summary>
        /// Checks every roster name against every punctuation edit that the permitted set allows. A punctuation
        /// mark added to <see cref="DisplayNamePolicy.IsPermittedCharacter"/> but not removed by
        /// <see cref="DisplayNamePolicy.ToComparisonKey"/> fails this test.
        /// </summary>
        [Test]
        public void NoPunctuationEditOfARosterNameEscapesTheReservation()
        {
            // The characters besides letters and digits that a name may contain.
            char[] separators = { ' ', '-', '_', '\'' };

            foreach (string reserved in TestBotConfig.Names)
            {
                foreach (string variant in PunctuationVariantsOf(reserved, separators))
                {
                    Assert.That(Validate(variant), Is.EqualTo(DisplayNameRefusal.ReservedName),
                        $"'{variant}' was not refused, so '{reserved}' is a reservation one keystroke wide");
                }
            }
        }

        /// <summary>
        /// Returns the punctuation variants of <paramref name="name"/>: each existing mark replaced by each
        /// separator, each mark removed, and each separator inserted at every interior position. Letters and
        /// digits are never changed.
        /// </summary>
        static List<string> PunctuationVariantsOf(string name, char[] separators)
        {
            List<string> variants = new List<string>();

            for (int index = 0; index < name.Length; index++)
            {
                if (!char.IsLetterOrDigit(name[index]))
                {
                    // An existing mark: replace it with each separator, and remove it.
                    foreach (char separator in separators)
                        variants.Add(name.Substring(0, index) + separator + name.Substring(index + 1));
                    variants.Add(name.Substring(0, index) + name.Substring(index + 1));
                    continue;
                }

                // Interior positions only. ToStoredForm trims leading and trailing spaces, and
                // RunsOfSpacesAreCollapsedRatherThanRefused tests that.
                if (index == 0)
                    continue;
                foreach (char separator in separators)
                    variants.Add(name.Substring(0, index) + separator + name.Substring(index));
            }

            return variants;
        }

        /// <summary>The reservation matches only listed names, not names that look similar.</summary>
        [TestCase("Cogwheels")]
        [TestCase("Robot")]
        public void ANameThatMerelyResemblesABotIsAllowed(string name)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.None));
        }

        /// <summary>
        /// The fallback guest name must pass the rules, or the server would refuse the name it gave a player if the
        /// player submitted it.
        /// </summary>
        [TestCase("Guest 0000")]
        [TestCase("Guest 9999")]
        [TestCase("Guest 12345")]
        public void TheFallbackGuestNameIsAcceptable(string name)
        {
            Assert.That(Validate(name), Is.EqualTo(DisplayNameRefusal.None));
        }

        [Test]
        public void ANameThatClaimsToBeTheGameIsReserved()
        {
            // A player named after the game or its staff, such as Support, could impersonate an official
            // channel.
            foreach (string reserved in DisplayNamePolicy.ReservedNames)
                Assert.That(Validate(reserved), Is.EqualTo(DisplayNameRefusal.ReservedName), $"'{reserved}' was not reserved");

            Assert.That(Validate("admin"), Is.EqualTo(DisplayNameRefusal.ReservedName));
            Assert.That(Validate("Ad-min"), Is.EqualTo(DisplayNameRefusal.ReservedName));
            Assert.That(Validate("m e t a p l a y"), Is.EqualTo(DisplayNameRefusal.ReservedName));

            // Names that only contain a reserved name are allowed.
            Assert.That(Validate("Admiral"), Is.EqualTo(DisplayNameRefusal.None));
            Assert.That(Validate("Supporter"), Is.EqualTo(DisplayNameRefusal.None));
        }

        [Test]
        public void EveryReservedNameCouldHaveBeenTypedInTheFirstPlace()
        {
            // Every reserved name must pass the length bounds, so a test that expects ReservedName is refused for
            // the reservation and not for its length.
            foreach (string reserved in DisplayNamePolicy.ReservedNames.Concat(TestBotConfig.Names))
            {
                Assert.That(DisplayNamePolicy.CountCharacters(reserved), Is.InRange(DisplayNamePolicy.MinLength, DisplayNamePolicy.MaxLength),
                    $"'{reserved}' could not be typed as a name in the first place");
            }
        }

        #endregion

        #region The admin path

        [Test]
        public void TheAdminPathBoundsTheRawStringAndNotOnlyTheCanonicalOne()
        {
            // A dashboard rename goes through the SDK, which calls this validator and then stores the string
            // unchanged. The game's own rename path stores the canonical form, but on this path a name that is
            // short only after canonicalization would be stored at its full raw length.
            TableStakesPlayerRequirementsValidator validator = new TableStakesPlayerRequirementsValidator();

            string padded = "ab" + new string(' ', 500) + "c";
            Assert.That(DisplayNamePolicy.ToStoredForm(padded), Has.Length.EqualTo(4), "the padding did not collapse, so this tests nothing");
            Assert.That(validator.ValidatePlayerName(padded), Is.False, "a 503-character name was accepted because it canonicalizes to four");

            // Names within the raw length bound that pass the ordinary rules are accepted.
            Assert.That(validator.ValidatePlayerName("Anna-Liisa"), Is.True);
            Assert.That(validator.ValidatePlayerName(new string('a', DisplayNamePolicy.MaxLength)), Is.True);
            Assert.That(validator.ValidatePlayerName(new string('a', DisplayNamePolicy.MaxLength + 1)), Is.False);

            // The ordinary rules, including the reserved game names, still apply on this path.
            Assert.That(validator.ValidatePlayerName("Sup-port"), Is.False, "a name claiming to be the game reached the admin path");
            Assert.That(validator.ValidatePlayerName("ab"), Is.False);
            Assert.That(validator.ValidatePlayerName(null), Is.False);

            // The validator reports the game's length bounds instead of the SDK's defaults, so the dashboard
            // shows the bounds that are enforced.
            Assert.That(validator.MinPlayerNameLength, Is.EqualTo(DisplayNamePolicy.MinLength));
            Assert.That(validator.MaxPlayerNameLength, Is.EqualTo(DisplayNamePolicy.MaxLength));
        }

        [Test]
        public void TheAdminPathRefusesAComputerPlayersNameWhereverTheRosterCanBeReached()
        {
            // Bot names are game config, which the SDK does not pass to the validator. The server's subclass
            // supplies the active config's roster, and ValidatorWithRoster stands in for it here. The base
            // validator has no roster, so it accepts the same name, which shows that the roster causes the
            // refusal.
            TableStakesPlayerRequirementsValidator withoutRoster = new TableStakesPlayerRequirementsValidator();
            ValidatorWithRoster                   withRoster    = new ValidatorWithRoster();

            Assert.That(withRoster.ValidatePlayerName("Cog-wheel"), Is.False, "a computer player's name reached the admin path");
            Assert.That(withRoster.ValidatePlayerName("Anna-Liisa"), Is.True, "an ordinary name was refused");
            Assert.That(withoutRoster.ValidatePlayerName("Cog-wheel"), Is.True, "the base validator answered as though it held a roster");
        }

        /// <summary>A validator that reserves the fixture roster's names, as the server's subclass does with the published roster.</summary>
        internal class ValidatorWithRoster : TableStakesPlayerRequirementsValidator
        {
            protected override BotNameRoster ReservedBotNames() => Roster;
        }

        #endregion

        #region The rate limit

        [Test]
        public void ARenameInsideTheCooldownIsRefused()
        {
            MetaTime lastRenamedAt = Now;

            Assert.That(ValidateRename("Fresh Name", Current, lastRenamedAt, Now), Is.EqualTo(DisplayNameRefusal.TooSoon));
            Assert.That(ValidateRename("Fresh Name", Current, lastRenamedAt, Now + DisplayNamePolicy.RenameCooldown - MetaDuration.FromSeconds(1)),
                Is.EqualTo(DisplayNameRefusal.TooSoon));
            Assert.That(ValidateRename("Fresh Name", Current, lastRenamedAt, Now + DisplayNamePolicy.RenameCooldown),
                Is.EqualTo(DisplayNameRefusal.None));
        }

        [Test]
        public void APlayerWhoHasNeverRenamedIsNotRateLimited()
        {
            Assert.That(ValidateRename("Fresh Name", Current, MetaTime.Epoch, Now), Is.EqualTo(DisplayNameRefusal.None));
        }

        /// <summary>
        /// A resubmitted or replayed rename of the current name is refused as Unchanged rather than accepted again.
        /// Both names are canonicalized before the comparison, so extra spaces are not a change.
        /// </summary>
        [TestCase("Old Name", DisplayNameRefusal.Unchanged)]
        [TestCase("  Old   Name  ", DisplayNameRefusal.Unchanged)]
        // A change of case is a real rename.
        [TestCase("old name", DisplayNameRefusal.None)]
        public void ANameThatIsAlreadyYoursIsNotAChange(string name, DisplayNameRefusal expected)
        {
            Assert.That(ValidateRename(name, Current, MetaTime.Epoch, Now), Is.EqualTo(expected));
        }

        /// <summary>
        /// A name is judged before the cooldown is checked, so the player is not told to wait and then refused for
        /// a different reason. For an unchanged name, the player would otherwise be told to wait for a rename that
        /// could never succeed.
        /// </summary>
        [TestCase("Old Name", DisplayNameRefusal.Unchanged)]
        [TestCase("ab", DisplayNameRefusal.TooShort)]
        [TestCase("Cogwheel", DisplayNameRefusal.ReservedName)]
        public void TheNameIsJudgedBeforeTheClock(string name, DisplayNameRefusal expected)
        {
            Assert.That(ValidateRename(name, Current, Now, Now), Is.EqualTo(expected));
        }

        [Test]
        public void EveryRefusalHasSomethingToSay()
        {
            // A refused rename must tell the player why, so every refusal reason needs a message.
            foreach (DisplayNameRefusal refusal in Enum.GetValues<DisplayNameRefusal>())
            {
                if (refusal == DisplayNameRefusal.None)
                    continue;
                Assert.That(DisplayNamePolicy.DescribeRefusal(refusal), Is.Not.Empty, $"{refusal} has no message");
            }
        }

        [Test]
        public void ARefusalSaysWhatIsWrongAndWhatWouldBeRight()
        {
            // Pins the exact messages the rename screen shows. Each message names the rule it enforces so the
            // player knows what to change. A render test can only check that the screen shows a message, not that
            // the message is correct.
            Assert.Multiple(() =>
            {
                Assert.That(DisplayNamePolicy.DescribeRefusal(DisplayNameRefusal.TooShort),
                    Is.EqualTo($"That name is too short — {DisplayNamePolicy.MinLength} characters at least."));
                Assert.That(DisplayNamePolicy.DescribeRefusal(DisplayNameRefusal.TooLong),
                    Is.EqualTo($"That name is too long — {DisplayNamePolicy.MaxLength} characters at most."));
                Assert.That(DisplayNamePolicy.DescribeRefusal(DisplayNameRefusal.IllegalCharacter),
                    Is.EqualTo("Letters, numbers, spaces, hyphens, underscores and apostrophes only."));
                Assert.That(DisplayNamePolicy.DescribeRefusal(DisplayNameRefusal.MissingLetter),
                    Is.EqualTo("A name needs at least one letter in it."));
                Assert.That(DisplayNamePolicy.DescribeRefusal(DisplayNameRefusal.InvalidPunctuation),
                    Is.EqualTo("Hyphens, underscores and apostrophes go between letters, one at a time."));
                Assert.That(DisplayNamePolicy.DescribeRefusal(DisplayNameRefusal.ReservedName),
                    Is.EqualTo("That name is reserved for a computer player or the game itself. Pick another."));
                Assert.That(DisplayNamePolicy.DescribeRefusal(DisplayNameRefusal.Unchanged),
                    Is.EqualTo("That is already your name."));
                Assert.That(DisplayNamePolicy.DescribeRefusal(DisplayNameRefusal.TooSoon),
                    Is.EqualTo($"You just changed your name. Try again in {DisplayNamePolicy.RenameCooldown.Milliseconds / 1000} seconds."));
            });
        }

        #endregion

        #region The roster itself

        [Test]
        public void ATableDrawsDistinctBotNames()
        {
            // A table must never seat two bots with the same name. The test draws every count a table can request
            // over many seeds, because a draw with replacement passes a single-seed test most of the time.
            for (ulong seed = 0; seed < 200; seed++)
            {
                for (int count = 0; count <= MatchRules.NumSeats; count++)
                {
                    List<string> drawn = Roster.Draw(seed, count);
                    Assert.That(drawn, Has.Count.EqualTo(count));
                    Assert.That(drawn, Is.Unique, $"seed {seed} drew a repeated name for {count} seats");
                }
            }
        }

        [Test]
        public void TheWholeRosterCanBeDrawnAtOnce()
        {
            List<string> drawn = Roster.Draw(7UL, Roster.Count);
            Assert.That(drawn, Is.Unique);
            Assert.That(drawn, Is.EquivalentTo(TestBotConfig.Names), "a full draw is a permutation of the roster");
        }

        [Test]
        public void TheDrawIsAFunctionOfItsSeed()
        {
            List<string> fromSeed42      = Roster.Draw(42UL, 3);
            List<string> fromSeed42Again = Roster.Draw(42UL, 3);
            List<string> fromSeed43      = Roster.Draw(43UL, 3);

            Assert.That(fromSeed42Again, Is.EqualTo(fromSeed42));
            Assert.That(fromSeed43, Is.Not.EqualTo(fromSeed42));
        }

        #endregion
    }
}
