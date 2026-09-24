using Game.Logic;
using Metaplay.Core;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Services;
using WebClientBase.Configuration;

namespace WebClient.Tests;

/// <summary>
/// The app's client service with no session, for the render tests. <c>PlayerModel</c> is null and every
/// session-backed method declines, so the shell's screens draw the fixture data. The table and the matchmaking state
/// that a session would deliver are set by the test instead: <see cref="ShowTable"/>, <see cref="SetMatchmakingStatus"/>
/// and <see cref="SetMatchmakingSeatDeadlineAt"/>. Until a test sets them, they hold the values of a client with no
/// session: no table, not searching, and no seat deadline.
/// <para>
/// Both <c>ConnectAsync</c> and the SDK core initialization are skipped: this assembly is a plain net10.0 test
/// host with no pre-built serializer and no integration metadata, and a render without a session does not need
/// the initialization.
/// </para>
/// </summary>
public class NoSessionClientService : MetaplayClientService
{
    /// <summary>The table set by <see cref="ShowTable"/>, or null when the player is at no table.</summary>
    public override MatchModel? Match => _match;

    /// <summary>The status set by <see cref="SetMatchmakingStatus"/>.</summary>
    public override MatchmakingStatus MatchmakingStatus => _matchmakingStatus;

    /// <summary>The deadline set by <see cref="SetMatchmakingSeatDeadlineAt"/>.</summary>
    public override MetaTime MatchmakingSeatDeadlineAt => _matchmakingSeatDeadlineAt;

    private MatchModel?       _match;
    private MatchmakingStatus _matchmakingStatus         = MatchmakingStatus.NotSearching;
    private MetaTime          _matchmakingSeatDeadlineAt = MetaTime.Epoch;

    protected override void EnsureCoreInitialized()
    {
        // No SDK core initialization: the test host never opens a session, and the entry assembly carries no
        // integration metadata the initialization would look for.
    }

    public override Task ConnectAsync() => Task.CompletedTask;

    /// <summary>Set <see cref="Match"/> and notify the rendered components of the change.</summary>
    public void ShowTable(MatchModel? match)
    {
        _match = match;
        NotifyStateChanged();
    }

    /// <summary>Set <see cref="MatchmakingStatus"/> and notify the rendered components of the change.</summary>
    public void SetMatchmakingStatus(MatchmakingStatus status)
    {
        _matchmakingStatus = status;
        NotifyStateChanged();
    }

    /// <summary>Set <see cref="MatchmakingSeatDeadlineAt"/> and notify the rendered components of the change.</summary>
    public void SetMatchmakingSeatDeadlineAt(MetaTime deadline)
    {
        _matchmakingSeatDeadlineAt = deadline;
        NotifyStateChanged();
    }
}

/// <summary>The service registrations that every bUnit fixture rendering the app's components needs.</summary>
internal static class NoSessionServices
{
    /// <summary>
    /// Registers <paramref name="client"/> both as itself, for the test to drive, and as the
    /// <see cref="MetaplayClientService"/> the app's components inject. Also registers the app's
    /// <see cref="WebClientConfig"/>, because the pages read the title and logo from it. The client is registered
    /// through a factory so that the container disposes it with the test context.
    /// </summary>
    public static NoSessionClientService AddNoSessionClient(this IServiceCollection services, NoSessionClientService client)
    {
        services.AddSingleton(_ => client);
        services.AddScoped<MetaplayClientService>(sp => sp.GetRequiredService<NoSessionClientService>());
        services.AddSingleton(new WebClientConfig("Table Stakes", "🃏", GameTheme.Colors));
        return client;
    }
}
