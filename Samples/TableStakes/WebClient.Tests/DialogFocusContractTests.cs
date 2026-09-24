using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace WebClient.Tests;

/// <summary>
/// Checks that every element declaring <c>aria-modal="true"</c> also has <c>tabindex="-1"</c>. When a dialog
/// contains nothing focusable, <c>modal-focus.js</c> focuses the dialog element itself, and <c>focus()</c> does
/// nothing on an element without a <c>tabindex</c>, so focus would stay on the inert content behind the scrim.
/// <para>
/// The test scans the Razor source because some dialogs are only reachable through the matchmaking queue and the
/// property is static markup. It cannot check a <c>tabindex</c> set at runtime.
/// </para>
/// </summary>
[TestFixture]
public class DialogFocusContractTests
{
    /// <summary>The directories scanned for Razor components, relative to the repository root.</summary>
    private static readonly string[] SourceRoots = { "WebClient", "WebClientBase" };

    private static readonly Regex AriaModal = new(@"aria-modal\s*=\s*""true""", RegexOptions.Compiled);
    private static readonly Regex Tabindex  = new(@"tabindex\s*=\s*""-1""",     RegexOptions.Compiled);

    private readonly record struct Dialog(string Where, string Tag);

    [Test]
    public void EveryModalDialogIsProgrammaticallyFocusable()
    {
        Dialog[] dialogs = FindModalDialogs().ToArray();

        // A scan that finds no dialogs would pass without testing anything, so an empty result fails.
        Assert.That(dialogs, Is.Not.Empty,
            $"found no aria-modal elements under {string.Join(" or ", SourceRoots)} — the components moved, or this scan did.");

        foreach (Dialog dialog in dialogs)
            TestContext.Out.WriteLine(dialog.Where);

        string[] missing = dialogs
            .Where(dialog => !Tabindex.IsMatch(dialog.Tag))
            .Select(dialog => dialog.Where)
            .ToArray();

        Assert.That(missing, Is.Empty,
            "these dialogs declare aria-modal=\"true\" without a tabindex, so modal-focus.js cannot put focus " +
            "on them when they open with nothing focusable inside: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Returns every start tag containing <c>aria-modal="true"</c> in the scanned Razor files, with its file and
    /// line. The tag is bounded by the nearest <c>&lt;</c> before the match and the nearest <c>&gt;</c> after it,
    /// so angle brackets elsewhere in the file, such as comparisons in <c>@code</c>, do not affect the match.
    /// </summary>
    private static IEnumerable<Dialog> FindModalDialogs()
    {
        foreach (string file in RazorFiles())
        {
            string source = File.ReadAllText(file);

            foreach (Match match in AriaModal.Matches(source))
            {
                int start = source.LastIndexOf('<', match.Index);
                int end   = source.IndexOf('>', match.Index);
                if (start < 0 || end < 0)
                    continue;

                string relative = Path.GetRelativePath(CssSource.RepoRoot().FullName, file).Replace('\\', '/');
                int    line     = source.Take(start).Count(c => c == '\n') + 1;

                yield return new Dialog($"{relative}:{line}", source.Substring(start, end - start + 1));
            }
        }
    }

    private static IEnumerable<string> RazorFiles()
    {
        foreach (string root in SourceRoots)
        {
            string path = Path.Combine(CssSource.RepoRoot().FullName, root);
            foreach (string file in Directory.EnumerateFiles(path, "*.razor", SearchOption.AllDirectories))
            {
                // Skip bin/ and obj/, which hold build copies of the same components.
                string relative = Path.GetRelativePath(path, file).Replace('\\', '/');
                if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal))
                    continue;

                yield return file;
            }
        }
    }
}
