#!/usr/bin/env dotnet
// Builds the game server Docker image with the Blazor WASM WebClient baked in. Run from the repo root:
//
//   dotnet run tools/ServerImageBuild.cs                    build the image with the CLI's default tag
//   dotnet run tools/ServerImageBuild.cs -- mygame:1a27c25  arguments forwarded to `metaplay build image`
//   dotnet run tools/ServerImageBuild.cs -- --stage-only    publish and stage the client, skip the image
//   dotnet run tools/ServerImageBuild.cs -- --aot           compile the client ahead of time to WebAssembly

// File-based apps default to AOT-compatible and nullable-enabled, neither of which fits here: this is a
// build tool run with `dotnet run` and never published AOT, and the rest of the repo's C# disables
// nullable annotations. Without these, JsonSerializer's reflection trips the trimming analyzer.
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

return ServerImageBuild.Run(args);

/// <summary>
/// Publishes the WebClient, stages it into <c>Backend/Server/publicwebapp/</c> with a <c>build-info.json</c>
/// naming its commit, and builds the server image, which <c>Server.csproj</c> copies the staged client into.
/// Use this rather than <c>metaplay build image</c> alone, because the SDK's Dockerfile cannot build the
/// WebClient.
/// </summary>
static class ServerImageBuild
{
    const string StagingPath = "Backend/Server/publicwebapp";

