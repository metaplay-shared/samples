using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// Locates the app's stylesheets and extracts brace-balanced blocks from them, for tests that read CSS source.
/// Reading source shows which rules were written but not which rule wins the cascade, so a test that a value
/// reaches the screen belongs in the browser suite. <see cref="RuleBlocks"/> is the exact-selector lookup. A fixture
/// that matches selectors differently (anchored to a line start, or the rule body only) keeps its own lookup, because
/// its assertions depend on it.
/// </summary>
internal static class CssSource
{
    /// <summary>
    /// Returns the repository root, found by walking up from the test assembly to the directory that contains
    /// <c>WebClient/WebClient.csproj</c>. The working directory is not used, because it varies between runners.
    /// </summary>
    internal static DirectoryInfo RepoRoot() => _repoRoot.Value;

    private static readonly Lazy<DirectoryInfo> _repoRoot = new Lazy<DirectoryInfo>(FindRepoRoot);

    private static DirectoryInfo FindRepoRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WebClient", "WebClient.csproj")))
            directory = directory.Parent;

        if (directory == null)
            throw new DirectoryNotFoundException("Could not find the repository root above " + AppContext.BaseDirectory);

        return directory;
    }

    /// <summary>Returns the absolute path of <paramref name="relativePath"/>, which uses forward slashes on every host.</summary>
    internal static string RepoPath(string relativePath) =>
        Path.Combine(RepoRoot().FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The path of the game table's stylesheet, <c>app.css</c>.</summary>
    internal static string AppCssPath() => RepoPath("WebClient/wwwroot/app.css");

    /// <summary>The path of the meta shell's stylesheet, <c>meta-shell.css</c>, which styles every page outside the table.</summary>
    internal static string MetaCssPath() => RepoPath("WebClient/wwwroot/meta-shell.css");

    /// <summary>The text of <c>app.css</c>, read once per run.</summary>
    internal static string AppCss() => _appCss.Value;

    /// <summary>The text of <c>meta-shell.css</c>, read once per run.</summary>
    internal static string MetaCss() => _metaCss.Value;

    private static readonly Lazy<string> _appCss  = new Lazy<string>(() => File.ReadAllText(AppCssPath()));
    private static readonly Lazy<string> _metaCss = new Lazy<string>(() => File.ReadAllText(MetaCssPath()));

    /// <summary>
    /// The file name and text of every stylesheet, for checks that cover the whole client. A stylesheet added to
    /// <c>wwwroot</c> must be added here, or those checks do not cover it.
    /// </summary>
    internal static IEnumerable<(string Name, string Css)> Stylesheets()
    {
        yield return (Path.GetFileName(AppCssPath()), AppCss());
        yield return (Path.GetFileName(MetaCssPath()), MetaCss());
    }

    /// <summary>
    /// <paramref name="css"/> with its comments removed, so a comment that mentions a property is not reported as a
    /// rule that sets it.
    /// </summary>
    internal static string WithoutComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>
    /// Returns the text from <paramref name="startIndex"/> through the brace that closes the first block, counting
    /// nested braces. A <c>[^}]*</c> regex would stop at the first nested block's closing brace. Start at the
    /// opening brace to get only the block, or at a selector or <c>@</c> to include it. Throws when the block never
    /// closes, so a broken stylesheet is reported as broken instead of as a missing declaration.
    /// </summary>
    internal static string BalancedBlock(string css, int startIndex)
    {
        int depth = 0;
        for (int i = startIndex; i < css.Length; i++)
        {
            if (css[i] == '{')
                depth += 1;
            else if (css[i] == '}')
            {
                depth -= 1;
                if (depth == 0)
                    return css.Substring(startIndex, i - startIndex + 1);
            }
        }

        throw new InvalidOperationException($"The CSS block opening at index {startIndex} never closes.");
    }

    /// <summary>
    /// Returns the first brace-balanced block at or after <paramref name="searchFrom"/>, without the text before
    /// its opening brace. Pass the result of an <c>IndexOf</c> search. A <paramref name="searchFrom"/> of <c>-1</c>
    /// returns the empty string instead of throwing, so the caller's assertion reports the missing rule.
    /// </summary>
    internal static string BlockFollowing(string css, int searchFrom)
    {
        if (searchFrom < 0)
            return "";

        int open = css.IndexOf('{', searchFrom);
        return open < 0 ? "" : BalancedBlock(css, open);
    }

    /// <summary>The first block of <see cref="RuleBlocks"/>, or the empty string.</summary>
    internal static string RuleBlock(string css, string selector)
    {
        List<string> blocks = RuleBlocks(css, selector);
        return blocks.Count > 0 ? blocks[0] : "";
    }

    /// <summary>
    /// Every brace-balanced rule block at an exactly matching selector: the selector followed by optional whitespace
    /// and an opening brace, so <c>.m-avatar</c> does not match <c>.m-avatar__face</c>. A selector can appear more
    /// than once, for example inside a media query, and every occurrence is returned.
    /// </summary>
    internal static List<string> RuleBlocks(string css, string selector)
    {
        List<string> blocks = new List<string>();

        foreach (Match match in Regex.Matches(css, Regex.Escape(selector) + @"\s*\{"))
        {
            string block = BalancedBlock(css, match.Index + match.Value.Length - 1);
            if (block != "")
                blocks.Add(block);
        }

        return blocks;
    }

    /// <summary>The brace-balanced body of each <c>@media (prefers-reduced-motion: reduce)</c> block.</summary>
    internal static List<string> ReducedMotionBlocks(string css)
    {
        const string Marker = "@media (prefers-reduced-motion: reduce)";

        List<string> blocks = new List<string>();

        int at = css.IndexOf(Marker, StringComparison.Ordinal);
        while (at >= 0)
        {
            string block = BlockFollowing(css, at + Marker.Length);
            if (block != "")
                blocks.Add(block);

            at = css.IndexOf(Marker, at + Marker.Length, StringComparison.Ordinal);
        }

        return blocks;
    }
}
