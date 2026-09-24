using Game.Logic;
using Game.Server.Community;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Server.Match;

public sealed partial class MatchActor
{
    int _lastPopulation = -1;

    void ReportPopulation(bool heartbeat = false)
    {
        int players = Model.IsTerminal ? 0 : Model.Seats.Count(seat => seat.IsConnectedHuman);
        if (heartbeat || players != _lastPopulation)
        {
            CastMessage(CommunityActor.EntityId, new InternalMatchPopulation(players));
            _lastPopulation = players;
        }
    }

    void ArmPopulationHeartbeat()
    {
        ReportPopulation(heartbeat: true);
        if (Model.IsTerminal)
            return;
        ScheduleExecuteOnActorContext(DateTime.UtcNow + TimeSpan.FromSeconds(10), () =>
        {
            ArmPopulationHeartbeat();
            return Task.CompletedTask;
        });
    }
}
