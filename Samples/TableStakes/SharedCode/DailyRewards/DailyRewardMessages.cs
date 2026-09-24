using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Client to server: request today's daily reward.
    /// <para>
    /// The message has <b>no fields</b>. The server decides the day, the step, the new streak and whether the
    /// skip day applies from the published schedule and the player state, so a client cannot request a larger
    /// reward or a future day's reward (<c>docs/daily-rewards.md</c>).
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.PlayerDailyRewardClaimRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerDailyRewardClaimRequest : MetaMessage
    {
        public static readonly PlayerDailyRewardClaimRequest Instance = new PlayerDailyRewardClaimRequest();

        public override string ToString() => "claim daily reward";
    }

    /// <summary>
    /// Server to client: the result of a daily reward claim, with the refusal reason so the client can show a
    /// specific message.
    /// <para>
    /// It does not carry the new state. An accepted claim reaches the client as the
    /// <see cref="PlayerDailyRewardClaimed"/> action, which updates the wallet and streak together and survives a
    /// reconnect. This message only says whether that action is coming.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.PlayerDailyRewardClaimResponse, MessageDirection.ServerToClient)]
    public class PlayerDailyRewardClaimResponse : MetaMessage
    {
        /// <summary>Why the claim was refused, or <see cref="DailyRewardRefusal.None"/> if it was not.</summary>
        public DailyRewardRefusal Refusal { get; private set; }

        /// <summary>The activation index (<see cref="DailyActivation.Index"/>) this response is about.</summary>
        public int ActivationIndex { get; private set; }

        PlayerDailyRewardClaimResponse() { }

        public PlayerDailyRewardClaimResponse(DailyRewardRefusal refusal, int activation)
        {
            Refusal         = refusal;
            ActivationIndex = activation;
        }

        /// <summary>
        /// Whether the claim was accepted. This is a method, not a property, because the serializer treats every
        /// public property of a message as a member and fails at startup on one without a setter.
        /// </summary>
        public bool IsAccepted() => Refusal == DailyRewardRefusal.None;

        public override string ToString() => IsAccepted() ? $"daily reward claimed for day {ActivationIndex}" : $"daily reward refused: {Refusal}";
    }
}
