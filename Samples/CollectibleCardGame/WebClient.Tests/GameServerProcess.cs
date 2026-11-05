using NUnit.Framework;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;

namespace WebClient.Tests;

/// <summary>
/// A game server the test suite owns, so a test can restart it or pace it differently.
/// <para>
/// <see cref="MatchAbandonTests"/> kills a match by restarting the server it is on: that is the real event
/// rather than an approximation of it — a rolling deploy — and it is why the harness is kept rather than
/// replaced by a dev-only "terminate match" endpoint that would be product surface existing for one test. The
/// other suites that own one need an option nobody else may have — a timing, or no session linger — which is
/// what <see cref="StartAsync"/>'s option overrides are for.
/// </para>
/// <para>
/// The web client dev server stays up throughout: restarting it would cost a cold WebAssembly boot and would
/// invalidate <c>AGENTS.md</c>'s "restart the web client after rebuilding it" gotcha in the other direction.
/// </para>
/// <para>
/// A fixture that owns the process cannot share the ports with another game server, so it runs after the harness
/// (<c>run_e2e.py</c>) has stopped the shared one. It binds the same ports the shared server had, which
/// the harness passes in <c>E2E_SERVER_PORT_ARGS</c>, so the web client the harness keeps running reaches
/// it. Run by hand without the harness, it binds the stock ports.
/// </para>
/// </summary>
public sealed class GameServerProcess : IAsyncDisposable
{
    /// <summary>
    /// The listener arguments, one per line: from the harness, or the stock ports' readiness endpoint. The SDK's
    /// readiness endpoint answers 503 until the node is ready. It lives on the <em>system</em> HTTP server rather
    /// than the admin API, and that server is off outside cloud environments, so it is turned on explicitly.
    /// </summary>
    static readonly string[] PortArgs =
        (Environment.GetEnvironmentVariable("E2E_SERVER_PORT_ARGS") is { Length: > 0 } harnessArgs
            ? harnessArgs
            : "--Environment:EnableSystemHttpServer=true\n--Environment:SystemHttpPort=8899")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The SDK's console shortcuts, off. A local server polls its standard input for a key between two turns
    /// of its main loop, and the poll blocks when that input is redirected, as a test runner's is. A blocked
    /// loop never sees the stop a SIGTERM requests, so a graceful restart would wait out the whole drain.
    /// </summary>
    const string KeyboardInputOff = "--Environment:EnableKeyboardInput=false";

    /// <summary> The system HTTP server, whose <c>/isReady</c> is polled. </summary>
    static readonly string SystemUrl =
        Environment.GetEnvironmentVariable("E2E_SERVER_SYSTEM_URL") is { Length: > 0 } url ? url.TrimEnd('/') : "http://127.0.0.1:8899";

    /// <summary>
    /// Every options file this server reads, in order, ending with the suites' pacing profile — beats and bot
    /// think delays at zero. <c>METAPLAY_OPTIONS</c> replaces the SDK's built-in list rather than appending to
    /// it, which is why the two built-in files are restated here; <c>AGENTS.md</c> says why that trade is the
    /// right way round. Paths are relative to the server's working directory, which
    /// <c>dotnet run --project</c> sets to the project directory.
    /// <para>
    /// Set here as well as in <c>e2e.toml</c> because this suite owns its own server process and is run on
    /// its own as often as it is run through the script. The readiness endpoint stays on the command line
    /// above: it is this suite's alone, and a file that turned it on for every server would collide with the
    /// one the script starts.
    /// </para>
    /// </summary>
    const string OptionsPaths = "Config/Options.base.yaml;Config/Options.local.yaml;Config/Options.e2e.yaml";

    /// <summary>
    /// A listener of the server's whose port closing is how "the old one is gone" is known: the WebSocket listener
    /// the web client connects to, or the stock TCP client port without the harness.
    /// </summary>
    static readonly int GameClientPort =
        int.TryParse(Environment.GetEnvironmentVariable("E2E_WS_PORT"), out int webSocketPort) ? webSocketPort : 9339;

    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>
    /// Where the server's own output goes, so a failed start says why: the harness's output directory, or the
    /// temporary directory without the harness.
    /// </summary>
    public static string LogPath => Path.Combine(
        Environment.GetEnvironmentVariable("E2E_OUTPUT_DIR") is { Length: > 0 } outputDir ? outputDir : Path.GetTempPath(),
        "stickypaws-owned-server.log");

    Process? _process;

    /// <summary> This server's own option overrides, so a restart comes back on the same pacing. </summary>
    readonly string[] _overrides;

    GameServerProcess(Process process, string[] overrides)
    {
        _process   = process;
        _overrides = overrides;
    }

