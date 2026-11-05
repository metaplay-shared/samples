using System.Security.Cryptography;

namespace WebClient.Tests;

/// <summary>
/// Checks, once per test run, that the web client under test is the one built from this working tree. A client left
/// running from an earlier session, or one not rebuilt, would otherwise fail the tests as if the features were broken.
/// </summary>
internal static class ClientUnderTest
{
    private const string AppAssemblyPattern = "WebClient.*.wasm";

    private static readonly object Gate = new object();
    private static Task?         _verification;

    /// <summary>Verify the client at <paramref name="clientOrigin"/>, or return the run's first verification.</summary>
    public static Task VerifyIsThisBuildAsync(string clientOrigin)
    {
        lock (Gate)
            return _verification ??= VerifyAsync(clientOrigin);
    }

    private static async Task VerifyAsync(string clientOrigin)
    {
        FileInfo? built = FindBuiltAppAssembly();
        if (built == null)
        {
            TestContext.Progress.WriteLine($"Client identity not checked: no built app assembly found to compare {clientOrigin} against.");
            return;
        }

        // The dev server also serves the app assembly under a name without the fingerprint. A published client has
        // only the fingerprinted name.
        string servedPath = E2EStack.PublishedWwwroot == null ? "_framework/WebClient.wasm" : $"_framework/{built.Name}";
        byte[] served     = await FetchAsync(clientOrigin, servedPath);
        if (Hash(served) == Hash(await File.ReadAllBytesAsync(built.FullName)))
            return;

        int port = new Uri(clientOrigin).Port;
        throw new InvalidOperationException(
            $"The web client at {clientOrigin} is not the one built from this working tree.{Environment.NewLine}" +
            $"  - Another server may hold the port, such as a dev server left running. List the holders with " +
            $"'lsof -iTCP:{port} -sTCP:LISTEN', or 'Get-NetTCPConnection -LocalPort {port} -State Listen' on Windows. " +
            $"One inside WSL holds 127.0.0.1, which wins the name 'localhost'.{Environment.NewLine}" +
            $"  - The client may not have been rebuilt. Run 'dotnet build WebClient/WebClient.csproj' and restart it." +
            $"{Environment.NewLine}Compared the served {servedPath} with {built.FullName}.");
    }

    private static async Task<byte[]> FetchAsync(string clientOrigin, string path)
    {
        // No proxy: one set in the environment would answer for the local client.
        using HttpClientHandler handler = new HttpClientHandler { UseProxy = false };
        using HttpClient        http    = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        try
        {
            return await http.GetByteArrayAsync($"{clientOrigin}/{path}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"No web client answered at {clientOrigin}. Start it with 'dotnet run --project WebClient/WebClient.csproj' " +
                $"(see AGENTS.md, \"Build & run\"), or run the suite through 'python3 run_e2e.py', which starts its own.", ex);
        }
    }

    /// <summary>The app assembly the client under test should serve, or null when there is none to compare with.</summary>
    private static FileInfo? FindBuiltAppAssembly()
    {
        // Trimming rewrites the app assembly, so a published client is compared with its own publish output.
        string? published = E2EStack.PublishedWwwroot;
        if (published != null)
        {
            string framework = Path.Combine(published, "_framework");
            return NewestAppAssemblyIn(framework)
                ?? throw new InvalidOperationException(
                    $"E2E_CLIENT_WWWROOT names {published}, but {framework} holds no {AppAssemblyPattern}, so it is " +
                    "not a published web client.");
        }

        DirectoryInfo? projectRoot = FindProjectRoot();
        if (projectRoot == null)
            return null;

        return NewestAppAssemblyIn(
            Path.Combine(projectRoot.FullName, "WebClient", "bin", Configuration(), "net10.0-browser", "wwwroot", "_framework"));
    }

    // Builds leave the fingerprinted names of earlier builds behind, so the newest is the current one.
    private static FileInfo? NewestAppAssemblyIn(string framework)
    {
        if (!Directory.Exists(framework))
            return null;

        return new DirectoryInfo(framework)
            .GetFiles(AppAssemblyPattern)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>The configuration this test assembly was built in, read from its output path.</summary>
    private static string Configuration()
    {
        string[] segments = AppContext.BaseDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int      bin      = Array.LastIndexOf(segments, "bin");
        return bin >= 0 && bin + 1 < segments.Length ? segments[bin + 1] : "Debug";
    }

    /// <summary>The directory with <c>WebClient/WebClient.csproj</c>, found by walking up from this test assembly.</summary>
    private static DirectoryInfo? FindProjectRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WebClient", "WebClient.csproj")))
            directory = directory.Parent;
        return directory;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
