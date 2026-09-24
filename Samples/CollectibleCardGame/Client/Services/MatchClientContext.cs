using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.MultiplayerEntity;

namespace Game.Client.Services;

/// <summary>
/// The client's context for one match channel: the model, its clock, and the directed refusal the table sends
/// this seat.
/// <para>
/// <b>The listeners are bound in the constructor.</b> The SDK flushes the channel's buffered messages before
/// it calls the activation hooks, so a listener bound in a hook misses a refusal buffered while the match's
/// config downloaded (<c>Docs/protocol.md</c>, "Entity channel listeners must be rebound on every
/// activation"). The constructor runs on every activation, which is also the first-frame signal: a reconnect
/// to the same table carries the same entity id, so identity is not.
/// </para>
/// </summary>
public class MatchClientContext : MultiplayerEntityClientContext<MatchModel>
{
    readonly MatchService _service;

    public MatchClientContext(ClientMultiplayerEntityContextInitArgs args, MatchService service) : base(args)
    {
        _service = service;

        _messageDispatcher.AddListener<MatchIntentRefused>(OnIntentRefused);

        _service.OnMatchAttached(this, args.PlayerId);
    }

    // The match actor accepts no direct (UDP) connection; everything travels over the session.
    protected override bool EnableDirectConnection => false;

    /// <summary>
    /// The model's clock as of this frame: the server's clock as the timeline delivers it, carried forward
    /// between ticks (<see cref="ModelClock"/>).
    /// </summary>
    public MetaTime ModelNow
        => ModelClock.Interpolate(CommittedModel.CurrentTime, CurrentTickPresentationAt, MetaTime.Now, CommittedModel.TicksPerSecond);

    /// <summary> Send one message to the table on this channel. </summary>
    public void Send(MetaMessage message) => _messageDispatcher.SendMessage(message);

    public override void OnEntityDetached()
    {
        _service.OnMatchDetached();
        base.OnEntityDetached();
    }

    void OnIntentRefused(MatchIntentRefused refusal) => _service.OnIntentRefused(refusal);
}
