using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Server.Community;

/// <summary>All ranked participants, including offline players. Activity leases are deliberately not persisted.</summary>
[MetaSerializable]
[SupportedSchemaVersions(1, 1)]
public class CommunityState : ISchemaMigratable
{
    [MetaMember(1)] public MetaDictionary<EntityId, LeaderboardEntry> Players { get; set; }
        = new MetaDictionary<EntityId, LeaderboardEntry>();

    [MetaMember(2)] public bool SeededFromPlayers { get; set; }

    public void Update(LeaderboardEntry entry)
    {
        if (entry.Matches <= 0)
            return;
        if (Players.TryGetValue(entry.PlayerId, out LeaderboardEntry previous) && previous.Matches > entry.Matches)
            return;
        Players[entry.PlayerId] = entry;
    }

    public CommunitySnapshot Read(EntityId playerId)
    {
        List<LeaderboardEntry> ordered = Players.Values.OrderByDescending(player => player.Rating)
            .ThenByDescending(player => player.Wins).ThenBy(player => player.PlayerId.ToString(), StringComparer.Ordinal)
            .ToList();
        CommunitySnapshot snapshot = new CommunitySnapshot { RankedPlayers = ordered.Count };
        for (int index = 0; index < ordered.Count; index++)
        {
            LeaderboardRow row = new LeaderboardRow { Position = index + 1, Player = ordered[index] };
            if (index < 20)
                snapshot.Leaders.Add(row);
            if (row.Player.PlayerId == playerId)
                snapshot.OwnPosition = row;
        }
        return snapshot;
    }
}

/// <summary>Replacement samples avoid double counting; leases remove tables lost with a process or node.</summary>
public class MatchPopulation
{
    readonly Dictionary<EntityId, (int Players, DateTime Expires)> _matches = new();
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    public void Update(EntityId matchId, int players, DateTime now)
    {
        if (players <= 0)
            _matches.Remove(matchId);
        else
            _matches[matchId] = (Math.Min(2, players), now + LeaseDuration);
    }

    public int Count(DateTime now)
    {
        foreach (EntityId matchId in _matches.Where(entry => entry.Value.Expires <= now).Select(entry => entry.Key).ToArray())
            _matches.Remove(matchId);
        return _matches.Values.Sum(entry => entry.Players);
    }
}
