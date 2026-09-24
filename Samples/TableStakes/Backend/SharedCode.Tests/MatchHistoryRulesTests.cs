using Metaplay.Core;
using Metaplay.Core.Model;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="PlayerRecord"/>, the bounded match history, the rule that records each match once,
    /// and the player's public identity. The recording rules are pure functions over a list, so "recorded once
    /// per match however many times the result arrives" is tested without an actor, a message or a clock.
    /// </summary>
    [TestFixture]
    public class MatchHistoryRulesTests
    {
        static readonly MetaTime Now = MetaTime.FromDateTime(new System.DateTime(2026, 9, 1, 12, 0, 0, System.DateTimeKind.Utc));

        static readonly IReadOnlyList<EntityId> NoDeclines = new List<EntityId>();

        static MatchHistoryEntry Entry(int index, int rank = 1, int tricksWon = 1, int humanOpponents = 0, bool finishedByPlayer = true) =>
            new MatchHistoryEntry(MatchTestDeals.MatchId(index), Now, tricksWon, rank, rank == 0, humanOpponents, finishedByPlayer);

        #region The record

        [Test]
        public void TheCountersFoldOneGameAtATime()
        {
            PlayerRecord record = new PlayerRecord();

            record.Add(Entry(1, rank: 0, tricksWon: 3));
            record.Add(Entry(2, rank: 2, tricksWon: 1));
            record.Add(Entry(3, rank: 0, tricksWon: 2));

            Assert.That(record.GamesPlayed, Is.EqualTo(3));
            Assert.That(record.GamesWon, Is.EqualTo(2));
            Assert.That(record.TricksWon, Is.EqualTo(6));
        }

        [Test]
        public void AWinIsFirstPlaceAndNothingElse()
        {
            // The record counts a win from the table's rank, which already applies the tie-break, and does not
            // recompute it from trick counts (docs/game-rules.md, "End of the game and standings").
            PlayerRecord record = new PlayerRecord();

            record.Add(Entry(1, rank: 0, tricksWon: 1));
            record.Add(Entry(2, rank: 1, tricksWon: 4));

            Assert.That(record.GamesWon, Is.EqualTo(1), "a rank-1 seat with more tricks was counted as a win");
            Assert.That(record.TricksWon, Is.EqualTo(5), "tricks are counted whether the game was won or not");
        }

        [Test]
        public void EveryCompletedGameCounts()
        {
            // A game counts even when a bot finished it for the player. finishedByPlayer is recorded but does not
            // decide whether the game counts (docs/player.md, "Lifetime record and match history").
            PlayerRecord record = new PlayerRecord();

            record.Add(Entry(1, rank: 0, tricksWon: 3, finishedByPlayer: false));
            record.Add(Entry(2, rank: 3, tricksWon: 0, finishedByPlayer: false));

            Assert.That(record.GamesPlayed, Is.EqualTo(2));
            Assert.That(record.GamesWon, Is.EqualTo(1), "a win a bot played out was not credited");
        }

        #endregion

        #region The history's bound

        [Test]
        public void TheHistoryIsBoundedAndTheOldestFallsOff()
        {
            List<MatchHistoryEntry> history = new List<MatchHistoryEntry>();

            for (int index = 0; index < MatchHistoryRules.HistoryCapacity + 5; index++)
                MatchHistoryRules.Append(history, Entry(index));

            Assert.That(history, Has.Count.EqualTo(MatchHistoryRules.HistoryCapacity));
            Assert.That(history[0].MatchId, Is.EqualTo(MatchTestDeals.MatchId(5)), "the oldest entries did not fall off the front");
            Assert.That(history[^1].MatchId, Is.EqualTo(MatchTestDeals.MatchId(MatchHistoryRules.HistoryCapacity + 4)), "the newest entry is not at the end");
        }

        [Test]
        public void AnOverlongHistoryIsTrimmedAllTheWay()
        {
            // Lowering HistoryCapacity in a new build leaves longer lists in saved accounts. Trimming one entry
            // per append would keep such an account over the capacity forever.
            List<MatchHistoryEntry> history = new List<MatchHistoryEntry>();
            for (int index = 0; index < MatchHistoryRules.HistoryCapacity + 10; index++)
                history.Add(Entry(index));

            MatchHistoryRules.Append(history, Entry(999));

            Assert.That(history, Has.Count.EqualTo(MatchHistoryRules.HistoryCapacity));
            Assert.That(history[^1].MatchId, Is.EqualTo(MatchTestDeals.MatchId(999)));
        }

        #endregion

        #region Idempotency

        [Test]
        public void AMatchAlreadyOnTheHistoryIsRecognized()
        {
            List<MatchHistoryEntry> history = new List<MatchHistoryEntry> { Entry(1), Entry(2) };

            Assert.That(MatchHistoryRules.HasRecorded(history, MatchTestDeals.MatchId(1)), Is.True);
            Assert.That(MatchHistoryRules.HasRecorded(history, MatchTestDeals.MatchId(2)), Is.True);
            Assert.That(MatchHistoryRules.HasRecorded(history, MatchTestDeals.MatchId(3)), Is.False);
        }

        [Test]
        public void AnEmptyOrInvalidLookupIsNotAMatch()
        {
            Assert.That(MatchHistoryRules.HasRecorded(null, MatchTestDeals.MatchId(1)), Is.False);
            Assert.That(MatchHistoryRules.HasRecorded(new List<MatchHistoryEntry>(), MatchTestDeals.MatchId(1)), Is.False);
            Assert.That(MatchHistoryRules.HasRecorded(new List<MatchHistoryEntry> { Entry(1) }, EntityId.None), Is.False,
                "an invalid id matched an entry, so a malformed delivery would look already-recorded");
        }

        [Test]
        public void RedeliveringTheSameResultMovesNothing()
        {
            // The match resends a result on every wake until the player acknowledges it, so every repeat
            // delivery must change nothing (docs/player.md, "Idempotence and its limit").
            List<MatchHistoryEntry> history = new List<MatchHistoryEntry>();
            PlayerRecord            record  = new PlayerRecord();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                MatchHistoryEntry entry = Entry(1, rank: 0, tricksWon: 3);
                if (MatchHistoryRules.HasRecorded(history, entry.MatchId))
                    continue;

                MatchHistoryRules.Append(history, entry);
                record.Add(entry);
            }

            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(record.GamesPlayed, Is.EqualTo(1));
            Assert.That(record.GamesWon, Is.EqualTo(1));
            Assert.That(record.TricksWon, Is.EqualTo(3));
        }

        [Test]
        public void NegativeControl_RecordingWithoutTheCheckDoubleCounts()
        {
            // Negative control for RedeliveringTheSameResultMovesNothing: without the HasRecorded check,
            // repeated deliveries count the same game several times.
            List<MatchHistoryEntry> history = new List<MatchHistoryEntry>();
            PlayerRecord            record  = new PlayerRecord();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                MatchHistoryEntry entry = Entry(1, rank: 0, tricksWon: 3);
                MatchHistoryRules.Append(history, entry);
                record.Add(entry);
            }

            Assert.That(record.GamesPlayed, Is.EqualTo(5), "the unchecked path did not double-count, so the check above proves nothing");
        }

        #endregion

        #region What a delivery is allowed to move

        static MatchSeatResult Result(int rank = 0, int tricksWon = 3) =>
            new MatchSeatResult(seat: 0, EntityId.Create(EntityKindCore.Player, 1UL), rank, tricksWon,
                humanOpponents: 0, finishedByPlayer: true, MatchSeatLossReason.None);

        [Test]
        public void AnAbandonedTableNeverMovesTheRecord()
        {
            // A delivery carries the terminal phase, so the same message both clears the player's match pointer
            // and decides whether the game is counted. An abandoned table never started and has no standings, so
            // it clears the pointer and counts nothing (docs/match.md, "Phases").
            List<MatchHistoryEntry> history = new List<MatchHistoryEntry>();

            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Abandoned, result: null, history, NoDeclines, MatchTestDeals.MatchId(1)), Is.False);

            // The phase decides, so an Abandoned delivery counts nothing even when it carries a result.
            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Abandoned, Result(), history, NoDeclines, MatchTestDeals.MatchId(1)), Is.False);
        }

        [Test]
        public void ATableThatReachedAResultMovesTheRecordExactlyOnce()
        {
            List<MatchHistoryEntry> history = new List<MatchHistoryEntry>();

            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Ended, Result(), history, NoDeclines, MatchTestDeals.MatchId(1)), Is.True);

            // After the entry is appended, every later delivery of the same result finds it and is not recorded.
            MatchHistoryRules.Append(history, Entry(1, rank: 0, tricksWon: 3));

            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Ended, Result(), history, NoDeclines, MatchTestDeals.MatchId(1)), Is.False);
            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Ended, Result(), history, NoDeclines, MatchTestDeals.MatchId(2)), Is.True,
                "a different table was refused because another one was already recorded");
        }

        /// <summary>
        /// When a player declines a late seat assignment, the seat at that table stays assigned to them, a bot
        /// plays it, and the table delivers a result. The player never saw the game, so the result must not use
        /// a tournament attempt or advance missions.
        /// </summary>
        [Test]
        public void ADeclinedTableIsNeverRecorded()
        {
            List<MatchHistoryEntry> history  = new List<MatchHistoryEntry>();
            List<EntityId>          declined = new List<EntityId>();

            MatchHistoryRules.NoteDeclined(declined, MatchTestDeals.MatchId(2));

            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Ended, Result(), history, declined, MatchTestDeals.MatchId(2)), Is.False);
            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Ended, Result(), history, declined, MatchTestDeals.MatchId(1)), Is.True,
                "the table the player actually sat at was refused");
        }

        [Test]
        public void TheDeclinedListIsBoundedAndHoldsEachTableOnce()
        {
            List<EntityId> declined = new List<EntityId>();

            MatchHistoryRules.NoteDeclined(declined, MatchTestDeals.MatchId(1));
            MatchHistoryRules.NoteDeclined(declined, MatchTestDeals.MatchId(1));
            Assert.That(declined, Is.EqualTo(new[] { MatchTestDeals.MatchId(1) }));

            for (int index = 2; index <= MatchHistoryRules.DeclinedCapacity + 1; index++)
                MatchHistoryRules.NoteDeclined(declined, MatchTestDeals.MatchId(index));

            Assert.That(declined, Has.Count.EqualTo(MatchHistoryRules.DeclinedCapacity));
            Assert.That(declined, Does.Not.Contain(MatchTestDeals.MatchId(1)), "the oldest table is the one dropped");
        }

        [Test]
        public void ANonTerminalOrEmptyDeliveryRecordsNothing()
        {
            // A table only delivers a result from a terminal phase, and an Ended delivery always carries a result,
            // so neither case can happen. This pins what ShouldRecord does with such a malformed message.
            List<MatchHistoryEntry> history = new List<MatchHistoryEntry>();

            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Playing, Result(), history, NoDeclines, MatchTestDeals.MatchId(1)), Is.False);
            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Ended, result: null, history, NoDeclines, MatchTestDeals.MatchId(1)), Is.False);
        }

        #endregion

        #region The model's own rule

        [Test]
        public void TheModelRefusesASecondRecordingOfOneMatch()
        {
            // PlayerModel.TryRecordMatch enforces once per match itself, so a caller that skips the
            // HasRecorded check still cannot record a match twice.
            PlayerModel player = new PlayerModel();

            Assert.That(player.TryRecordMatch(Entry(1, rank: 0, tricksWon: 3)), Is.True);
            Assert.That(player.TryRecordMatch(Entry(1, rank: 0, tricksWon: 3)), Is.False);
            Assert.That(player.TryRecordMatch(Entry(2, rank: 1, tricksWon: 1)), Is.True);

            Assert.That(player.Record.GamesPlayed, Is.EqualTo(2));
            Assert.That(player.Record.GamesWon, Is.EqualTo(1));
            Assert.That(player.Record.TricksWon, Is.EqualTo(4));
            Assert.That(player.MatchHistory, Has.Count.EqualTo(2));
        }

        [Test]
        public void ARenameStampsWhenItHappened()
        {
            // LastRenamedAt is the only state the rename rate limit reads. A rename that did not set it would let
            // a player rename without limit.
            PlayerModel player = new PlayerModel();
            Assert.That(player.LastRenamedAt, Is.EqualTo(MetaTime.Epoch));

            player.ApplyRename("New Name", Now);

            Assert.That(player.PlayerName, Is.EqualTo("New Name"));
            Assert.That(player.LastRenamedAt, Is.EqualTo(Now));
            Assert.That(DisplayNamePolicy.ValidateRename("Another Name", player.PlayerName, player.LastRenamedAt, Now, TestBotConfig.Roster), Is.EqualTo(DisplayNameRefusal.TooSoon));
        }

        [Test]
        public void AnAcceptedRenameIsFourWritesInOnePlace()
        {
            // ApplyRename updates the count and the flag together with the name, so a refused or replayed request,
            // which never reaches ApplyRename, never counts.
            PlayerModel player = new PlayerModel();

            Assert.That(player.HasCustomizedName, Is.False);
            Assert.That(player.NameChangeCount, Is.Zero);

            player.ApplyRename("First Pick", Now);

            Assert.That(player.PlayerName, Is.EqualTo("First Pick"));
            Assert.That(player.HasCustomizedName, Is.True);
            Assert.That(player.NameChangeCount, Is.EqualTo(1));

            player.ApplyRename("Second Pick", Now + MetaDuration.FromMinutes(1));

            Assert.That(player.NameChangeCount, Is.EqualTo(2));
        }

        [Test]
        public void ARenameActionWritesNothingOnItsDryRun()
        {
            // The SDK executes a model action twice, first with commit false and then with commit true. An action
            // that wrote on the dry run would increase the count twice for one rename.
            PlayerModel      player = new PlayerModel();
            PlayerRenamed    action = new PlayerRenamed("Fresh Name", Now);

            Assert.That(action.Execute(player, commit: false), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.PlayerName, Is.Null, "the dry run wrote the name");
            Assert.That(player.NameChangeCount, Is.Zero, "the dry run moved the count");
            Assert.That(player.HasCustomizedName, Is.False);
            Assert.That(player.LastRenamedAt, Is.EqualTo(MetaTime.Epoch));

            Assert.That(action.Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.PlayerName, Is.EqualTo("Fresh Name"));
            Assert.That(player.NameChangeCount, Is.EqualTo(1));
        }

        #endregion

        #region The public identity

        [Test]
        public void ThePublicIdentityIsTheNameAndNothingPrivate()
        {
            // The public identity is what other players see (docs/player.md, "Public identity"). It must carry the
            // display name and must not carry anything the profile screen is not allowed to show.
            PlayerModel player = new PlayerModel();
            player.PlayerId = EntityId.Create(EntityKindCore.Player, 4242);
            player.ApplyRename("Velvet Ace", Now);

            PlayerPublicIdentity identity = player.BuildPublicIdentity();

            Assert.That(identity.DisplayName, Is.EqualTo("Velvet Ace"));
            Assert.That(identity.PlayerId, Is.EqualTo(player.PlayerId));

            // A player with no avatar equipped has a null AvatarId, and the client draws its default avatar.
            Assert.That(identity.AvatarId, Is.Null);
        }

        [Test]
        public void ARenameTellsTheHoldersOfASnapshot()
        {
            // BuildPublicIdentity builds a new identity on every call, so any holder of an older copy must be told
            // about a rename. Without OnPublicIdentityChanged, the tournament standings would show a renamed
            // player's old name for the rest of the season (docs/player.md, "Public identity").
            RecordingServerListener listener = new RecordingServerListener();
            PlayerModel             player   = new PlayerModel { ServerListener = listener };

            player.ApplyRename("Velvet Ace", Now);

            Assert.That(listener.IdentityChanges, Is.EqualTo(1));

            player.ApplyRename("Golden Fox", Now + MetaDuration.FromMinutes(1));

            Assert.That(listener.IdentityChanges, Is.EqualTo(2));
        }

        [Test]
        public void ARenameActionTellsNobodyOnItsDryRun()
        {
            // The dry run must not call the listener. A listener called before the commit would read the old name,
            // and one called twice would refresh for nothing.
            RecordingServerListener listener = new RecordingServerListener();
            PlayerModel             player   = new PlayerModel { ServerListener = listener };

            new PlayerRenamed("Fresh Name", Now).Execute(player, commit: false);

            Assert.That(listener.IdentityChanges, Is.Zero);

            new PlayerRenamed("Fresh Name", Now).Execute(player, commit: true);

            Assert.That(listener.IdentityChanges, Is.EqualTo(1));
        }

        /// <summary>Counts the calls to <see cref="IPlayerModelServerListener.OnPublicIdentityChanged"/>.</summary>
        sealed class RecordingServerListener : IPlayerModelServerListener
        {
            public int IdentityChanges { get; private set; }

            public void OnPublicIdentityChanged() => IdentityChanges++;
        }

        [Test]
        public void TwoIdentitiesAreEqualWhenTheyDrawTheSame()
        {
            // Value equality lets a holder that was told to refresh check whether anything it shows changed.
            // BuildPublicIdentity returns a new object per call, so reference equality would always report a change.
            PlayerModel player = new PlayerModel();
            player.PlayerId = EntityId.Create(EntityKindCore.Player, 99);
            player.ApplyRename("Same Name", Now);

            PlayerPublicIdentity first  = player.BuildPublicIdentity();
            PlayerPublicIdentity second = player.BuildPublicIdentity();
            Assert.That(second, Is.EqualTo(first));
            Assert.That(second.GetHashCode(), Is.EqualTo(first.GetHashCode()));

            PlayerPublicIdentity before = player.BuildPublicIdentity();
            player.ApplyRename("Other Name", Now + MetaDuration.FromMinutes(1));

            Assert.That(player.BuildPublicIdentity(), Is.Not.EqualTo(before));
        }

        [Test]
        public void ThePublicIdentityFollowsTheNameRatherThanBeingStored()
        {
            // The identity is built from the model on each call, not stored. An identity built earlier keeps its
            // values, and the model never hands out a stale identity.
            PlayerModel player = new PlayerModel();
            player.ApplyRename("Before", Now);
            PlayerPublicIdentity before = player.BuildPublicIdentity();

            player.ApplyRename("After", Now + MetaDuration.FromMinutes(1));

            Assert.That(before.DisplayName, Is.EqualTo("Before"), "the earlier snapshot was mutated behind its holder");
            Assert.That(player.BuildPublicIdentity().DisplayName, Is.EqualTo("After"));
        }

        #endregion
    }
}
