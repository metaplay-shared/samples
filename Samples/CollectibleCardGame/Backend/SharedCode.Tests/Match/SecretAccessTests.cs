using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Game.Logic.Tests
{
    /// <summary>
    /// That the secret is reached through <b>one</b> accessor, checked by reading the source.
    /// <para>
    /// Reflection cannot ask this. It can say that exactly two members are <c>ServerOnly</c> and that no
    /// action declares one — <c>MatchModelTests</c> and <c>MatchActionTests</c> do both — but "only
    /// <see cref="SecretOps"/> touches the hidden half" is a statement about <em>call sites</em>, and the
    /// only cheap way to check a call site without a Roslyn analyzer is to read the file.
    /// </para>
    /// <para>
    /// The one-path-to-the-secret claim is worth that unusual measure. It is what the whole secrecy argument
    /// rests on: the shape
    /// tests say nothing hidden rides the wire, and this says nothing outside one file can read it in the
    /// first place — which is what keeps the guard-first, no-op-on-a-follower convention from being one
    /// forgetful commit away from a leak.
    /// </para>
    /// <para>
    /// <b>Every tree that compiles against the model is scanned</b>, not only the shared one. The first
    /// version of this test walked <c>SharedCode/</c> alone, and the actor was reading
    /// <c>Model.Secret.BotSeed</c> directly the whole time — harmless in itself, and exactly the shape of
    /// convenience that makes a rule stop meaning anything. The server is where the interesting secret reads
    /// live (the mulligan swap, the defaulted peek, a played card's identity), so leaving it out left the guard
    /// covering the half that needed it least.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SecretAccessTests
    {
        /// <summary>
        /// The files allowed to name the secret types: the declaration, the one accessor, and the one type
        /// that reads and writes them.
        /// </summary>
        static readonly HashSet<string> Allowed = new HashSet<string>
        {
            "MatchSecrets.cs",
            "SecretOps.cs",
            "MatchModel.cs",
        };

        /// <summary>
        /// Anything that reaches the hidden half: the two secret types by name, the accessor, and the member
        /// itself. Matched on word boundaries so a comment mentioning <c>SecretOps</c> does not trip it.
        /// </summary>
        static readonly Regex ReachesTheSecret = new Regex(@"\b(MatchSecrets|SeatSecrets|SecretSeat)\b|\.Secret\b", RegexOptions.Compiled);

        /// <summary>
        /// The trees that compile against <see cref="MatchModel"/> and are therefore able to reach its hidden
        /// half. Relative to the sample root; a tree that does not exist is a failure rather than a skip,
        /// because a renamed folder must not silently take the guard with it.
        /// </summary>
        static readonly string[] ScannedTrees =
        {
            "SharedCode",
            Path.Combine("Backend", "Server"),
            Path.Combine("Backend", "BotClient"),

            // The two browser trees. They compile against MatchModel and are therefore just as able to name
            // the secret as the server is. A client-side read is not a leak — the member is null on a
            // follower — but it is a wrong or throwing answer, and no suite outside SharedCode/ would see it.
            "Client",
            "ClientBase",
        };

        /// <summary>
        /// The extensions worth reading. <c>.razor</c> is in the list because a Blazor component's own
        /// code-behind lives in one, so a browser tree scanned for <c>.cs</c> alone is half scanned.
        /// </summary>
        static readonly string[] ScannedExtensions = { "*.cs", "*.razor" };

        [Test]
        public void TheSecretIsReachedThroughOneAccessor()
        {
            List<string> offenders = new List<string>();

            foreach (string path in SourceFiles())
            {
                string[] lines = File.ReadAllLines(path);
                for (int ndx = 0; ndx < lines.Length; ndx++)
                {
                    string line    = lines[ndx];
                    string trimmed = line.TrimStart();

                    // Comments are where the design is explained, and explaining it means naming it.
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                        continue;

                    if (ReachesTheSecret.IsMatch(line))
                        offenders.Add($"{Path.GetFileName(path)}:{ndx + 1}: {trimmed}");
                }
            }

            Assert.That(offenders, Is.Empty,
                "the hidden half is reached through MatchModel.SecretSeat and read and written only by SecretOps; "
                + $"these {offenders.Count} site(s) reach it directly:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary> Every source file of every scanned tree, minus the build output. </summary>
        static IEnumerable<string> SourceFiles()
        {
            DirectoryInfo root = FindSampleRoot();

            foreach (string tree in ScannedTrees)
            {
                string full = Path.Combine(root.FullName, tree);
                Assert.That(Directory.Exists(full), Is.True,
                    $"'{tree}' is on this test's scan list and does not exist; a renamed tree must not silently drop out of it");

                foreach (string extension in ScannedExtensions)
                {
                    foreach (string path in Directory.EnumerateFiles(full, extension, SearchOption.AllDirectories))
                    {
                        if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                            path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                            continue;

                        if (!Allowed.Contains(Path.GetFileName(path)))
                            yield return path;
                    }
                }
            }
        }

        /// <summary>
        /// The shared-code tree, found by walking up from this file rather than from the working directory —
        /// a test run's working directory is the test host's and says nothing about where the source is.
        /// <para>
        /// A root that cannot be found is a <b>failure</b>, never an ignore and never an inconclusive: a
        /// silent pass here would take the whole guarantee with it, and NUnit counts an inconclusive result in
        /// no bucket at all.
        /// </para>
        /// </summary>
        static DirectoryInfo FindSampleRoot([CallerFilePath] string thisFile = "")
        {
            Assert.That(thisFile, Is.Not.Empty, "the compiler did not supply this file's path");

            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(thisFile));
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "SharedCode", "Match")))
                    return directory;

                directory = directory.Parent;
            }

            Assert.Fail($"could not find the sample root by walking up from '{thisFile}'; "
                + "this test reads the source, so it cannot pass without it");
            return null;
        }
    }
}
