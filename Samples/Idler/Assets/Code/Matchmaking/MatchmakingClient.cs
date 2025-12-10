// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.TypeCodes;
using Game.Logic.Matchmaking;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Message;

namespace Game.Logic
{
    public class MatchmakingClient : IMetaplaySubClient
    {
        /// <inheritdoc />
        public ClientSlot ClientSlot => ClientSlotGame.Matchmaker;

        IMessageDispatcher _messageDispatcher;

        public IdleMatchingResponse LatestResponse { get; private set; }

        #if UNITY_EDITOR
        public static MatchmakingClient EditorHookCurrent;
        #endif

        public MatchmakingClient()
        {
            #if UNITY_EDITOR
            EditorHookCurrent = this;
            #endif
        }

        public void Initialize(IMetaplaySubClientServices clientServices)
        {
            _messageDispatcher = clientServices.MessageDispatcher;
            _messageDispatcher.AddListener<IdleMatchingResponse>(HandleMatchingResponse);
        }

        public void Dispose()
        {
            _messageDispatcher.RemoveListener<IdleMatchingResponse>(HandleMatchingResponse);
        }

        public void HandleMatchingResponse(IdleMatchingResponse response)
        {
            LatestResponse = response;
        }

        public void SendMatchmakingRequest()
        {
            _messageDispatcher.SendMessage(new IdleMatchingRequest());
        }

        public void OnSessionStart(SessionProtocol.SessionStartSuccess successMessage, ClientSessionStartResources sessionStartResources) { }
        public void OnSessionStop() { }
        public void OnDisconnected() { }
        public void EarlyUpdate() { }
        public void UpdateLogic(MetaTime time) { }
        public void FlushPendingMessages() { }
    }
}

