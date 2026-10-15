#!/usr/bin/env dotnet
// Runs the local game server and WebClient watchers in one foreground console:
//
//   dotnet run tools/DevStack.cs
//
// or publishes the WebClient in Release and runs the server alone, which serves the published client on
// port 5560, the build to measure performance on and the one a phone on the LAN should load:
//
//   dotnet run tools/DevStack.cs -- --release
//
// Both children inherit this console, so their normal colored stdout/stderr remains visible. The launcher
// owns their complete process trees: Ctrl+C gives them a short graceful-shutdown window and then kills any
// survivors, while a Windows kill-on-close Job Object also covers closing or force-ending the launcher.
//
// The watched stack also guards the web dev server against a WebClient build from outside the watcher (an
// IDE, a second shell, an agent checking its change), which leaves the server handing out a client it can no
// longer serve; see WebClientRouteGuard.

#:property PublishAot=false
#:property Nullable=disable

using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

return await DevStack.RunAsync(args);

static class DevStack
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool release = false;
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--help":
                case "-h":
                    return Usage(null);
                case "--release":
                    release = true;
                    break;
                default:
                    return Usage($"Unknown argument: {args[0]}");
            }

            if (args.Length > 1)
                return Usage($"Unexpected argument: {args[1]}");
        }

        string repoRoot = RepoRoot.TryFind();
        if (repoRoot == null)
        {
            Console.Error.WriteLine($"Could not find metaplay-project.yaml above {Directory.GetCurrentDirectory()}.");
            return 1;
        }

        string webProject = Path.Combine(repoRoot, WebClientPublish.ProjectPath);
        ProcessSpec[] processes;

        if (release)
        {
            int publishResult = WebClientPublish.Run(repoRoot, aot: false);
            if (publishResult != 0)
                return publishResult;
            Console.WriteLine();

            // The server alone: it serves the publish output through WebClientHosting, and nothing is watched,
            // so a measurement is not disturbed by a rebuild.
            processes =
            [
                new ProcessSpec("server", "metaplay", ["dev", "server"], repoRoot),
            ];

            string lanIp = TryFindLanIPv4();
            Console.WriteLine("Release stack");
            Console.WriteLine($"  web:    published to {WebClientPublish.WebRootPath}, served by the server");
            Console.WriteLine("  server: metaplay dev server");
            Console.WriteLine($"  URL:    http://localhost:{PublicWebApiPort}");
            Console.WriteLine(lanIp != null
                ? $"  LAN:    http://{lanIp}:{PublicWebApiPort}   (a phone needs inbound TCP {PublicWebApiPort} and 9380 allowed through the firewall)"
                : "  LAN:    no network adapter with a default gateway was found");
            Console.WriteLine("  Nothing is watched: stop and re-run after changing the client or the server.");
            Console.WriteLine("  Stop the server process tree with Ctrl+C.");
        }
        else
        {
            int serializerResult = WebClientPublish.GenerateSerializer(repoRoot);
            if (serializerResult != 0)
                return serializerResult;
            Console.WriteLine();

            processes =
            [
                new ProcessSpec("server", "metaplay", ["dev", "server", "--watch"], repoRoot),
                new ProcessSpec(WebLabel, "dotnet", ["watch", "--project", webProject, "--non-interactive", "run"], repoRoot),
            ];

            Console.WriteLine("Local development stack");
            Console.WriteLine("  server: metaplay dev server --watch");
            Console.WriteLine("  web:    dotnet watch --project WebClient/WebClient.csproj --non-interactive run");
            Console.WriteLine($"  URL:    http://localhost:{WebClientDevPort}");
            Console.WriteLine("  A WebClient build from outside the watcher is caught: the web watcher is restarted when its");
            Console.WriteLine("  dev server can no longer serve the client it hands out.");
            Console.WriteLine("  Stop both process trees with Ctrl+C.");
        }
        Console.WriteLine();

        using CancellationTokenSource shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            using ProcessSupervisor supervisor = new ProcessSupervisor(processes);
            WebClientRouteGuard guard = release ? null : new WebClientRouteGuard(repoRoot, WebClientDevPort, () => supervisor.RestartAsync(WebLabel));

            using CancellationTokenSource guardStop = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            Task guardRun = guard?.RunAsync(guardStop.Token) ?? Task.CompletedTask;
            try
            {
                return await supervisor.RunAsync(shutdown.Token, cancellationExitCode: 130, gracefulShutdown: TimeSpan.FromSeconds(2));
            }
            finally
            {
                guardStop.Cancel();
                await guardRun;
            }
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine($"Could not establish process-tree ownership: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>The supervised web watcher's label, which the guard restarts it by.</summary>
    const string WebLabel = "web";

    /// <summary>The port the web watcher's dev server serves the Debug client on (WebClient/Properties/launchSettings.json).</summary>
    const int WebClientDevPort = 5290;

    /// <summary>The port the local server's public web host serves the published client on.</summary>
    const int PublicWebApiPort = 5560;

    /// <summary>
    /// The IPv4 address of the first up, non-loopback adapter with a default gateway, the one facing the LAN,
    /// which is the address a phone loads the page from. Null when there is none.
    /// </summary>
    static string TryFindLanIPv4()
    {
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                IPInterfaceProperties properties = adapter.GetIPProperties();
                bool hasGateway = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));
                if (!hasGateway)
                    continue;

                UnicastIPAddressInformation address = properties.UnicastAddresses
                    .FirstOrDefault(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork);
                if (address != null)
                    return address.Address.ToString();
            }
        }
        catch (NetworkInformationException)
        {
        }

        return null;
    }

    static int Usage(string error)
    {
        TextWriter writer = error == null ? Console.Out : Console.Error;
        if (error != null)
            writer.WriteLine(error);
        writer.WriteLine("Usage: dotnet run tools/DevStack.cs [-- --release]");
        writer.WriteLine();
        writer.WriteLine("Runs the Metaplay server and WebClient in watch mode in one foreground console.");
        writer.WriteLine("Ctrl+C stops both complete process trees.");
        writer.WriteLine();
        writer.WriteLine("  --release          Publish the WebClient in Release and run the server alone, which serves");
        writer.WriteLine($"                     it at http://localhost:{PublicWebApiPort} and over the LAN. Nothing is watched.");
        return error == null ? 0 : 1;
    }

    sealed record ProcessSpec(string Label, string FileName, string[] Arguments, string WorkingDirectory);

    /// <summary>
    /// Owns the complete process tree of every child it starts. The first child to exit on its own stops the
    /// rest; a child restarted through <see cref="RestartAsync"/> is replaced rather than counted as an exit.
    /// </summary>
    sealed class ProcessSupervisor : IDisposable
    {
        readonly ProcessSpec[] _specs;
        readonly Process[] _processes;
        readonly bool[] _restarting;
        readonly List<Process> _replaced = new List<Process>();
        readonly WindowsKillJob _windowsJob;
        readonly object _sync = new object();

        /// <summary>Completed, and replaced, whenever the set of processes to watch changes.</summary>
        TaskCompletionSource _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        bool _stopping;
        bool _disposed;

        public ProcessSupervisor(ProcessSpec[] specs)
        {
            _specs       = specs;
            _processes   = new Process[specs.Length];
            _restarting  = new bool[specs.Length];
            _windowsJob  = OperatingSystem.IsWindows() ? new WindowsKillJob() : null;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        public async Task<int> RunAsync(CancellationToken cancellationToken, int cancellationExitCode, TimeSpan gracefulShutdown)
        {
            try
            {
                lock (_sync)
                {
                    for (int ndx = 0; ndx < _specs.Length; ndx++)
                        _processes[ndx] = Start(_specs[ndx]);
                }
            }
            catch (Win32Exception exception)
            {
                Console.Error.WriteLine($"Could not start '{exception.Data["label"] ?? "process"}': {exception.Message}");
                StopNow();
                return 1;
            }

            Task cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            while (true)
            {
                Task       changed;
                List<Task> exits = new List<Task>();
                lock (_sync)
                {
                    changed = _changed.Task;
                    for (int ndx = 0; ndx < _processes.Length; ndx++)
                    {
                        if (!_restarting[ndx])
                            exits.Add(_processes[ndx].WaitForExitAsync());
                    }
                }

                Task anyExit = exits.Count > 0 ? Task.WhenAny(exits) : Task.Delay(Timeout.InfiniteTimeSpan);
                await Task.WhenAny(anyExit, cancelled, changed);
                if (cancellationToken.IsCancellationRequested)
                    break;

                int exitedIndex = FindExited();
                if (exitedIndex >= 0)
                {
                    int    exitCode = _processes[exitedIndex].ExitCode;
                    string label    = _specs[exitedIndex].Label;
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"[{label}] exited with code {exitCode}; stopping the remaining process tree.");
                    StopNow();
                    await WaitForStoppedAsync();
                    return exitCode == 0 ? 1 : exitCode;
                }
            }

            if (gracefulShutdown > TimeSpan.Zero)
            {
                Task allExited = Task.WhenAll(_processes.Select(process => process.WaitForExitAsync()));
                await Task.WhenAny(allExited, Task.Delay(gracefulShutdown));
            }
            StopNow();
            await WaitForStoppedAsync();
            return cancellationExitCode;
        }

        /// <summary>
        /// Kill the process tree started from the spec labelled <paramref name="label"/> and start it again.
        /// False when the supervisor is stopping, or the replacement could not be started, in which case the
        /// exit of the old one is what stops the stack.
        /// </summary>
        public async Task<bool> RestartAsync(string label)
        {
            int     index = Array.FindIndex(_specs, spec => spec.Label == label);
            Process old;
            lock (_sync)
            {
                if (_stopping || index < 0)
                    return false;
                _restarting[index] = true;
                old = _processes[index];
                SignalChanged();
            }

            Kill(old);
            await old.WaitForExitAsync();

            lock (_sync)
            {
                _restarting[index] = false;
                if (!_stopping)
                {
                    try
                    {
                        _processes[index] = Start(_specs[index]);
                        _replaced.Add(old);
                    }
                    catch (Win32Exception exception)
                    {
                        Console.Error.WriteLine($"Could not restart '{label}': {exception.Message}");
                    }
                }
                SignalChanged();
                return !_stopping && _processes[index] != old;
            }
        }

        /// <summary>The first child that has exited and is not being restarted, or -1.</summary>
        int FindExited()
        {
            lock (_sync)
            {
                for (int ndx = 0; ndx < _processes.Length; ndx++)
                {
                    if (!_restarting[ndx] && _processes[ndx].HasExited)
                        return ndx;
                }
                return -1;
            }
        }

        void SignalChanged()
        {
            TaskCompletionSource changed = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult();
        }

        /// <summary>Start <paramref name="spec"/> and add it to the kill-on-close job. Called under the lock.</summary>
        Process Start(ProcessSpec spec)
        {
            Console.WriteLine($"[{spec.Label}] starting: {spec.FileName} {string.Join(" ", spec.Arguments)}");
            ProcessStartInfo startInfo = ChildProcess.CreateStartInfo(spec.FileName, spec.Arguments, spec.WorkingDirectory);
            Process process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Win32Exception exception)
            {
                exception.Data["label"] = spec.Label;
                throw;
            }

            try
            {
                _windowsJob?.Add(process);
            }
            catch
            {
                Kill(process);
                throw;
            }

            return process;
        }

        static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        void OnProcessExit(object sender, EventArgs eventArgs) => StopNow();

        void StopNow()
        {
            lock (_sync)
            {
                _stopping = true;
                foreach (Process process in _processes)
                {
                    if (process != null)
                        Kill(process);
                }

                _windowsJob?.Dispose();
            }
        }

        async Task WaitForStoppedAsync()
        {
            Task[] waits = _processes.Where(process => process != null).Select(async process =>
            {
                try
                {
                    await process.WaitForExitAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }).ToArray();
            await Task.WhenAll(waits);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            StopNow();
            foreach (Process process in _processes.Concat(_replaced))
                process?.Dispose();
        }
    }

    /// <summary>
    /// Restarts the web watcher when a build of the WebClient from outside the watcher has left it
    /// handing out a client it can no longer serve.
    /// <para>
    /// The dev server reads its static-asset route table once, at startup, and serves the app assembly under a
    /// content-hashed name. An outside build renames the assembly while the running server still routes the
    /// old name, and <c>dotnet watch</c> only refreshes the browser for it.
    /// </para>
    /// </summary>
    sealed class WebClientRouteGuard
    {
        /// <summary>The manifest a Debug build of the WebClient rewrites, relative to the repo root.</summary>
        const string ManifestPath = "WebClient/bin/Debug/net10.0-browser/WebClient.staticwebassets.endpoints.json";

        static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        /// <summary>A build writes the manifest among many files; it is judged once it has stopped changing for this long.</summary>
        static readonly TimeSpan SettleTime   = TimeSpan.FromSeconds(2);
        /// <summary>How long the restarted watcher is given to build and bring the dev server back before giving up.</summary>
        static readonly TimeSpan RelaunchWait = TimeSpan.FromSeconds(90);

        /// <summary>The app assembly's fingerprinted name in the boot config, which is embedded in _framework/dotnet.js.</summary>
        static readonly Regex AppAssemblyName = new Regex("\"(WebClient\\.[a-z0-9]+\\.wasm)\"", RegexOptions.Compiled);

        readonly string _manifest;
        readonly Uri _baseUri;
        readonly Func<Task<bool>> _restartWatcher;
        readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        /// <param name="restartWatcher">Kills the web watcher's process tree and starts it again; false when it could not.</param>
        public WebClientRouteGuard(string repoRoot, int port, Func<Task<bool>> restartWatcher)
        {
            _manifest       = Path.Combine(repoRoot, ManifestPath);
            _baseUri        = new Uri($"http://localhost:{port}/");
            _restartWatcher = restartWatcher;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            // A missing manifest reads as a fixed epoch time, so the first build is seen as a change like any other.
            DateTime lastSeen = File.GetLastWriteTimeUtc(_manifest);
            try
            {
                while (true)
                {
                    // Every WebClient build rewrites the endpoint manifest, so a change to it is the cue to probe.
                    await Task.Delay(PollInterval, cancellationToken);
                    DateTime written = File.GetLastWriteTimeUtc(_manifest);
                    if (written == lastSeen)
                        continue;

                    do
                    {
                        lastSeen = written;
                        await Task.Delay(SettleTime, cancellationToken);
                        written = File.GetLastWriteTimeUtc(_manifest);
                    }
                    while (written != lastSeen);

                    await CheckAsync(cancellationToken);
                    // The relaunched server's own build may rewrite the manifest; that is not a build to judge again.
                    lastSeen = File.GetLastWriteTimeUtc(_manifest);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        async Task CheckAsync(CancellationToken cancellationToken)
        {
            Probe probe = await ProbeAsync(cancellationToken);
            if (probe.Result != ProbeResult.Stale)
                return;

            Console.WriteLine();
            Console.WriteLine($"[web] The dev server cannot serve {probe.AssemblyName}, the client its boot config hands out: the WebClient was");
            Console.WriteLine("[web] built outside the watcher and the server's route table is stale. Restarting the web watcher.");

            if (!await _restartWatcher())
            {
                Console.WriteLine("[web] The web watcher could not be restarted; restart the stack by hand.");
                return;
            }

            if (!await WaitForServerAsync(RelaunchWait, cancellationToken))
            {
                Console.WriteLine($"[web] The dev server did not come back within {RelaunchWait.TotalSeconds:0} s; restart the stack by hand.");
                return;
            }

            Probe after = await ProbeAsync(cancellationToken);
            Console.WriteLine(after.Result == ProbeResult.Healthy
                ? $"[web] The dev server is back and serves {after.AssemblyName}. Reload the page if it did not reload itself."
                : "[web] The dev server is back but still cannot serve the client it hands out; restart the stack by hand.");
            Console.WriteLine();
        }

        enum ProbeResult { Healthy, Stale, Unreachable }

        readonly record struct Probe(ProbeResult Result, string AssemblyName);

        /// <summary>
        /// Whether the server can serve the app assembly its own boot config names. Unreachable while the server
        /// is down or starting, which is nothing to repair; Healthy when the boot config names no assembly,
        /// since there is then nothing to judge.
        /// </summary>
        async Task<Probe> ProbeAsync(CancellationToken cancellationToken)
        {
            string bootConfig;
            try
            {
                using HttpResponseMessage response = await _http.GetAsync(new Uri(_baseUri, "_framework/dotnet.js"), cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return new Probe(ProbeResult.Unreachable, null);
                bootConfig = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException)
            {
                return new Probe(ProbeResult.Unreachable, null);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new Probe(ProbeResult.Unreachable, null);
            }

            Match match = AppAssemblyName.Match(bootConfig);
            if (!match.Success)
                return new Probe(ProbeResult.Healthy, null);

            string assemblyName = match.Groups[1].Value;
            try
            {
                using HttpResponseMessage response = await _http.GetAsync(new Uri(_baseUri, "_framework/" + assemblyName), cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return new Probe(ProbeResult.Stale, assemblyName);
                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return new Probe(body.Length == 0 ? ProbeResult.Stale : ProbeResult.Healthy, assemblyName);
            }
            catch (HttpRequestException)
            {
                return new Probe(ProbeResult.Unreachable, assemblyName);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new Probe(ProbeResult.Unreachable, assemblyName);
            }
        }

        /// <summary>Whether the server answers for its boot config within <paramref name="timeout"/>.</summary>
        async Task<bool> WaitForServerAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < timeout)
            {
                await Task.Delay(500, cancellationToken);
                Probe probe = await ProbeAsync(cancellationToken);
                if (probe.Result != ProbeResult.Unreachable)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Windows Job Objects are inherited by descendants. KILL_ON_JOB_CLOSE makes the OS terminate every
    /// server/web watcher descendant if the launcher exits without reaching managed cleanup.
    /// </summary>
    sealed class WindowsKillJob : IDisposable
    {
        const uint JobObjectLimitKillOnJobClose = 0x00002000;
        readonly SafeFileHandle _handle;
        bool _disposed;

        public WindowsKillJob()
        {
            _handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (_handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the process cleanup job.");

            JobObjectExtendedLimitInformation limits = new JobObjectExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
            int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, pointer, fDeleteOld: false);
                if (!NativeMethods.SetInformationJobObject(_handle, 9, pointer, (uint)size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure the process cleanup job.");
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        public void Add(Process process)
        {
            if (!NativeMethods.AssignProcessToJobObject(_handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not add process {process.Id} to the cleanup job.");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _handle.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, IntPtr information, uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
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
