// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Client;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Session;
using System;
using System.Threading.Tasks;

/// <summary>
/// Environment configs for the raw client: the server endpoint, CDN, and logging settings.
/// The SDK discovers this <see cref="IEnvironmentConfigProvider"/> from the application assembly
/// and uses it to configure connections. The single environment here connects to a game server
/// running locally (see Backend/Server); edit or extend it to connect to other environments.
/// </summary>
public class RawClientEnvironmentConfigProvider : IEnvironmentConfigProvider
{
    static readonly EnvironmentConfig _localhost = new EnvironmentConfig
    {
        Id = "localhost",
        DisplayName = "Local Development",
        ConnectionEndpointConfig = new ConnectionEndpointConfig
        {
            ServerHost = "localhost",
            ServerPort = 9339,
            EnableTls = false,
            CdnBaseUrl = "http://localhost:5552/",
            BackupGateways = new ServerGatewaySpec[] { },
        },
        ClientLoggingConfig = new ClientLoggingConfig { LogLevel = LogLevel.Information },
    };

    public void InitializeSingleton() { }
    public EnvironmentConfig GetCurrent() => _localhost;
}

/// <summary>
/// A minimal "raw" .NET console client for the HelloWorld sample: runs the same shared game
/// logic as the Unity client, driven by the engine-agnostic Metaplay client integration
/// without any game engine.
/// </summary>
public static class Program
{
    // The pending console read, if any. \note The blocking read runs in a thread pool task:
    // Console.In.ReadLineAsync() is not used as it can execute synchronously and block the
    // calling thread. The task is kept across sessions so that a line arriving while no
    // session is ongoing is handled by the next session.
    static Task<string> _pendingInput;

    public static async Task<int> Main(string[] args)
    {
        // Initialize the Metaplay core. The game's integration types (PlayerModel, actions,
        // the environment config provider above, ...) are discovered from this assembly, and
        // the serializer is generated at runtime.
        MetaplayCore.InitializeForExternalApp("RawClient");

        // Create the client. This starts the SDK. The server endpoint and other environment
        // settings come from the RawClientEnvironmentConfigProvider above; client options and
        // delegates are passed here. All Create() arguments are optional — the omitted ones below
        // (connectionDelegate, localizationDelegate, analyticsDelegate, socialAuthenticationDelegate,
        // gameConfigDelegate, connectionConfig, offlineOptions, ...) use their defaults.
        // \note Safe to create outside the frame loop: the frame loop is not running yet.
        using (IMetaplayClient client = MetaplayClient.Create(
            buildVersion: new BuildVersion(version: "1.0.0", buildNumber: "1", commitId: "")))
        {
            // Run the application logic in a frame loop, see FrameLoop.
            await FrameLoop.RunAsync(() => RunSessionLoopAsync(client));
        }

        Console.WriteLine("Bye!");
        return 0;
    }

    /// <summary>
    /// The async connection loop, see <see cref="IMetaplayClient.ConnectAsync"/>.
    /// </summary>
    static async Task RunSessionLoopAsync(IMetaplayClient client)
    {
        for (;;)
        {
            Console.WriteLine("Connecting...");
            MetaplaySession session;
            try
            {
                session = await client.ConnectAsync();
            }
            catch (FailedToStartSessionException ex)
            {
                Console.WriteLine($"Failed to start session: {ex.Failure.EnglishLocalizedReason}");
                Console.WriteLine("Is the game server running? Start it with: dotnet run --project ../Backend/Server");
                return;
            }

            // A session has been established and the player state is available.
            session.SessionStartComplete();
            PlayerModel player = (PlayerModel)session.PlayerContext.Model;
            Console.WriteLine($"Session started: playerId={session.PlayerId}, playerName={player.PlayerName}, numClicks={player.NumClicks}");
            Console.WriteLine("Press Enter to click the button, or type 'q' + Enter to quit.");

            // Run the in-session logic until the user quits or the session ends.
            bool quitRequested = await RunInSessionLogicAsync(session);
            if (quitRequested)
                return;

            // The session ended on us: report the reason, and reconnect if the session
            // ended due to a transient reason.
            ConnectionLostEvent connectionLost = await session.WaitForSessionEndAsync();
            Console.WriteLine($"Session ended: {connectionLost.EnglishLocalizedReason}");
            if (!connectionLost.AutoReconnectRecommended)
                return;
        }
    }

    /// <summary>
    /// The in-session logic: handle console input until the session ends. Returns true if
    /// the session was closed deliberately because the user quit, false if the session
    /// ended on its own. \note This game-level outcome is the application's own state: the
    /// initiator of a session close knows why it closed, and that reason is not routed
    /// through the SDK.
    /// </summary>
    static async Task<bool> RunInSessionLogicAsync(MetaplaySession session)
    {
        for (;;)
        {
            // Keep a console read pending.
            _pendingInput ??= Task.Run(Console.In.ReadLine);

            // Wake up when the session ends or a line of console input arrives.
            Task<ConnectionLostEvent> sessionEndTask = session.WaitForSessionEndAsync();
            await Task.WhenAny(sessionEndTask, _pendingInput);

            // \note Check the session end first: the session may have ended in the same
            //       frame in which the console read completed.
            if (sessionEndTask.IsCompleted)
                return false;

            string line = _pendingInput.Result;
            _pendingInput = null;

            if (line == null || line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                // Quit (or end of piped input): end the session gracefully. Pending
                // actions are flushed to the server before the connection closes.
                Console.WriteLine("Closing session...");
                await session.CloseAsync();
                return true;
            }

            // Execute a game action on the player: the action runs the shared game
            // logic locally and is sent to the server, which executes the same
            // logic and verifies the resulting state.
            session.PlayerContext.ExecuteAction(new PlayerClickButton());
            Console.WriteLine($"Clicked! numClicks={((PlayerModel)session.PlayerContext.Model).NumClicks}");
        }
    }
}
