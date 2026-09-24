using Metaplay.Unity;
using System;
using System.IO;
using System.Threading.Tasks;

namespace WebClientBase.Integration;

/// <summary>
/// Offline server for the Blazor WebAssembly client. It runs the session in-process against the built-in game
/// config, with no game server. The "offline" environment selects it (see
/// <see cref="Configuration.StaticEnvironmentConfigProvider"/>), and the SDK finds it as the
/// <c>IOfflineServer</c> integration. In the browser, the SDK fetches the built-in config archive over HTTP from
/// <c>&lt;page base&gt;Assets/SharedGameConfig.mpa</c>. This class only replaces the error for a missing archive.
/// </summary>
public class BlazorOfflineServer : DefaultOfflineServer
{
    public override async Task InitializeAsync(MetaplayOfflineOptions offlineOptions)
    {
        try
        {
            await base.InitializeAsync(offlineOptions);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // A missing archive fetched over HTTP fails with an IOException, which the SDK's file-not-found error
            // does not cover. Name the archive and the command that builds it.
            throw new InvalidOperationException(
                $"Offline mode could not load the built-in game config from '{GetBuiltinGameConfigArchivePath()}'. " +
                "Generate it with: dotnet run --project tools/GameConfigGen",
                ex);
        }
    }
}