    /// <summary>
    /// Start a server, on the suites' pacing profile and with the readiness endpoint on.
    /// <para>
    /// <paramref name="optionOverrides"/> are runtime-option assignments in the reader's own long form —
    /// <c>Match:TurnDeadline=00:00:05</c>, without the leading dashes — appended after the port set and
    /// therefore winning over every options file. They are here rather than in <c>Options.e2e.yaml</c>
    /// because that file is read by every suite and two fixtures assert against its numbers: the turn
    /// deadline there is five minutes <em>on purpose</em>, so a suite that needs a short one has to bring its
    /// own server rather than shorten everybody's. A restart keeps the same overrides.
    /// </para>
    /// </summary>
    public static async Task<GameServerProcess> StartAsync(params string[] optionOverrides)
    {
        string[] overrides = optionOverrides.Select(option => $"--{option}").ToArray();

        await WaitUntilPortClosedAsync(TimeSpan.FromSeconds(30));

        Process process = Start(overrides);
        await WaitUntilReadyAsync(TimeSpan.FromMinutes(2));

        return new GameServerProcess(process, overrides);
    }

    static Process Start(string[] overrides)
    {
        // --no-build, with the build done by the suite's own build step, so a restart is seconds rather than a
        // rebuild. The overrides come after the port set, so they win over it and over every options file.
        ProcessStartInfo info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (string arg in new[] { "run", "--no-build", "--project", "../../../../Backend/Server", "--" }.Concat(PortArgs).Append(KeyboardInputOff).Concat(overrides))
            info.ArgumentList.Add(arg);

        info.Environment["METAPLAY_OPTIONS"] = OptionsPaths;

        Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the game server");

        // Written to a file rather than dropped: a full pipe buffer would block the server's own console
        // writes, and a server that failed to start is otherwise a timeout with no reason attached.
        StreamWriter log = new StreamWriter(LogPath, append: true) { AutoFlush = true };
        process.OutputDataReceived += (_, args) => { if (args.Data != null) log.WriteLine(args.Data); };
        process.ErrorDataReceived  += (_, args) => { if (args.Data != null) log.WriteLine(args.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    /// <summary>
    /// Stop gracefully and start again — a rolling deploy. The server is asked to shut down and given time to
    /// drain rather than being killed, because that is the shape of the event the abandon path is about: the
    /// tables on this node end, and the accounts pointing at them find out on their next session.
    /// </summary>
    public async Task RestartAsync()
    {
        await StopGracefullyAsync();
        _process = Start(_overrides);
        await WaitUntilReadyAsync(TimeSpan.FromMinutes(2));
    }

    /// <summary>
    /// A real SIGTERM.
    /// <para>
    /// .NET's <c>Process.Kill</c> sends <b>SIGKILL</b> on Unix in both of its overloads — there is no SIGTERM
    /// anywhere in that API, whatever <c>entireProcessTree: false</c> suggests. Posting the signal with
    /// <c>kill</c> is what actually asks, and asking is the difference between a drain and a node loss.
    /// </para>
    /// </summary>
    async Task StopGracefullyAsync()
    {
        if (_process == null || _process.HasExited)
            return;

        using (Process signal = Process.Start(new ProcessStartInfo("/bin/kill", $"-TERM {_process.Id}") { UseShellExecute = false })!)
            await signal.WaitForExitAsync();

        // Long enough for the shutdown to drain, and then SIGKILL rather than hanging the suite: a server that
        // will not drain is a finding, but it must not be a fixture that never finishes.
        using (CancellationTokenSource drain = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
        {
            try
            {
                await _process.WaitForExitAsync(drain.Token);
            }
            catch (OperationCanceledException)
            {
                TestContext.Out.WriteLine("the server did not exit on SIGTERM within 45 s; killing it");
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }

        await WaitUntilPortClosedAsync(TimeSpan.FromSeconds(60));
    }

    /// <summary> SIGKILL. Nothing runs on the way out, which is the point. </summary>
    async Task KillAsync()
    {
        if (_process == null || _process.HasExited)
            return;

        _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync();

        await WaitUntilPortClosedAsync(TimeSpan.FromSeconds(60));
    }

    /// <summary> Readiness is polled, never slept. </summary>
    static async Task WaitUntilReadyAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                HttpResponseMessage response = await Http.GetAsync($"{SystemUrl}/isReady");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception)
            {
                // Not listening yet.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"The game server did not report ready within {timeout}. Its output is in {LogPath}");
    }

    static async Task WaitUntilPortClosedAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!await IsPortOpenAsync(GameClientPort))
                return;

            await Task.Delay(250);
        }

        throw new TimeoutException($"Port {GameClientPort} was still open after {timeout}; another game server is running");
    }

    static async Task<bool> IsPortOpenAsync(int port)
    {
        try
        {
            using TcpClient client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromSeconds(1));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // A kill, not a drain: the test is over, nothing is going to read what a graceful shutdown would
        // persist, and waiting out a drain here would add it to every fixture's cost.
        await KillAsync();
        _process?.Dispose();
        _process = null;
    }
}
