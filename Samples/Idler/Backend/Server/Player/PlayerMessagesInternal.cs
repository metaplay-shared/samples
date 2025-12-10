// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Game.Logic.TypeCodes;
using Metaplay.Cloud.Entity;
using Metaplay.Core;

namespace Game.Server.Player
{
    // A place for for game-specific server-internal PlayerActor-related messages.

    [MetaMessage(MessageCodes.InternalPlayerGetBattleAttackParamsRequest, MessageDirection.ServerInternal)]
    public class InternalPlayerGetBattleAttackParamsRequest : EntityAskRequest<InternalPlayerGetBattleAttackParamsResponse>
    {
        public static readonly InternalPlayerGetBattleAttackParamsRequest Instance = new InternalPlayerGetBattleAttackParamsRequest();
        InternalPlayerGetBattleAttackParamsRequest() { }
    }

    [MetaMessage(MessageCodes.InternalPlayerGetBattleAttackParamsResponse, MessageDirection.ServerInternal)]
    public class InternalPlayerGetBattleAttackParamsResponse : EntityAskResponse
    {
        public int            AttackMmr            { get; private set; }
        public ProducerTypeId HighestLevelProducer { get; private set; }

        InternalPlayerGetBattleAttackParamsResponse() { }

        public InternalPlayerGetBattleAttackParamsResponse(int attackMmr, ProducerTypeId highestLevelProducer)
        {
            AttackMmr            = attackMmr;
            HighestLevelProducer = highestLevelProducer;
        }
    }
    
    [MetaMessage(MessageCodes.InternalWinIdlerPvPBattleMessage, MessageDirection.ServerInternal)]
    public class InternalWinIdlerPvPBattleMessage : MetaMessage
    {
        public static readonly InternalWinIdlerPvPBattleMessage Instance = new InternalWinIdlerPvPBattleMessage();
        InternalWinIdlerPvPBattleMessage() { }
    }

    /*
    /// <summary>
    /// An example message.
    /// </summary>
    [MetaMessage(MessageCodes.InternalExampleMessage, MessageDirection.ServerInternal)]
    public class ExampleInternalPlayerMessage : MetaMessage
    {
        public int ExampleValue { get; private set; }

        public ExampleInternalPlayerMessage() { }
        public ExampleInternalPlayerMessage(int exampleValue)
        {
            ExampleValue = exampleValue;
        }
    }
    */
}
