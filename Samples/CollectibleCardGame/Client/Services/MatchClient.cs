using Game.ClientBase.Services;
using Game.Logic;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Unity;

namespace Game.Client.Services;

/// <summary>
/// The client's half of the match protocol: one sub-client on its own client slot. The server associates the
/// match, the SDK downloads and syncs the model and delivers this seat's hand as private state, and the
/// context listens on the match's entity channel for the directed messages.
/// <para>
/// <b>The client is never told to go find a match; it is told which one it is in</b>
/// (<c>Docs/client.md</c>).
/// </para>
/// </summary>
public class MatchClient : MultiplayerEntityClientBase<MatchModel, MatchClientContext>
{
    readonly MatchService _service;

    public MatchClient(MatchService service)
    {
        _service = service;
    }

    public override ClientSlot ClientSlot => ClientSlotGame.Match;

    protected override string LogChannelName => "match";

    protected override MatchClientContext CreateActiveModelContext(EntityInitialState state, ISharedGameConfig gameConfig)
    {
        MatchModel model = DefaultDeserializeModel(state.State, gameConfig);
        return new MatchClientContext(DefaultInitArgs(model, state), _service);
    }

    /// <summary>
    /// A timeline update did not hash to what the server said applying it would produce. The SDK's default
    /// closes the connection with a generic terminal error, which is wrong in both halves: a shell
    /// classifying it reports an unreachable server, and <em>terminal</em> stops the very reconnect that
    /// fetches the fresh copy. The game names it instead, as a transient error the shell recognizes and
    /// reports as re-syncing (<c>Docs/client.md</c>, "When the connection drops").
    /// </summary>
    public override void OnTimelineUpdateFailed()
    {
        MetaplaySDK.Connection?.CloseWithError(new EntityTimelineDesyncConnectionError(Model?.EntityId ?? default));
    }
}
