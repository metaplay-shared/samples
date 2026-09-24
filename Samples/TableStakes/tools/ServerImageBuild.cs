// Builds the game server Docker image with the Blazor WASM WebClient included. Run from the project root:
//
//   dotnet run tools/ServerImageBuild.cs                    build the image with the CLI's default tag
//   dotnet run tools/ServerImageBuild.cs -- mygame:1a27c25  arguments forwarded to `metaplay build image`
//   dotnet run tools/ServerImageBuild.cs -- --stage-only    publish and stage the client, skip the image
//
// This is a .NET 10 file-based app, not a project, because it only runs `dotnet`, `git` and `metaplay` as
// processes and references no SDK assemblies. The other tools/ programs are projects because they reference
// the SDK's assemblies and analyzers.

// File-based apps default to PublishAot=true and Nullable=enable. This tool is never published, so AOT is
// turned off, which also stops the trimming analyzer from warning about JsonSerializer's use of reflection.
// Nullable is turned off to match the rest of the sample's C# code.
#:property PublishAot=false
#:property Nullable=disable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

return ServerImageBuild.Run(args);

/// <summary>
/// Publishes the WebClient, stages it into <c>Backend/Server/publicwebapp/</c>, and then runs
/// <c>metaplay build image</c>. The SDK's <c>MetaplaySDK/Dockerfile.server</c> cannot build the WebClient, because
/// its build stage has neither the client's sources nor the <c>wasm-tools</c> workload. <c>Server.csproj</c> copies
/// the staged directory to <c>/gameserver/publicwebapp</c> in the image, the default
/// <c>WebClientHosting.WebRootPath</c> in cloud environments. <b>Use this instead of running
/// <c>metaplay build image</c> directly</b>, which includes whatever an earlier run staged. This tool recreates the
/// publish and staging directories on every run, and stamps a build ID into <c>index.html</c> and
/// <c>build-info.json</c> (docs/web-client.md, "Updating clients after a deploy").
/// </summary>
static class ServerImageBuild
{
    const string ClientProjectPath        = "WebClient/WebClient.csproj";
    const string ClientPublishDir         = "WebClient/bin/Release/net10.0-browser/publish";
    const string ClientPublishWebRootPath = ClientPublishDir + "/wwwroot";
    const string StagingPath              = "Backend/Server/publicwebapp";
    const string SerializerGenProjectPath = "tools/SerializerGen";
    const string SerializerOutputPath     = "WebClient/Serializer";

    /// <summary>The tag in <c>WebClient/wwwroot/index.html</c> that staging replaces with the build ID.</summary>
    const string BuildIdPlaceholder = "<meta name=\"ts-build-id\" content=\"dev\" />";

