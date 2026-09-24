using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Logic
{
    /// <summary>
    /// Why a name or rename was refused, so the client can tell the player the reason
    /// (<c>docs/player.md</c>, "Name rules").
    /// </summary>
    [MetaSerializable]
    public enum DisplayNameRefusal
    {
        /// <summary>The name is accepted.</summary>
        None = 0,

        /// <summary>Shorter than <see cref="DisplayNamePolicy.MinLength"/> once trimmed.</summary>
        TooShort = 1,

        /// <summary>Longer than <see cref="DisplayNamePolicy.MaxLength"/> once trimmed.</summary>
        TooLong = 2,

        /// <summary>A character that <see cref="DisplayNamePolicy.IsPermittedCharacter"/> refuses, or a combining mark with no letter or digit before it.</summary>
        IllegalCharacter = 3,

        /// <summary>A reserved bot name, or one of <see cref="DisplayNamePolicy.ReservedNames"/>.</summary>
        ReservedName = 4,

        /// <summary>Renamed too recently. The cooldown is <see cref="DisplayNamePolicy.RenameCooldown"/>.</summary>
        TooSoon = 5,

        /// <summary>The name contains no letter.</summary>
        MissingLetter = 6,

        /// <summary>The name starts or ends with punctuation, or has two punctuation characters in a row.</summary>
        InvalidPunctuation = 7,

        /// <summary>The name is already this player's name.</summary>
        Unchanged = 8,
    }

    /// <summary>
    /// The display name rules, as pure functions.
    /// <para>
    /// The rules are in shared code so the client can give immediate feedback. The server runs the same checks
    /// and refuses an invalid rename regardless of what the client did.
    /// </para>
    /// </summary>
    public static class DisplayNamePolicy
    {
        /// <summary>The shortest allowed name, in characters as counted by <see cref="CountCharacters"/>.</summary>
        public const int MinLength = 3;

        /// <summary>
        /// The longest allowed name, in characters as counted by <see cref="CountCharacters"/>. The table's seat
        /// plaques are sized to fit names of this length on a phone screen, so this is code rather than config.
        /// </summary>
        public const int MaxLength = 16;

        /// <summary>
        /// The maximum length of a stored name in UTF-16 code units. <see cref="MaxLength"/> counts grapheme
        /// clusters, which does not limit storage size: "Bob" followed by a thousand combining marks counts as
        /// three characters. Real grapheme clusters need far fewer than eight code units, so this limit refuses
        /// only abuse. <see cref="TableStakesPlayerRequirementsValidator"/> has the tighter admin limit.
        /// </summary>
        public const int MaxRawLength = MaxLength * 8;

        /// <summary>
        /// Names reserved for the game itself, so a player cannot impersonate staff or the system. They are
        /// compared in the <see cref="ToComparisonKey"/> form, like the reserved bot names, and refused as
        /// <see cref="DisplayNameRefusal.ReservedName"/>.
        /// <para>
        /// These are code rather than config so a config publish cannot release them, and so a name can be
        /// checked against them without a config archive. The reserved bot names are config and are passed in
        /// by the caller. This list is not a profanity filter. The game has none (<c>docs/player.md</c>).
        /// </para>
        /// </summary>
        public static readonly string[] ReservedNames =
        {
            "Admin",
            "Administrator",
            "Moderator",
            "Metaplay",
            "System",
            "Support",
            "TableStakes",
        };

        /// <summary>
        /// The minimum time between two renames. There is no limit on the total number of renames.
        /// </summary>
        public static readonly MetaDuration RenameCooldown = MetaDuration.FromSeconds(30);

        /// <summary>
        /// The stored form of a name: leading and trailing spaces removed, and each run of internal spaces
        /// collapsed to one. Stray spaces are usually typing mistakes, so they are removed rather than refused.
        /// <para>
        /// Only U+0020 is treated as a space. Other whitespace characters, such as tab or no-break space, are not
        /// permitted and <see cref="Validate"/> refuses them as <see cref="DisplayNameRefusal.IllegalCharacter"/>.
        /// </para>
        /// </summary>
        public static string ToStoredForm(string name)
        {
            if (name == null)
                return string.Empty;

            if (IsAlreadyCanonical(name))
                return name;

            StringBuilder builder = new StringBuilder(name.Length);
            bool pendingSpace = false;
            foreach (char ch in name)
            {
                if (ch == ' ')
                {
                    // Defer writing the space, so a run of spaces produces one space and trailing spaces
                    // produce none. Leading spaces are dropped because the builder is still empty.
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                builder.Append(ch);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Whether <paramref name="name"/> has no leading, trailing or doubled space, so <see cref="ToStoredForm"/>
        /// can return it unchanged. This avoids building a new string for most names, which matters when the
        /// config build validates every generated name.
        /// </summary>
        static bool IsAlreadyCanonical(string name)
        {
            if (name.Length == 0)
                return true;
            if (name[0] == ' ' || name[name.Length - 1] == ' ')
                return false;

            for (int index = 1; index < name.Length; index++)
            {
                if (name[index] == ' ' && name[index - 1] == ' ')
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The form used to decide whether two names are the same name: lower-cased, with every separator
        /// (<see cref="IsSeparator"/>) and every combining mark removed, so a player cannot get around a reserved
        /// name by adding or changing punctuation or accents. Two names match when their letters and digits match.
        /// Look-alike characters from other scripts are not matched (<c>docs/player.md</c>, "Name rules").
        /// </summary>
        public static string ToComparisonKey(string name)
        {
            if (name == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder(name.Length);
            foreach (char ch in name)
            {
                if (IsSeparator(ch) || IsCombiningMark(ch))
                    continue;
                builder.Append(char.ToLowerInvariant(ch));
            }
            return builder.ToString();
        }

        /// <summary>
        /// The number of grapheme clusters in <paramref name="name"/>, not UTF-16 code units. A letter with two
        /// combining marks counts as one character, as it appears on screen.
        /// </summary>
        public static int CountCharacters(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            // Fast path without allocation: with no surrogate and no combining mark, each code unit is one
            // character. The fast path can miscount only names with CRLF or joined emoji, whose characters
            // Validate refuses anyway, so at most the refusal reason differs.
            bool needsGraphemeCount = false;
            foreach (char ch in name)
            {
                if (char.IsSurrogate(ch) || IsCombiningMark(ch))
                {
                    needsGraphemeCount = true;
                    break;
                }
            }

            if (!needsGraphemeCount)
                return name.Length;

            int count = 0;
            System.Globalization.TextElementEnumerator elements = System.Globalization.StringInfo.GetTextElementEnumerator(name);
            while (elements.MoveNext())
                count++;
            return count;
        }

        /// <summary>
        /// Whether <paramref name="ch"/> is one of the permitted punctuation characters. <see cref="ToComparisonKey"/>
        /// removes exactly this set, and <see cref="IsPermittedCharacter"/> allows exactly this set besides
        /// letters, digits and combining marks. The two must stay equal, or a permitted character that
        /// <see cref="ToComparisonKey"/> keeps could be used to get around a reserved name.
        /// </summary>
        static bool IsSeparator(char ch) => ch == ' ' || ch == '-' || ch == '_' || ch == '\'';

        /// <summary>
        /// Whether <paramref name="ch"/> is a combining mark, such as an accent or a vowel sign. Many scripts need
        /// them, so they are permitted, but <see cref="Validate"/> allows one only after a letter, a digit or
        /// another combining mark.
        /// </summary>
        static bool IsCombiningMark(char ch)
        {
            System.Globalization.UnicodeCategory category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            return category == System.Globalization.UnicodeCategory.NonSpacingMark
                || category == System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }

        /// <summary>
        /// Whether <paramref name="ch"/> may appear in a name: letters, digits and combining marks from any
        /// script, plus the separators in <see cref="IsSeparator"/>. This is an allow list, so control characters,
        /// bidirectional overrides, zero-width characters and emoji are refused without having to list them.
        /// <see cref="Validate"/> checks the position of a combining mark.
        /// </summary>
        public static bool IsPermittedCharacter(char ch)
        {
            if (char.IsLetterOrDigit(ch))
                return true;
            return IsSeparator(ch) || IsCombiningMark(ch);
        }

        /// <summary>
        /// Checks the name itself. The checks that depend on the player are in <see cref="ValidateRename"/>. The
        /// checks run in an order that returns the most useful reason: for example, a reserved name is reported
        /// as reserved even if its punctuation is also invalid, because fixing the punctuation would not help.
        /// </summary>
        /// <param name="name">The name as typed.</param>
        /// <param name="reservedBotNames">
        /// The reserved bot names from the published config. The name is checked only against this roster, so a
        /// caller without config access must pass <see cref="BotNameRoster.Empty"/> explicitly.
        /// </param>
        public static DisplayNameRefusal Validate(string name, BotNameRoster reservedBotNames)
        {
            string canonical = ToStoredForm(name);

            int length = CountCharacters(canonical);
            if (length < MinLength)
                return DisplayNameRefusal.TooShort;
            if (length > MaxLength)
                return DisplayNameRefusal.TooLong;

            // Limit the stored length too. A name within MaxLength characters can still be arbitrarily long in
            // code units (see MaxRawLength).
            if (canonical.Length > MaxRawLength)
                return DisplayNameRefusal.TooLong;

            bool hasLetter = false;
            for (int index = 0; index < canonical.Length; index++)
            {
                char ch = canonical[index];

                if (IsCombiningMark(ch))
                {
                    // A combining mark must follow a letter, a digit or another combining mark.
                    char previous = index > 0 ? canonical[index - 1] : '\0';
                    if (index == 0 || (!char.IsLetterOrDigit(previous) && !IsCombiningMark(previous)))
                        return DisplayNameRefusal.IllegalCharacter;
                    continue;
                }

                if (!IsPermittedCharacter(ch))
                    return DisplayNameRefusal.IllegalCharacter;

                hasLetter |= char.IsLetter(ch);
            }

            if (!hasLetter)
                return DisplayNameRefusal.MissingLetter;

            if (IsReservedName(canonical, reservedBotNames))
                return DisplayNameRefusal.ReservedName;

            if (HasMisplacedPunctuation(canonical))
                return DisplayNameRefusal.InvalidPunctuation;

            return DisplayNameRefusal.None;
        }

        /// <summary>
        /// Whether <paramref name="name"/> matches a reserved bot name or one of <see cref="ReservedNames"/>. Both
        /// are compared in the <see cref="ToComparisonKey"/> form.
        /// </summary>
        public static bool IsReservedName(string name, BotNameRoster reservedBotNames)
        {
            string normalized = ToComparisonKey(name);
            if (normalized.Length == 0)
                return false;

            if (reservedBotNames != null && reservedBotNames.IsReservedComparisonKey(normalized))
                return true;

            return ReservedNameComparisonKeys.Contains(normalized);
        }

        /// <summary><see cref="ReservedNames"/> in <see cref="ToComparisonKey"/> form, computed once.</summary>
        static readonly HashSet<string> ReservedNameComparisonKeys = BuildNormalizedReservedNames();

        static HashSet<string> BuildNormalizedReservedNames()
        {
            HashSet<string> normalized = new HashSet<string>(StringComparer.Ordinal);
            foreach (string reserved in ReservedNames)
                normalized.Add(ToComparisonKey(reserved));
            return normalized;
        }

        /// <summary>
        /// Whether a separator is at the start or end of <paramref name="canonical"/>, or two separators are
        /// adjacent. <see cref="ToStoredForm"/> has already removed outer spaces, so this catches leading or
        /// trailing hyphens, underscores and apostrophes, and runs of separators.
        /// </summary>
        static bool HasMisplacedPunctuation(string canonical)
        {
            if (canonical.Length == 0)
                return false;

            if (IsSeparator(canonical[0]) || IsSeparator(canonical[canonical.Length - 1]))
                return true;

            for (int index = 1; index < canonical.Length; index++)
            {
                if (IsSeparator(canonical[index]) && IsSeparator(canonical[index - 1]))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks a rename: the name itself, then whether it differs from <paramref name="currentName"/>, then the
        /// cooldown. The unchanged check runs before the cooldown, so a repeated request returns
        /// <see cref="DisplayNameRefusal.Unchanged"/> rather than <see cref="DisplayNameRefusal.TooSoon"/>.
        /// </summary>
        /// <param name="name">The name as the player typed it.</param>
        /// <param name="currentName">The name this player has now.</param>
        /// <param name="lastRenamedAt">When this player last renamed, or <see cref="MetaTime.Epoch"/> if never.</param>
        /// <param name="now">The current time.</param>
        /// <param name="reservedBotNames">The reserved bot names. See <see cref="Validate"/>.</param>
        public static DisplayNameRefusal ValidateRename(string name, string currentName, MetaTime lastRenamedAt, MetaTime now, BotNameRoster reservedBotNames)
        {
            DisplayNameRefusal refusal = Validate(name, reservedBotNames);
            if (refusal != DisplayNameRefusal.None)
                return refusal;

            // Compare canonical forms case-sensitively: "bob" to "Bob" is a change, but an extra space is not.
            if (string.Equals(ToStoredForm(name), ToStoredForm(currentName), System.StringComparison.Ordinal))
                return DisplayNameRefusal.Unchanged;

            if (lastRenamedAt > MetaTime.Epoch && now < lastRenamedAt + RenameCooldown)
                return DisplayNameRefusal.TooSoon;

            return DisplayNameRefusal.None;
        }

        /// <summary>The player-facing message for <paramref name="refusal"/>, or empty for <see cref="DisplayNameRefusal.None"/>.</summary>
        public static string DescribeRefusal(DisplayNameRefusal refusal) => refusal switch
        {
            DisplayNameRefusal.TooShort             => $"That name is too short — {MinLength} characters at least.",
            DisplayNameRefusal.TooLong              => $"That name is too long — {MaxLength} characters at most.",
            DisplayNameRefusal.IllegalCharacter     => "Letters, numbers, spaces, hyphens, underscores and apostrophes only.",
            DisplayNameRefusal.MissingLetter        => "A name needs at least one letter in it.",
            DisplayNameRefusal.InvalidPunctuation   => "Hyphens, underscores and apostrophes go between letters, one at a time.",
            DisplayNameRefusal.ReservedName         => "That name is reserved for a computer player or the game itself. Pick another.",
            DisplayNameRefusal.Unchanged            => "That is already your name.",
            DisplayNameRefusal.TooSoon              => $"You just changed your name. Try again in {RenameCooldown.Milliseconds / 1000} seconds.",
            _                                       => string.Empty,
        };
    }
}
