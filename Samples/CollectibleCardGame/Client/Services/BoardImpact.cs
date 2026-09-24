using Game.Logic;
using System.Collections.Generic;

namespace Game.Client.Services;

/// <summary> Confirmed health feedback, never a prediction or a second application of a Den hit. </summary>
public sealed record BoardImpact(string Kind, string TargetKind, long Target, int Amount)
{
    public static List<BoardImpact> FromEvents(IReadOnlyList<MatchEvent> events)
    {
        List<BoardImpact> impacts = new List<BoardImpact>();
        foreach (MatchEvent ev in events)
        {
            switch (ev)
            {
                case DamageDealtEvent damage when damage.Target.IsCritter && damage.Amount > 0:
                    impacts.Add(new BoardImpact(damage.AbsorbedByBubble ? "blocked" : "damage", "critter",
                        damage.Target.Critter.Value, damage.AbsorbedByBubble ? 0 : damage.Amount));
                    break;
                case DenDamagedEvent den when den.Amount > 0:
                    impacts.Add(new BoardImpact("damage", "den", den.Seat, den.Amount));
                    break;
                case HealedEvent heal when heal.Amount > 0:
                    impacts.Add(new BoardImpact("heal", heal.Target.IsDen ? "den" : "critter",
                        heal.Target.IsDen ? heal.Target.DenSeat : heal.Target.Critter.Value, heal.Amount));
                    break;
            }
        }
        return impacts;
    }
}