    public static int Run(string[] args)
    {
        bool         stageOnly = false;
        List<string> imageArgs = [];

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--stage-only":
                    stageOnly = true;
                    break;

                case "--help":
                case "-h":
                    return Usage(null);

                // Any other argument, such as an image tag or --architecture, is passed to
                // `metaplay build image` unchanged.
                default:
                    imageArgs.Add(arg);
                    break;
            }
        }

        string projectRoot = TryFindProjectRoot();
        if (projectRoot == null)
        {
            Console.Error.WriteLine($"Could not find the repo root above {Directory.GetCurrentDirectory()} — no directory on the way up holds metaplay-project.yaml.");
            return 1;
        }

        string clientProject  = Path.Combine(projectRoot, ClientProjectPath);
        string publishDir     = Path.Combine(projectRoot, ClientPublishDir);
        string publishWebRoot = Path.Combine(projectRoot, ClientPublishWebRootPath);
        string stagingDir     = Path.Combine(projectRoot, StagingPath);

        // Generate the pre-built WASM serializer in a separate process before the publish. The client's build
        // can generate it too, but a build does not reliably resolve a DLL that the same build generated (see
        // tools/SerializerGen/BrowserSerializer.targets), and a fresh CI checkout has no DLL.
        Console.WriteLine();
        Console.WriteLine("==> Generating the WASM serializer");
        int serializerGenExitCode = RunProcess(
            "dotnet",
            ["run", "--project", Path.Combine(projectRoot, SerializerGenProjectPath), "-c", "Release", "--", Path.Combine(projectRoot, SerializerOutputPath)],
            projectRoot);
        if (serializerGenExitCode != 0)
        {
            Console.Error.WriteLine("WASM serializer generation failed.");
            return serializerGenExitCode;
        }

        // This must be a publish, because a build does not copy index.html and the static assets into its
        // output (it serves them through a manifest). Delete the publish directory first: a publish never
        // deletes files, so an asset removed from wwwroot would stay from an earlier publish and be staged.
        Console.WriteLine();
        Console.WriteLine("==> Publishing WebClient (Release)");
        if (Directory.Exists(publishDir))
            Directory.Delete(publishDir, recursive: true);
        int publishExitCode = RunProcess("dotnet", ["publish", clientProject, "-c", "Release"], projectRoot);
        if (publishExitCode != 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("WebClient publish failed. A WASM publish needs the wasm-tools workload:");
            Console.Error.WriteLine("  dotnet workload install wasm-tools");
            return publishExitCode;
        }

        // Fail here if the publish output lacks what the server serves, instead of in a deployed environment.
        string[] requiredEntries = ["index.html", "_framework"];
        foreach (string required in requiredEntries)
        {
            string requiredPath = Path.Combine(publishWebRoot, required);
            if (!File.Exists(requiredPath) && !Directory.Exists(requiredPath))
            {
                Console.Error.WriteLine($"Publish output is missing '{required}': {publishWebRoot}");
                return 1;
            }
        }

        // Delete the staging directory before copying. Blazor's asset file names contain content hashes and are
        // listed in the boot manifest in _framework/dotnet.js, so copying over an older publish would leave
        // files that the manifest does not reference.
        Console.WriteLine($"==> Staging into {StagingPath}");
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, recursive: true);
        CopyDirectory(publishWebRoot, stagingDir);

        string buildId = WriteBuildInfo(projectRoot, stagingDir);
        if (!StampBuildId(stagingDir, buildId))
            return 1;

        if (stageOnly)
        {
            Console.WriteLine();
            Console.WriteLine("Staged only; skipping the image build.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("==> Building server image");
        return RunProcess("metaplay", ["build", "image", .. imageArgs], projectRoot);
    }

    /// <summary>
    /// Writes <c>build-info.json</c> (commit, dirty flag, build time and build ID) next to index.html and returns
    /// the build ID. If git fails, for example because git is not on PATH or the source is not a checkout, the
    /// commit is written as "unknown" and the build continues.
    /// <para>
    /// The build ID is the UTC build time followed by the short commit, so two runs on one commit get
    /// different IDs. It contains only letters, digits and '-', because the page puts it in URLs.
    /// </para>
    /// </summary>
    static string WriteBuildInfo(string projectRoot, string stagingDir)
    {
        string   commit      = TryCapture("git", ["rev-parse", "HEAD"], projectRoot) ?? "unknown";
        string   status      = TryCapture("git", ["status", "--porcelain"], projectRoot);
        bool     isDirty     = !string.IsNullOrWhiteSpace(status);
        string   shortCommit = commit.Length > 10 ? commit.Substring(0, 10) : commit;
        DateTime builtAt     = DateTime.UtcNow;
        string   buildId     = builtAt.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + shortCommit;

        Dictionary<string, object> buildInfo = new Dictionary<string, object>
        {
            ["commit"]  = commit,
            ["dirty"]   = isDirty,
            // InvariantCulture: ':' in a format string is the current culture's time separator, which is '.'
            // in some locales, such as Finnish.
            ["builtAt"] = builtAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["buildId"] = buildId,
        };

        File.WriteAllText(
            Path.Combine(stagingDir, "build-info.json"),
            JsonSerializer.Serialize(buildInfo, new JsonSerializerOptions { WriteIndented = true }));

        string[] stagedFiles = Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories);
        double   sizeMb      = stagedFiles.Sum(path => new FileInfo(path).Length) / (1024.0 * 1024.0);

        Console.WriteLine($"    {stagedFiles.Length} files, {sizeMb.ToString("F1", CultureInfo.InvariantCulture)} MB, commit {shortCommit}{(isDirty ? " (working tree dirty)" : "")}, build ID {buildId}");
        return buildId;
    }

    /// <summary>
    /// Replaces the build ID placeholder in the staged index.html and deletes index.html.br and index.html.gz.
    /// The server would serve those compressed copies instead of index.html, and they still contain the
    /// placeholder. Returns false if the placeholder does not occur exactly once.
    /// </summary>
    static bool StampBuildId(string stagingDir, string buildId)
    {
        string indexPath = Path.Combine(stagingDir, "index.html");
        string html      = File.ReadAllText(indexPath);

        int first = html.IndexOf(BuildIdPlaceholder, StringComparison.Ordinal);
        if (first < 0 || html.IndexOf(BuildIdPlaceholder, first + 1, StringComparison.Ordinal) >= 0)
        {
            Console.Error.WriteLine($"index.html must contain the build ID placeholder exactly once: {BuildIdPlaceholder}");
            return false;
        }

        File.WriteAllText(indexPath, html.Replace(BuildIdPlaceholder, $"<meta name=\"ts-build-id\" content=\"{buildId}\" />"));

        foreach (string compressedCopy in new[] { indexPath + ".br", indexPath + ".gz" })
        {
            if (File.Exists(compressedCopy))
                File.Delete(compressedCopy);
        }

        return true;
    }

    static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (string filePath in Directory.GetFiles(sourceDir))
            File.Copy(filePath, Path.Combine(targetDir, Path.GetFileName(filePath)));

        foreach (string subDirPath in Directory.GetDirectories(sourceDir))
            CopyDirectory(subDirPath, Path.Combine(targetDir, Path.GetFileName(subDirPath)));
    }

    /// <summary>
    /// Runs a command with its output going to this console and returns its exit code. If the executable is not
    /// found, prints its name and returns 1, because the exception message does not name the file.
    /// </summary>
    static int RunProcess(string fileName, string[] arguments, string workingDirectory)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute  = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using Process process = Process.Start(startInfo);
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception)
        {
            Console.Error.WriteLine($"'{fileName}' was not found on PATH.");
            if (fileName == "metaplay")
                Console.Error.WriteLine("Install the Metaplay CLI, or re-run with --stage-only and build the image yourself; the client is already staged.");
            return 1;
        }
    }

    /// <summary>Runs a command and returns its trimmed stdout, or null if it could not run or failed.</summary>
    static string TryCapture(string fileName, string[] arguments, string workingDirectory)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory       = workingDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using Process process = Process.Start(startInfo);
            // Read stderr concurrently with stdout, and discard it. If the child fills the stderr pipe buffer
            // while this process is still reading stdout, both processes wait forever. `git status` can print
            // a line-ending warning per file, which is enough to fill the buffer in a large tree.
            Task<string> drainErrors = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            drainErrors.GetAwaiter().GetResult();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    static string TryFindProjectRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "metaplay-project.yaml")))
            directory = directory.Parent;

        return directory?.FullName;
    }

    static int Usage(string error)
    {
        if (error != null)
            Console.Error.WriteLine(error);

        Console.Error.WriteLine("Usage: dotnet run tools/ServerImageBuild.cs [-- <args for the CLI's build image>]");
        Console.Error.WriteLine("       dotnet run tools/ServerImageBuild.cs -- --stage-only");
        return error != null ? 1 : 0;
    }
}
