using Game.ClientBase.Services;
using Game.Logic;
using Metaplay.Client;
using Metaplay.Core.Client;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Session;

namespace Game.Client.Services;

/// <summary>
/// Game-specific Metaplay client service that manages the Metaplay client connection. Inherits connection
/// lifecycle management from <see cref="MetaplayClientServiceBase{TModel}"/> and implements the player model
/// client listener so model changes re-render the UI.
/// </summary>
public class MetaplayClientService : MetaplayClientServiceBase<PlayerModel>, IPlayerModelClientListener, IRootPlayerModelChangeObserver
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
            playerModel.ClientListener = this;

        // Applied to the match model the session starts with and to every future one. Without it the board
        // never re-renders, because the match actions' listener calls all no-op.
        _matchClient.SetClientListeners(model => ((MatchModel)model).ClientListener = Match);

        Community.OnSessionStarted();
    }

    #region Model change observers (trigger UI re-render)

    // The observer hub: one typed handle per subsystem. This service observes the root model itself.
    IRootPlayerModelChangeObserver IPlayerModelClientListener.Root => this;

    ICollectionChangeObserver IPlayerModelClientListener.Collection => Collection;

    void IRootPlayerModelChangeObserver.GenericPropertyChanged(string propertyName) => NotifyStateChanged();

    #endregion
}
