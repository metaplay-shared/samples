using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary> Collects what <see cref="ContentValidator"/> reports so a test can assert on it. </summary>
    public sealed class CollectingIssueSink : IContentIssueSink
    {
        public readonly List<string> Errors   = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public void Error(string sheet, string row, string column, string message)
            => Errors.Add(Format(sheet, row, column, message));

        public void Warning(string sheet, string row, string column, string message)
            => Warnings.Add(Format(sheet, row, column, message));

        static string Format(string sheet, string row, string column, string message)
            => $"{sheet}/{row}/{column}: {message}";

        /// <summary> Everything collected, for use as an assertion failure message. </summary>
        public string Describe()
            => string.Join("\n", Errors.Select(e => "E " + e).Concat(Warnings.Select(w => "W " + w)));
    }
}
