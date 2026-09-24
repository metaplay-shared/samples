using Metaplay.Core.Config;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// A config item that checks its own values when the config is built.
    /// <para>
    /// To validate a new library, implement this interface on its item type and register the library in
    /// <see cref="GameConfigValidation"/>. Every item of every registered library is validated, and any error
    /// fails the build.
    /// </para>
    /// </summary>
    public interface IValidatedConfigItem
    {
        void Validate(ConfigItemValidation validation);
    }

    /// <summary>
    /// One run of <see cref="GameConfigValidation"/>: the SDK's report and the error list that every
    /// <see cref="ConfigItemValidation"/> scope of the run writes to.
    /// </summary>
    internal sealed class ConfigValidationRun
    {
        readonly GameConfigValidationResult _result;
        readonly List<string>               _errors = new List<string>();

        internal ConfigValidationRun(GameConfigValidationResult result)
        {
            _result = result;
        }

        /// <summary>Every error recorded so far, in the order recorded.</summary>
        internal IReadOnlyList<string> Errors => _errors;

        /// <summary>
        /// Returns the validation scope for item <paramref name="itemKey"/> of library <paramref name="libraryName"/>.
        /// A null key is shown as "(no key)".
        /// </summary>
        internal ConfigItemValidation For(string libraryName, object itemKey) =>
            new ConfigItemValidation(_result, _errors, libraryName, itemKey?.ToString() ?? "(no key)");
    }

    /// <summary>
    /// Collects validation errors for one config item, with helper checks.
    /// <para>
    /// Every message includes the library name and the item key, so a failing build names the sheet row to fix.
    /// Errors accumulate instead of stopping at the first one, and <see cref="GameConfigValidation"/> fails the
    /// build at the end if any were recorded.
    /// </para>
    /// </summary>
    public sealed class ConfigItemValidation
    {
        readonly GameConfigValidationResult _result;
        readonly List<string>               _errors;
        readonly string                     _libraryName;
        readonly string                     _itemKey;

        internal ConfigItemValidation(GameConfigValidationResult result, List<string> errors, string libraryName, string itemKey)
        {
            _result      = result;
            _errors      = errors;
            _libraryName = libraryName;
            _itemKey     = itemKey;
        }

        /// <summary>
        /// Records one error. <paramref name="memberHint"/> names the member or column at fault. The LiveOps
        /// Dashboard uses it to highlight the cell.
        /// </summary>
        public void Error(string message, string memberHint = "")
        {
            _result.Error(_libraryName, _itemKey, message, memberHint);
            _errors.Add($"{_libraryName}[{_itemKey}]{(memberHint.Length > 0 ? "." + memberHint : "")}: {message}");
        }

        /// <summary>Records an error if <paramref name="condition"/> is false.</summary>
        public void Require(bool condition, string message, string memberHint = "")
        {
            if (!condition)
                Error(message, memberHint);
        }

        public void RequirePositive(int value, string memberHint)
        {
            Require(value > 0, $"must be positive, is {value}", memberHint);
        }

        public void RequireNonNegative(int value, string memberHint)
        {
            Require(value >= 0, $"must not be negative, is {value}", memberHint);
        }

        public void RequireAtMost(int value, int limit, string memberHint)
        {
            Require(value <= limit, $"must be at most {limit}, is {value}", memberHint);
        }

        public void RequireCount<TItem>(IReadOnlyList<TItem> items, int expected, string memberHint)
        {
            if (items == null)
                Error($"must hold {expected} entries, is missing", memberHint);
            else
                Require(items.Count == expected, $"must hold exactly {expected} entries, holds {items.Count}", memberHint);
        }

        public void RequireNotEmpty<TItem>(IReadOnlyList<TItem> items, string memberHint)
        {
            Require(items != null && items.Count > 0, "must not be empty", memberHint);
        }

        /// <summary>
        /// The checks shared by every row of a table whose rows are numbered 1 to <paramref name="count"/>: the
        /// row has an id that no earlier row has, and its position is in range, held by no earlier row, and equal
        /// to its place in the list (<paramref name="index"/> + 1). <paramref name="ids"/> and
        /// <paramref name="positions"/> collect the rows seen so far. <paramref name="rowNoun"/> names a row in the
        /// messages, and <paramref name="authoredOrder"/> ends the message for a row out of order.
        /// </summary>
        public void RequireNumberedRow<TId>(TId id, int position, int index, int count, HashSet<TId> ids, HashSet<int> positions, string rowNoun, string authoredOrder, string memberHint)
            where TId : class
        {
            if (id == null)
                Error($"has no {rowNoun} id", memberHint);
            else if (!ids.Add(id))
                Error($"names {rowNoun} id '{id}' more than once", memberHint);

            Require(position >= 1 && position <= count, $"is at position {position}, outside 1-{count}", memberHint);
            if (!positions.Add(position))
                Error($"is at position {position}, which another {rowNoun} already holds", memberHint);
            Require(position == index + 1, $"is at position {position} but is listed {index + 1}st; {authoredOrder}", memberHint);
        }
    }
}
