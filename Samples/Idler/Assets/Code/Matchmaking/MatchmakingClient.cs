// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.Matchmaking;
using Game.Logic.TypeCodes;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Message;
using System.Threading.Tasks;

namespace Game.Logic
{
    public class MatchmakingClient : IMetaplaySubClient
    {
        /// <inheritdoc />
        public ClientSlot ClientSlot => ClientSlotGame.Matchmaker;

        IMessageDispatcher _messageDispatcher;

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
        }

        public void Dispose() { }

        public async Task<IdleMatchingResponse> RequestMatchmakingAsync()
        {
            return await _messageDispatcher.SendRequestAsync<IdleMatchingResponse>(
                new IdleMatchingRequest());
        }

        public void OnSessionStart(SessionProtocol.SessionStartSuccess successMessage, ClientSessionStartResources sessionStartResources) { }
        public void OnSessionStop() { }
        public void OnDisconnected() { }
        public void EarlyUpdate() { }
        public void UpdateLogic(MetaTime time) { }
        public void FlushPendingMessages() { }
    }
}

