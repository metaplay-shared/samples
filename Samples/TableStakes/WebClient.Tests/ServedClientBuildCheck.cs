using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace WebClient.Tests;

/// <summary>
/// Checks, once per test run, that the client answering on the tested port was built from this working tree, by
/// comparing the served app assembly with the build output byte for byte. Without the check, a stale dev server or
/// an unrebuilt client shows up as missing elements and features reported as broken. In WSL a dev server bound to
/// <c>127.0.0.1</c> takes <c>localhost</c> from a later server bound to <c>0.0.0.0</c>.
/// </summary>
internal static class ServedClientBuildCheck
{
    /// <summary>
    /// The path at which the dev server serves the app assembly regardless of its fingerprint. A publish output
    /// has only the fingerprinted name, so <see cref="ServedPathFor"/> requests that name instead.
    /// </summary>
    private const string ServedAppAssemblyPath = "_framework/WebClient.wasm";

    /// <summary>The fingerprinted name the build writes, as <c>WebClient.&lt;fingerprint&gt;.wasm</c>.</summary>
    private const string BuiltAppAssemblyPattern = "WebClient.*.wasm";

    private static readonly object Gate = new object();
    private static Task?         _verification;

    /// <summary>
    /// Verifies the client at <paramref name="menuUrl"/>. Only the first call in a test run performs the check,
    /// and later calls return the same task.
    /// </summary>
    public static Task VerifyIsThisBuildAsync(string menuUrl)
    {
        lock (Gate)
        {
            _verification ??= VerifyOnceAsync(menuUrl);
            return _verification;
        }
    }

    private static async Task VerifyOnceAsync(string menuUrl)
    {
        FileInfo? builtAssembly = FindBuiltAppAssembly();
        if (builtAssembly == null)
        {
            // No local build output to compare against, for example when the client runs in a container, so
            // skip the check.
            TestContext.Progress.WriteLine($"Client identity not checked: no built app assembly found to compare {menuUrl} against.");
            return;
        }

        string servedPath  = ServedPathFor(builtAssembly);
        byte[] servedBytes = await FetchServedAppAssemblyAsync(menuUrl, servedPath);
        if (Hash(servedBytes) == Hash(await File.ReadAllBytesAsync(builtAssembly.FullName)))
            return;

        throw new InvalidOperationException(
            $"The web client answering at {menuUrl} is not the one built from this working tree, so every " +
            $"assertion below it would be made against the wrong app.{Environment.NewLine}" +
            $"Two things cause this:{Environment.NewLine}" +
            $"  - Another server holds the port. A dev server left running from an earlier session keeps " +
            $"serving its own build, and one inside WSL holds 127.0.0.1 specifically, which beats a later " +
            $"server bound to 0.0.0.0 for the name 'localhost'. List the holders with " +
            $"'Get-NetTCPConnection -LocalPort {new Uri(menuUrl).Port} -State Listen'.{Environment.NewLine}" +
            $"  - The client was not rebuilt. Run 'dotnet build WebClient/WebClient.csproj' and restart it.{Environment.NewLine}" +
            $"Compared the served {servedPath} against {builtAssembly.FullName}.");
    }

    /// <summary>
    /// Returns the URL path of the app assembly to request. A publish output has only the fingerprinted name, so
    /// the request uses the name of the file being compared against.
    /// </summary>
    private static string ServedPathFor(FileInfo built)
        => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TABLESTAKES_E2E_CLIENT_WWWROOT"))
            ? ServedAppAssemblyPath
            : $"_framework/{built.Name}";

    private static async Task<byte[]> FetchServedAppAssemblyAsync(string menuUrl, string servedPath)
    {
        // The client under test is local, so bypass any proxy configured in the environment.
        using HttpClientHandler handler = new HttpClientHandler { UseProxy = false };
        using HttpClient        http    = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        try
        {
            return await http.GetByteArrayAsync($"{menuUrl}/{servedPath}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"No web client answered at {menuUrl}. Start it with " +
                $"'dotnet run --project WebClient/WebClient.csproj' (see AGENTS.md, \"Build and run\").", ex);
        }
    }

    /// <summary>
    /// Returns the app assembly to compare the served client against, or <c>null</c> when none is found. Earlier
    /// builds leave their fingerprinted files behind, so the newest file is used.
    /// <para>
    /// A harness that serves a publish output sets <c>TABLESTAKES_E2E_CLIENT_WWWROOT</c> to that directory, and
    /// the comparison uses the assembly in it. The build output cannot be used then, because trimming rewrites the
    /// published app assembly. In that mode the check only catches another process answering on the port, since
    /// the harness publishes right before serving.
    /// </para>
    /// </summary>
    private static FileInfo? FindBuiltAppAssembly()
    {
        string? servedWwwroot = Environment.GetEnvironmentVariable("TABLESTAKES_E2E_CLIENT_WWWROOT");
        if (!string.IsNullOrEmpty(servedWwwroot))
        {
            string    framework = Path.Combine(servedWwwroot, "_framework");
            FileInfo? published = NewestAppAssemblyIn(framework);
            if (published == null)
            {
                // A named wwwroot without an app assembly means the harness is broken. Skipping the check would
                // let the run pass without verifying the published client.
                throw new InvalidOperationException(
                    $"TABLESTAKES_E2E_CLIENT_WWWROOT names {servedWwwroot}, but {framework} holds no " +
                    $"{BuiltAppAssemblyPattern}. That directory is not a published web client.");
            }

            return published;
        }

        DirectoryInfo? repository = FindRepositoryRoot();
        if (repository == null)
            return null;

        return NewestAppAssemblyIn(Path.Combine(repository.FullName, "WebClient", "bin", BuildConfiguration(), "net10.0-browser", "wwwroot", "_framework"));
    }

    private static FileInfo? NewestAppAssemblyIn(string framework)
    {
        FileInfo[] built = Directory.Exists(framework)
            ? new DirectoryInfo(framework).GetFiles(BuiltAppAssemblyPattern)
            : Array.Empty<FileInfo>();

        return built.OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
    }

    /// <summary>The configuration the test assembly was built in, read from its own output path.</summary>
    private static string BuildConfiguration()
    {
        string[] segments = AppContext.BaseDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int      binNdx   = Array.LastIndexOf(segments, "bin");
        return binNdx >= 0 && binNdx + 1 < segments.Length ? segments[binNdx + 1] : "Debug";
    }

    /// <summary>The repository root (<see cref="CssSource.RepoRoot"/>), or null when the tests run outside it.</summary>
    private static DirectoryInfo? FindRepositoryRoot()
    {
        try
        {
            return CssSource.RepoRoot();
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
