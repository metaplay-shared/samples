using NUnit.Framework;
using System.Diagnostics;
using System.Net.Sockets;

namespace WebClient.Tests;

/// <summary>
/// A game server that a test fixture starts and restarts itself, so it can test what a server restart does to a
/// table in progress.
/// <para>
/// The fixture cannot share ports with another game server, so the E2E harness runs it in a phase with
/// <c>server = false</c> (<c>e2e.toml</c>), after it has stopped the shared server. It binds the ports that server had,
/// which the harness passes in <c>E2E_SERVER_PORT_ARGS</c>, so the web client the harness keeps running reaches it.
/// Run by hand without the harness, it binds the default ports, and no other game server may be running.
/// </para>
/// <para>
/// The server keeps its database in its own directory, emptied when the fixture starts, so a restart finds the
/// tables the run saved and nothing from an earlier run.
/// </para>
/// </summary>
public sealed class GameServerProcess : IAsyncDisposable
{
    /// <summary>The listener arguments from the harness, or the default ports with the system HTTP server on.</summary>
    static readonly string[] PortArgs =
        (E2EStack.Setting("E2E_SERVER_PORT_ARGS") ?? "--Environment:EnableSystemHttpServer=true\n--Environment:SystemHttpPort=8899")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The arguments the harness started the shared server with, so this server runs at the same pace.</summary>
    static readonly string[] ServerArgs =
        (E2EStack.Setting("E2E_SERVER_ARGS") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The server's console shortcuts, off. A local server polls standard input for a key on its main loop, and the
    /// poll can block when the input is redirected, as it is here.
    /// </summary>
    const string KeyboardInputOff = "--Environment:EnableKeyboardInput=false";

    /// <summary>The system HTTP server, for <c>/isReady</c> and <c>/gracefulShutdown</c>.</summary>
    static readonly string SystemUrl = (E2EStack.Setting("E2E_SERVER_SYSTEM_URL") ?? "http://127.0.0.1:8899").TrimEnd('/');

    /// <summary>The WebSocket port the web client connects to. It closing shows that a stopped server is gone.</summary>
    static readonly int WebSocketPort = int.Parse(E2EStack.WebSocketPort);

    static readonly string OutputDir = E2EStack.Setting("E2E_OUTPUT_DIR") ?? Path.GetTempPath();

    static readonly string DatabaseDir = Path.Combine(OutputDir, "owned-server-db");

    /// <summary>The server's output, so a failed start says why.</summary>
    public static string LogPath => Path.Combine(OutputDir, "owned-server.log");

    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>One writer for every start, because a restart's output goes to the same file.</summary>
    static readonly Lazy<StreamWriter> Log = new Lazy<StreamWriter>(() => new StreamWriter(LogPath, append: true) { AutoFlush = true });

    Process? _process;

    GameServerProcess(Process process)
    {
        _process = process;
    }

    /// <summary>Start a server with an empty database and wait until it is ready.</summary>
    public static async Task<GameServerProcess> StartAsync()
    {
        await WaitUntilPortClosedAsync(TimeSpan.FromSeconds(30));

        if (Directory.Exists(DatabaseDir))
            Directory.Delete(DatabaseDir, recursive: true);
        Directory.CreateDirectory(DatabaseDir);

        // The fixture gets no handle to dispose if the start fails, so kill the server here. Left running, it would
        // hold the ports of every later phase.
        GameServerProcess server = new GameServerProcess(Start());
        try
        {
            await WaitUntilReadyAsync(TimeSpan.FromMinutes(2));
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
        return server;
    }

    static Process Start()
    {
        // --no-build, because the harness built the server. The working directory is the test assembly's output
        // directory, so the server project is four levels up.
        ProcessStartInfo info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        IEnumerable<string> args = new[] { "run", "--no-build", "--no-launch-profile", "--project", "../../../../Backend/Server", "--" }
            .Concat(PortArgs)
            .Append($"--Database:SqliteDirectory={DatabaseDir}")
            .Append(KeyboardInputOff)
            .Concat(ServerArgs);
        foreach (string arg in args)
            info.ArgumentList.Add(arg);

        // The output goes to a file. Unread, a full pipe would block the server's console writes. The file is opened
        // first, so a failure to open it cannot leave a server running that the fixture does not know about.
        StreamWriter log = Log.Value;

        Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the game server");
        process.OutputDataReceived += (_, line) => { if (line.Data != null) lock (log) log.WriteLine(line.Data); };
        process.ErrorDataReceived  += (_, line) => { if (line.Data != null) lock (log) log.WriteLine(line.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>
    /// Stop the server gracefully and start it again on the same database, as a deploy does. The graceful stop lets
    /// every table's actor save its state.
    /// </summary>
    public async Task RestartAsync()
    {
        await StopGracefullyAsync();
        _process?.Dispose();
        _process = Start();
        await WaitUntilReadyAsync(TimeSpan.FromMinutes(2));
    }

    /// <summary>
    /// Ask the server to shut down through its system HTTP server, which works on every platform, and wait for it to
    /// exit. A server that does not exit in time is killed, so the fixture cannot hang.
    /// </summary>
    async Task StopGracefullyAsync()
    {
        if (_process == null || _process.HasExited)
            return;

        HttpResponseMessage response = await Http.PostAsync($"{SystemUrl}/gracefulShutdown", content: null);
        response.EnsureSuccessStatusCode();

        using (CancellationTokenSource drain = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
        {
            try
            {
                await _process.WaitForExitAsync(drain.Token);
            }
            catch (OperationCanceledException)
            {
                TestContext.Out.WriteLine("The game server did not exit within 60 s of a graceful shutdown. Killing it.");
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }

        await WaitUntilPortClosedAsync(TimeSpan.FromSeconds(60));
    }

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
            if (!await IsPortOpenAsync(WebSocketPort))
                return;

            await Task.Delay(250);
        }

        throw new TimeoutException($"Port {WebSocketPort} was still open after {timeout}. Another game server is running.");
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

    /// <summary>Kill the server. The test is over, so nothing needs the state a graceful stop would save.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_process != null && !_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process?.Dispose();
        _process = null;
    }
}
