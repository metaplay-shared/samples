using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Client to server: request a spin. It carries only the expected ordinal. The server decides the sector,
    /// prize and table, so a client cannot choose its result (<c>docs/spin-wheel.md</c>).
    /// <para>
    /// The ordinal rejects replays using only the player state. A retransmission or a stale tab names an ordinal
    /// the player has passed and is refused. A duplicate of the spin that just resolved finds its unacknowledged
    /// receipt and gets that answer.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.PlayerWheelSpinRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerWheelSpinRequest : MetaMessage
    {
        /// <summary>The ordinal this spin is expected to have: the player's resolved spin count plus one.</summary>
        public int ExpectedOrdinal { get; private set; }

        PlayerWheelSpinRequest() { }

        public PlayerWheelSpinRequest(int expectedOrdinal)
        {
            ExpectedOrdinal = expectedOrdinal;
        }

        public override string ToString() => $"spin the wheel (expecting ordinal {ExpectedOrdinal})";
    }

    /// <summary>
    /// Server to client: whether a spin request was accepted, with the refusal reason so the client can show a
    /// specific message. It does not carry the spin result. An accepted spin reaches the client as the
    /// <see cref="PlayerWheelSpinResolved"/> action, which updates the wallet and receipt together and survives a
    /// reconnect.
    /// </summary>
    [MetaMessage(MessageCodes.PlayerWheelSpinResponse, MessageDirection.ServerToClient)]
    public class PlayerWheelSpinResponse : MetaMessage
    {
        /// <summary>Why the spin was refused, or <see cref="SpinRefusal.None"/> if it was not.</summary>
        public SpinRefusal Refusal { get; private set; }

        /// <summary>The spin ordinal this response is about.</summary>
        public int Ordinal { get; private set; }

        PlayerWheelSpinResponse() { }

        public PlayerWheelSpinResponse(SpinRefusal refusal, int ordinal)
        {
            Refusal = refusal;
            Ordinal = ordinal;
        }

        /// <summary>
        /// Whether the spin was accepted. This is a method, not a property, because the serializer treats every
        /// public property of a message as a member and fails at startup on one without a setter.
        /// </summary>
        public bool IsAccepted() => Refusal == SpinRefusal.None;

        public override string ToString() => IsAccepted() ? $"spin {Ordinal} accepted" : $"spin refused: {Refusal}";
    }
}
