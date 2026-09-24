using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Client to server: a request to change the player's display name to <see cref="NewName"/>.
    /// <para>
    /// The server validates the name with <see cref="DisplayNamePolicy"/>. The client may run the same check
    /// for immediate feedback, but only the server's check decides (<c>docs/player.md</c>, "Name rules").
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.PlayerRenameRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerRenameRequest : MetaMessage
    {
        public string NewName { get; private set; }

        public PlayerRenameRequest() { }
        public PlayerRenameRequest(string newName) { NewName = newName; }

        public override string ToString() => $"rename to '{NewName}'";
    }

    /// <summary>
    /// Server to client: the result of a <see cref="PlayerRenameRequest"/>. A refusal includes the reason, so
    /// the client can tell the player why the name was not accepted.
    /// </summary>
    [MetaMessage(MessageCodes.PlayerRenameResponse, MessageDirection.ServerToClient)]
    public class PlayerRenameResponse : MetaMessage
    {
        /// <summary>Why the rename was refused, or <see cref="DisplayNameRefusal.None"/> if it was not.</summary>
        public DisplayNameRefusal Refusal { get; private set; }

        /// <summary>The player's name after the request: the new name if accepted, the unchanged name if refused.</summary>
        public string Name { get; private set; }

        PlayerRenameResponse() { }

        public PlayerRenameResponse(DisplayNameRefusal refusal, string name)
        {
            Refusal   = refusal;
            Name      = name;
        }

        /// <summary>
        /// Whether the rename was accepted. This is a method rather than a property because every public
        /// property of a message is serialized, and a get-only property causes a serializer error at startup.
        /// </summary>
        public bool IsAccepted() => Refusal == DisplayNameRefusal.None;

        public override string ToString() => IsAccepted() ? $"renamed to '{Name}'" : $"rename refused: {Refusal}";
    }
}
