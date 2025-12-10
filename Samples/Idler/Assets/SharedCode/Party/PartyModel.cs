
using System.Collections.Generic;
using System.Runtime.Serialization;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;

namespace Game.Logic
{
    [MetaSerializable]
    public class PartyChatMessage
    {
        // The Id of the player that sent the message. Only members can send messages so information about the sender
        // can be found in the `Members` collection.
        [MetaMember(1)] public EntityId FromPlayerId { get; }
        // The payload of the message.
        [MetaMember(2)] public string Message { get; }
        // The time of sending the message, added by the server.
        [MetaMember(3)] public MetaTime Timestamp { get; }

        [MetaDeserializationConstructor]
        public PartyChatMessage(EntityId fromPlayerId, string message, MetaTime timestamp)
        {
            FromPlayerId = fromPlayerId;
            Message = message;
            Timestamp = timestamp;
        }
    }

    [MetaSerializable]
    public class PartyMember
    {
        // The PlayerName property of the PlayerModel of the member, kept up-to-date for online players.
        // Any other PlayerModel data that we'd like to expose in the party would be shadowed in a similar manner.
        [MetaMember(1)] public string Name;
        // Whether this PartyMember represents an active member of the party or a historical entry. Note that
        // players can re-join a party so a historical entry can become an active member again.
        [MetaMember(2)] public bool IsOnline;

        [MetaDeserializationConstructor]
        public PartyMember(string name, bool isOnline)
        {
            Name = name;
            IsOnline = isOnline;
        }

        // Helper method for cloning a PartyMember instance and setting online status.
        public PartyMember WithOnlineStatus(bool isOnline)
        {
            return new PartyMember(Name, isOnline);
        }
    }

    public interface IPartyModelClientListener
    {
        void NewChatMessage(PartyChatMessage message);
        void MemberUpdated(EntityId member);
    }

    [MetaSerializableDerived(5)]
    [SupportedSchemaVersions(1, 1)]
    public class PartyModel : MultiplayerModelBase<PartyModel>
    {
        // The EntityId of the Player Entity that created this Party.
        [MetaMember(1)] public EntityId Creator { get; set; }
        // Public information about party members.
        [MetaMember(2)] public MetaDictionary<EntityId, PartyMember> Members { get; set; } = new MetaDictionary<EntityId, PartyMember>();
        // A bounded set of previous chat messages.
        [MetaMember(3)] public Queue<PartyChatMessage> ChatLog { get; set; } = new Queue<PartyChatMessage>();

        // Client-side side effects interface for UI updates.
        [IgnoreDataMember] public IPartyModelClientListener ClientListener { get; set; }

        // We aren't interested in ticking functionality for the Party model in this
        // implementation, as all mutations will happen through actions.
        public override int TicksPerSecond => 1;
        public override void OnTick() { }
        public override void OnFastForwardTime(MetaDuration elapsedTime) {}
    }
}