using Metaplay.Core.Client;
using Metaplay.Core.League;
using Metaplay.Core.Message;
using Metaplay.Core.Tasks;
using System;
using System.Threading;

namespace Game.Logic.League
{
    public class IdlerLeagueClient : LeagueClient<IdlerPlayerDivisionModel>
    {
        IMessageDispatcher _messageDispatcher;

        public bool   LeagueJoinRequestInProgress { get; private set; }
        public string LeagueJoinRequestStatus     { get; private set; }

        CancellationTokenSource _joinRequestTimeoutCts;

        public override void Initialize(IMetaplaySubClientServices clientServices)
        {
            base.Initialize(clientServices);

            _messageDispatcher = clientServices.MessageDispatcher;
            _messageDispatcher.AddListener<PlayerJoinIdleLeagueResponse>(HandleLeagueJoinResponse);
        }

        public override void Dispose()
        {
            base.Dispose();
            _messageDispatcher.RemoveListener<PlayerJoinIdleLeagueResponse>(HandleLeagueJoinResponse);
        }

        public void TryJoinLeagues()
        {
            if (LeagueJoinRequestInProgress)
                return;

            LeagueJoinRequestInProgress = true;
            LeagueJoinRequestStatus     = "Trying to join...";
            _messageDispatcher.SendMessage(PlayerJoinIdleLeagueRequest.Instance);

            _joinRequestTimeoutCts = new CancellationTokenSource();

            MetaTask.Delay(TimeSpan.FromSeconds(5), _joinRequestTimeoutCts.Token).ContinueWithCtx(t =>
            {
                LeagueJoinRequestInProgress = false;
                LeagueJoinRequestStatus     = "Timeout";
            }, _joinRequestTimeoutCts.Token);
        }

        void HandleLeagueJoinResponse(PlayerJoinIdleLeagueResponse response)
        {
            LeagueJoinRequestInProgress = false;
            _joinRequestTimeoutCts.Cancel();

            if (!response.Success)
            {
                LeagueJoinRequestStatus = "Failed: " + response.FailureReason;
            }
            else
            {
                LeagueJoinRequestStatus = null;
            }
        }

        public IdlerLeagueClient(ClientSlot clientSlot) : base(clientSlot) { }
    }
}