    public static int Run(string[] args)
    {
        bool         stageOnly = false;
        bool         aot       = false;
        List<string> imageArgs = [];

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--stage-only":
                    stageOnly = true;
                    break;

                // The project keeps AOT off so a plain build and the dev loop stay fast; this switches it on
                // for the publish alone. It takes several minutes.
                case "--aot":
                    aot = true;
                    break;

                case "--help":
                case "-h":
                    return Usage();

                // Anything else is the caller talking to the CLI - an image tag, say, or --architecture.
                default:
                    imageArgs.Add(arg);
                    break;
            }
        }

        string repoRoot = RepoRoot.TryFind();
        if (repoRoot == null)
        {
            Console.Error.WriteLine($"Could not find the repo root above {Directory.GetCurrentDirectory()} — no directory on the way up holds metaplay-project.yaml.");
            return 1;
        }

        string publishRoot = Path.Combine(repoRoot, WebClientPublish.WebRootPath);
        string stagingDir  = Path.Combine(repoRoot, StagingPath);

        Console.WriteLine();
        int publishResult = WebClientPublish.Run(repoRoot, aot);
        if (publishResult != 0)
            return publishResult;

        // Deleted before copying, not merged into: the client's assets are named by their content hash, so
        // a publish copied over an older one leaves files the new client never loads.
        Console.WriteLine($"==> Staging into {StagingPath}");
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, recursive: true);
        CopyDirectory(publishRoot, stagingDir);

        WriteBuildInfo(repoRoot, stagingDir);

        if (stageOnly)
        {
            Console.WriteLine();
            Console.WriteLine("Staged only; skipping the image build.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("==> Building server image");
        return RunProcess("metaplay", ["build", "image", .. imageArgs], repoRoot);
    }

    /// <summary>
    /// Writes the staged client's provenance next to index.html. Git failing (no git on PATH, a source
    /// export rather than a checkout) leaves the fields unknown rather than failing the build.
    /// </summary>
    static void WriteBuildInfo(string repoRoot, string stagingDir)
    {
        string commit  = TryCapture("git", ["rev-parse", "HEAD"], repoRoot) ?? "unknown";
        string status  = TryCapture("git", ["status", "--porcelain"], repoRoot);
        bool   isDirty = !string.IsNullOrWhiteSpace(status);

        Dictionary<string, object> buildInfo = new Dictionary<string, object>
        {
            ["commit"]  = commit,
            ["dirty"]   = isDirty,
            // InvariantCulture: ':' in a format string means "the current culture's time separator", which
            // on a machine with e.g. a Finnish locale writes 07.46.03 instead of 07:46:03.
            ["builtAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };

        File.WriteAllText(
            Path.Combine(stagingDir, "build-info.json"),
            JsonSerializer.Serialize(buildInfo, new JsonSerializerOptions { WriteIndented = true }));

        string[] stagedFiles = Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories);
        double   sizeMb      = stagedFiles.Sum(path => new FileInfo(path).Length) / (1024.0 * 1024.0);
        string   shortCommit = commit.Length > 10 ? commit.Substring(0, 10) : commit;

        Console.WriteLine($"    {stagedFiles.Length} files, {sizeMb.ToString("F1", CultureInfo.InvariantCulture)} MB, commit {shortCommit}{(isDirty ? " (working tree dirty)" : "")}");
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
    /// Runs a command with its output going straight to this console, and returns its exit code.
    /// </summary>
    static int RunProcess(string fileName, string[] arguments, string workingDirectory)
    {
        try
        {
            return ChildProcess.Run(fileName, arguments, workingDirectory);
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
        ProcessStartInfo startInfo = ChildProcess.CreateStartInfo(fileName, arguments, workingDirectory);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError  = true;

        try
        {
            using Process process = Process.Start(startInfo);
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    static int Usage()
    {
        Console.Error.WriteLine("Usage: dotnet run tools/ServerImageBuild.cs [-- <args for the CLI's build image>]");
        Console.Error.WriteLine("       dotnet run tools/ServerImageBuild.cs -- --stage-only");
        Console.Error.WriteLine("       dotnet run tools/ServerImageBuild.cs -- --aot [<args for the CLI's build image>]");
        return 0;
    }
}

// The helpers below are copied into each file-based app in tools/ rather than shared with #:include,
// which needs .NET SDK 10.0.300 or later.

/// <summary>The checkout a tool is run in.</summary>
static class RepoRoot
{
    /// <summary>
    /// The nearest directory at or above the working directory that holds <c>metaplay-project.yaml</c>, or null
    /// when the tool is run outside a checkout.
    /// </summary>
    public static string TryFind()
    {
        DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "metaplay-project.yaml")))
            directory = directory.Parent;

        return directory?.FullName;
    }
}

/// <summary>Starting the commands a tool drives (dotnet, git, metaplay) as child processes.</summary>
static class ChildProcess
{
    /// <summary>
    /// How to start <paramref name="fileName"/> with <paramref name="arguments"/> passed one by one, never
    /// re-split by a shell, in <paramref name="workingDirectory"/>. The child shares this console.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string fileName, string[] arguments, string workingDirectory)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute  = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    /// <summary>
    /// Runs a command with its output going straight to this console and returns its exit code. Throws
    /// <see cref="System.ComponentModel.Win32Exception"/> when the executable cannot be started.
    /// </summary>
    public static int Run(string fileName, string[] arguments, string workingDirectory)
    {
        using Process process = Process.Start(CreateStartInfo(fileName, arguments, workingDirectory));
        process.WaitForExit();
        return process.ExitCode;
    }
}

/// <summary>
/// The Release publish of the WebClient: the build the server serves, locally (<c>DevStack --release</c>,
/// through <c>Backend/Server/Config/Options.local.yaml</c>) and in a cloud image (<c>ServerImageBuild</c>).
/// </summary>
static class WebClientPublish
{
    /// <summary>The WebClient project, relative to the repo root.</summary>
    public const string ProjectPath = "WebClient/WebClient.csproj";

    /// <summary>The publish's output directory, relative to the repo root.</summary>
    public const string OutputDir = "WebClient/bin/Release/net10.0-browser/publish";

    /// <summary>
    /// The web root inside <see cref="OutputDir"/>, relative to the repo root. This is the directory
    /// <c>Backend/Server/Config/Options.local.yaml</c> points <c>WebClientHosting.WebRootPath</c> at.
    /// </summary>
    public const string WebRootPath = OutputDir + "/wwwroot";

    /// <summary>
    /// Generates the pre-built WASM serializer in a process of its own, before a build or publish of the
    /// WebClient. The client's build generates it too, but a build does not reliably resolve a DLL the same build
    /// generated (see <c>tools/SerializerGen/BrowserSerializer.targets</c>), and a fresh checkout has no DLL.
    /// Returns 0, or the exit code to fail with once the failure has been reported.
    /// </summary>
    public static int GenerateSerializer(string repoRoot)
    {
        Console.WriteLine("==> Generating the WASM serializer");
        int exitCode = ChildProcess.Run(
            "dotnet",
            ["run", "--project", Path.Combine(repoRoot, "tools/SerializerGen"), "--", Path.Combine(repoRoot, "WebClient/Serializer")],
            repoRoot);
        if (exitCode != 0)
            Console.Error.WriteLine("WASM serializer generation failed.");
        return exitCode;
    }

    /// <summary>
    /// Publishes the WebClient in Release, <paramref name="aot"/> compiling it ahead of time, and checks the
    /// output holds what the server serves. Returns 0, or the exit code to fail with once the failure has been
    /// reported.
    /// <para>
    /// A plain build does not produce a self-contained wwwroot: index.html and the static assets are resolved
    /// through a manifest rather than copied, so this has to be a publish. A publish adds to its output
    /// directory and never removes from it, and every rebuilt asset carries a new content hash in its name, so
    /// the directory is deleted first or it accumulates every earlier build.
    /// </para>
    /// </summary>
    public static int Run(string repoRoot, bool aot)
    {
        int serializerExitCode = GenerateSerializer(repoRoot);
        if (serializerExitCode != 0)
            return serializerExitCode;

        string outputDir = Path.Combine(repoRoot, OutputDir);
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        Console.WriteLine($"==> Publishing WebClient (Release{(aot ? ", AOT" : "")})");
        string   project   = Path.Combine(repoRoot, ProjectPath);
        string[] arguments = aot
            ? ["publish", project, "-c", "Release", "-p:RunAOTCompilation=true"]
            : ["publish", project, "-c", "Release"];
        int exitCode = ChildProcess.Run("dotnet", arguments, repoRoot);
        if (exitCode != 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("WebClient publish failed. A WASM publish needs the wasm-tools workload:");
            Console.Error.WriteLine("  dotnet workload install wasm-tools");
            return exitCode;
        }

        // Checked here rather than found out from a server that serves nothing.
        string webRoot = Path.Combine(repoRoot, WebRootPath);
        foreach (string required in new[] { "index.html", "_framework" })
        {
            string requiredPath = Path.Combine(webRoot, required);
            if (!File.Exists(requiredPath) && !Directory.Exists(requiredPath))
            {
                Console.Error.WriteLine($"Publish output is missing '{required}': {webRoot}");
                return 1;
            }
        }

        return 0;
    }
}
