using Game.Logic;
using Game.Server.Community;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Linq;

namespace Game.Server.Tests;

public class CommunityTests
{
    static EntityId Player(int id) => EntityId.Create(EntityKindCore.Player, (ulong)id);
    static LeaderboardEntry Entry(int id, int rating, int wins = 1, int matches = 1)
        => new LeaderboardEntry { PlayerId = Player(id), DisplayName = $"Player {id}", Rating = rating, Wins = wins, Matches = matches };

    [Test]
    public void LadderIncludesOfflinePlayersAndOwnPositionOutsideTopTwenty()
    {
        CommunityState state = new CommunityState();
        for (int id = 1; id <= 25; id++) state.Update(Entry(id, 1000 - id));
        CommunitySnapshot snapshot = state.Read(Player(25));
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.RankedPlayers, Is.EqualTo(25));
            Assert.That(snapshot.Leaders.Count, Is.EqualTo(20));
            Assert.That(snapshot.OwnPosition.Position, Is.EqualTo(25));
            Assert.That(snapshot.Leaders.First().Player.PlayerId, Is.EqualTo(Player(1)));
        });
    }

    [Test]
    public void RatingCanFallAndDuplicateOrOlderReportsDoNotCorruptTheLadder()
    {
        CommunityState state = new CommunityState();
        state.Update(Entry(1, 1000));
        state.Update(Entry(1, 950, matches: 2));
        state.Update(Entry(1, 1000));
        state.Update(Entry(2, 900, matches: 0));
        Assert.That(state.Read(Player(1)).OwnPosition.Player.Rating, Is.EqualTo(950));
        Assert.That(state.Players.Count, Is.EqualTo(1));
    }

    [Test]
    public void TiesAreStableAndRenamesUpdateWithoutDuplicatingPlayers()
    {
        CommunityState state = new CommunityState();
        state.Update(Entry(3, 1000));
        state.Update(Entry(2, 1000, wins: 2, matches: 2));
        state.Update(Entry(1, 1000));
        LeaderboardEntry renamed = Entry(1, 1000);
        renamed.DisplayName = "New name";
        state.Update(renamed);
        Assert.That(state.Read(Player(1)).Leaders.Select(row => row.Player.PlayerId), Is.EqualTo(new[] { Player(2), Player(1), Player(3) }));
        Assert.That(state.Read(Player(1)).OwnPosition.Player.DisplayName, Is.EqualTo("New name"));
    }

    [Test]
    public void PopulationSamplesReplaceExpireAndClear()
    {
        MatchPopulation population = new MatchPopulation();
        EntityId first = EntityId.Create(EntityKindGame.Match, 1);
        EntityId second = EntityId.Create(EntityKindGame.Match, 2);
        DateTime now = DateTime.UtcNow;
        population.Update(first, 2, now);
        population.Update(first, 1, now);
        population.Update(second, 2, now.AddSeconds(10));
        Assert.That(population.Count(now), Is.EqualTo(3));
        Assert.That(population.Count(now.AddSeconds(30)), Is.EqualTo(2));
        population.Update(second, 0, now.AddSeconds(31));
        Assert.That(population.Count(now.AddSeconds(31)), Is.Zero);
    }
}
