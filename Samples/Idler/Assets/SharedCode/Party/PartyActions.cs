using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    [MetaSerializable]
    public abstract class PartyAction : ModelAction<PartyModel> {}

    [ModelActionExecuteFlags(ModelActionExecuteFlags.LeaderSynchronized)]
    public abstract class PartyServerAction : PartyAction
    {
    }

    [ModelActionExecuteFlags(ModelActionExecuteFlags.FollowerSynchronized)]
    public abstract class PartyClientAction : PartyAction
    {
        public virtual bool ValidateOnServer(EntityId issuer) => true;
    }

    [ModelAction(1)]
    public class SendPartyMessage : PartyClientAction
    {
        private string Message { get; set; }
        private EntityId FromPlayer { get; set; }
        private MetaTime Timestamp { get; set; }

        SendPartyMessage() {}

        public SendPartyMessage(string message)
        {
            Message = message;
        }

        public override bool ValidateOnServer(EntityId issuer)
        {
            // Inject timestamp & sender on server, before action is executed on either end.
            Timestamp = MetaTime.Now;
            FromPlayer = issuer;
            return true;
        }

        public override MetaActionResult InvokeExecute(PartyModel model, bool commit)
        {
            if (commit)
            {
                PartyChatMessage message = new PartyChatMessage(FromPlayer, Message, Timestamp);

                model.ChatLog.Enqueue(message);
                while (model.ChatLog.Count > 10)
                    model.ChatLog.Dequeue();
                model.ClientListener?.NewChatMessage(message);
            }
            return MetaActionResult.Success;
        }
    }

    [ModelAction(2)]
    public class UpdatePartyMember : PartyServerAction
    {
        private EntityId MemberId { get; }
        private PartyMember State { get; }

        [MetaDeserializationConstructor]
        public UpdatePartyMember(EntityId memberId, PartyMember state)
        {
            MemberId = memberId;
            State = state;
        }

        public override MetaActionResult InvokeExecute(PartyModel model, bool commit)
        {
            if (commit)
            {
                model.Members[MemberId] = State;
                model.ClientListener?.MemberUpdated(MemberId);
            }
            return MetaActionResult.Success;
        }
    }
}