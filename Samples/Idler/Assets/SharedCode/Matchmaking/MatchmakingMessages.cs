// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.TypeCodes;
using Metaplay.Core.Message;
using Metaplay.Core.Model;

namespace Game.Logic.Matchmaking
{
    [MetaSerializableDerived(MessageCodes.IdleMatchingRequest)]
    public class IdleMatchingRequest : MetaRequest
    {
        public IdleMatchingRequest() { }
    }

    [MetaSerializableDerived(MessageCodes.IdleMatchingResponse)]
    public class IdleMatchingResponse : MetaResponse
    {
        public bool IsSuccess { get; set; }
        public bool DidWinBattle { get; set; }

        IdleMatchingResponse() { }

        public IdleMatchingResponse(bool isSuccess, bool didWinBattle)
        {
            IsSuccess = isSuccess;
            DidWinBattle = didWinBattle;
        }
    }
}

