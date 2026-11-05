using WebClientBase.Services;
using Game.Logic;
using Metaplay.Client;
using Metaplay.Core.Client;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Player;
using Metaplay.Core.Session;
using Metaplay.Unity;

namespace WebClient.Services;

/// <summary>
/// Game-specific Metaplay client service that manages the Metaplay client connection. Inherits connection
/// lifecycle management from <see cref="MetaplayClientServiceBase{TModel}"/> and implements the player model
/// client listener so model changes re-render the UI.
/// </summary>
public class MetaplayClientService : MetaplayClientServiceBase<PlayerModel>, IPlayerModelClientListener, IPlayerModelClientListenerCore, IRootPlayerModelChangeObserver
{
    // The subsystem services are owned here rather than injected because they reference each other: this
    // service is the observer hub the shared code dispatches through, and each subsystem service is a handle it
    // dispatches to. Program.cs registers them out of this instance.
    public CollectionService Collection { get; }
    public MatchService Match { get; }
    public MatchmakingService Matchmaking { get; }
    public CommunityService Community { get; }

    /// <summary>
    /// The match sub-client. Held so the model's own client listener can be attached to the current model and
    /// every future one at session start.
    /// </summary>
    readonly MatchClient _matchClient;

    public MetaplayClientService()
    {
        Collection   = new CollectionService(this);
        Match        = new MatchService(this);
        Matchmaking  = new MatchmakingService(this, Match);
        Community    = new CommunityService(this);
        _matchClient = new MatchClient(Match);
    }

    /// <summary>
    /// Create the Metaplay client, with the match sub-client on its own client slot. Creating the client
    /// starts the SDK, whose top-level message dispatcher carries the messages that ride the player session,
    /// so their listeners are bound here, once per client.
    /// </summary>
    protected override IMetaplayClient CreateClient()
    {
        IMetaplayClient client = MetaplayClient.Create(additionalClients: new IMetaplaySubClient[] { _matchClient });
        Matchmaking.BindListeners();
        Community.BindListeners();
        return client;
    }

    /// <summary>
    /// The UI renders when something changes rather than on every frame: the board renders on its own frame
    /// signature, and the meta screens are static between changes. A change the player model makes outside a client
    /// action reaches the UI through <see cref="IRootPlayerModelChangeObserver.GenericPropertyChanged"/>, and the
    /// subsystem services raise their own events.
    /// </summary>
    protected override bool RenderEveryFrame => false;

    /// <summary>
    /// Execute a player action once the current handler has returned, as a work item queued on the browser's one
    /// thread, rather than inline: a model listener can render a component synchronously, and an action that render
    /// fires would otherwise run inside the action that raised the listener, which the SDK refuses. The render the
    /// action requests lands on the next frame. The action has not run when this returns, so there is no result:
    /// null. The SDK sends the action whatever it returns, so a caller that needs the verdict asks
    /// <see cref="DryExecuteAction"/> first.
    /// </summary>
    public override MetaActionResult? ExecuteAction(PlayerActionBase action)
    {
        MetaplaySDK.RunOnMainThreadAsync(() => base.ExecuteAction(action));
        return null;
    }

    /// <summary>
    /// What the action would return against the current model, without changing it. Null before a session has
    /// started.
    /// </summary>
    public MetaActionResult? DryExecuteAction(PlayerActionBase action) => Session?.PlayerContext?.DryExecuteAction(action);

    /// <summary> The player's chosen display name, or empty before a session has started. </summary>
    public string DisplayName => PlayerModel?.DisplayName ?? "";

    /// <summary> Rename the player. Validation lives in the shared action, so the server refuses the same inputs. </summary>
    public void SetDisplayName(string displayName) => ExecuteAction(new PlayerSetDisplayName(displayName));

    /// <summary>
    /// Called when a session has started. Wire up the client listeners so model changes re-render the UI.
    /// </summary>
    protected override void OnSessionStarted(MetaplaySession session)
    {
        if (session.PlayerContext.Model is PlayerModel playerModel)
        {
            playerModel.ClientListener     = this;
            playerModel.ClientListenerCore = this;
        }

        // Applied to the match model the session starts with and to every future one. Without it the board
        // never re-renders, because the match actions' listener calls all no-op.
        _matchClient.SetClientListeners(model => ((MatchModel)model).ClientListener = Match);

        Community.OnSessionStarted();
        Matchmaking.OnSessionStarted();
    }

    #region Model change observers (trigger UI re-render)

    // The observer hub: one typed handle per subsystem. This service observes the root model itself.
    IRootPlayerModelChangeObserver IPlayerModelClientListener.Root => this;

    ICollectionChangeObserver IPlayerModelClientListener.Collection => Collection;

    void IRootPlayerModelChangeObserver.GenericPropertyChanged(string propertyName) => RequestRender();

    // The SDK's own server actions report through the core listener, such as a rename from the dashboard.
    void IPlayerModelClientListenerCore.OnPlayerNameChanged(string newName) => RequestRender();

    #endregion
}
