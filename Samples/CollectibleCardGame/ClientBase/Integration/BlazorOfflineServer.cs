using Metaplay.Unity;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Game.ClientBase.Integration;

/// <summary>
/// Offline server for the Blazor WebAssembly client: runs the session in-process against the built-in game
/// config, with no game server involved. Selected by the "offline" environment (see
/// <see cref="Configuration.StaticEnvironmentConfigProvider"/>), and picked up automatically as the SDK's
/// <c>IOfflineServer</c> integration.
/// <para>
/// The SDK's own paths are correct here — in the browser it resolves the built-in config archive to
/// <c>&lt;page base&gt;Assets/SharedGameConfig.mpa</c> and fetches it over HTTP, which is where the
/// GameConfigGen tool writes it — so only the diagnostic for a missing archive is specialized.
/// </para>
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
            // Fetching the archive over HTTP reports a missing file as IOException, which the SDK's own
            // file-not-found diagnostic does not cover, so name the archive and how to produce it.
            throw new InvalidOperationException(
                $"Offline mode could not load the built-in game config from '{GetBuiltinGameConfigArchivePath()}'. " +
                "Generate it with: dotnet run --project Tools/GameConfigGen",
                ex);
        }
    }
}
