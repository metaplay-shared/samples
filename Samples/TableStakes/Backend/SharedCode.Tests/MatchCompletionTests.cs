using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests the match-completion seam: the <see cref="MatchCompletion"/> fact that feature states consume
    /// through <see cref="IMatchCompletionObserver"/>.
    /// <para>
    /// Most tests use <see cref="RecordingObserver"/>, a stand-in consumer that implements
    /// <see cref="IMatchCompletionObserver"/>, is reached through
    /// <see cref="PlayerModel.CollectMatchCompletionObservers"/>, and records what it receives. The tests
    /// therefore check the seam itself rather than one feature's use of it.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchCompletionTests
    {
        static readonly MetaTime FinishedAt = MetaTime.FromDateTime(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

        #region The stand-in consumer

        /// <summary>A stand-in consumer that records every fact and context value it receives.</summary>
        internal sealed class RecordingObserver : IMatchCompletionObserver
        {
            public List<MatchCompletion> ReceivedCompletions { get; } = new List<MatchCompletion>();

            /// <summary>The context values from the last delivery, kept so a test can assert on them.</summary>
            public SharedGameConfig       ConfigSeen      { get; private set; }
            public EntityId               PlayerSeen      { get; private set; }
            public AnalyticsCorrelationId CorrelationSeen { get; private set; }
            public PlayerLocalTime        CompletedAtLocalSeen       { get; private set; }

            /// <summary>When true, <see cref="OnMatchCompleted"/> throws, as a feature with missing config would.</summary>
            public bool Throws { get; set; }

            /// <summary>An event that <see cref="OnMatchCompleted"/> emits to the player's log, when set.</summary>
            public PlayerEventBase EmitOnCompletion { get; set; }

            public void OnMatchCompleted(in MatchCompletionContext context)
            {
                if (Throws)
                    throw new InvalidOperationException("the feature's configuration is missing");

                ReceivedCompletions.Add(context.Completion);
                ConfigSeen           = context.GameConfig;
                PlayerSeen           = context.PlayerId;
                CorrelationSeen      = context.CorrelationIdFor("test_feature");
                CompletedAtLocalSeen = context.CompletedAtLocal;

                if (EmitOnCompletion != null)
                    context.Emit(EmitOnCompletion);
            }
        }

        /// <summary>
        /// Creates a player model with <paramref name="observerCount"/> stand-in consumers attached through the
        /// internal <c>_testMatchCompletionObservers</c> list (<c>SharedCode/AssemblyInfo.cs</c> says why a
        /// subclass cannot be used). Recording and dispatch then run the production code.
        /// </summary>
        static PlayerModel NewPlayer(List<PlayerEventBase> captured = null, int observerCount = 1)
        {
            PlayerModel player = TestPlayers.New(MetaTime.Epoch, captured: captured);

            player._testMatchCompletionObservers = new List<IMatchCompletionObserver>();
            for (int index = 0; index < observerCount; index++)
                player._testMatchCompletionObservers.Add(new RecordingObserver());

            return player;
        }

        /// <summary>The stand-in consumer at <paramref name="index"/> on <paramref name="player"/>.</summary>
        static RecordingObserver Observer(PlayerModel player, int index = 0) =>
            (RecordingObserver)player._testMatchCompletionObservers[index];

        /// <summary>
        /// Delivers one table result through <see cref="TestPlayers.DryRunThenCommit"/>. Both passes run so that a
        /// test catches a dry-run pass that changes state.
        /// </summary>
        static MetaActionResult Deliver(PlayerModel player, int matchIndex, int position = 0, int tricksWon = 3, MetaTime? at = null) =>
            TestPlayers.DryRunThenCommit(player, TestPlayers.MatchResult(matchIndex, at ?? FinishedAt, position, tricksWon));

        #endregion

        #region The fact itself

        [Test]
        public void AFinishedGameReachesTheConsumerWithWhatItNeeds()
        {
            PlayerModel       player   = NewPlayer();
            RecordingObserver observer = Observer(player);

            Deliver(player, matchIndex: 1, position: 0, tricksWon: 3);

            Assert.That(observer.ReceivedCompletions, Has.Count.EqualTo(1));

            MatchCompletion fact = observer.ReceivedCompletions[0];
            Assert.That(fact.MatchId, Is.EqualTo(MatchTestDeals.MatchId(1)));
            Assert.That(fact.CompletedAt, Is.EqualTo(FinishedAt), "the day a goal falls in is decided from this stamp");
            Assert.That(fact.Position, Is.EqualTo(0));
            Assert.That(fact.IsWin, Is.True);
            Assert.That(fact.TricksWon, Is.EqualTo(3));
        }

        [Test]
        public void ALossIsACompletedGameThatIsNotAWin()
        {
            // Consumers read both objective kinds from this fact: a loss advances MatchesCompleted and not
            // MatchesWon.
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1, position: 2, tricksWon: 1);

            MatchCompletion fact = Observer(player).ReceivedCompletions.Single();
            Assert.That(fact.IsWin, Is.False);
            Assert.That(fact.Position, Is.EqualTo(2));
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(1), "a loss is still a completed game");
        }

        [Test]
        public void EveryGameIsOneFactInTheOrderTheyWerePlayed()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1);
            Deliver(player, matchIndex: 2);
            Deliver(player, matchIndex: 3);

            Assert.That(Observer(player).ReceivedCompletions.Select(fact => fact.MatchId), Is.EqualTo(new[] { MatchTestDeals.MatchId(1), MatchTestDeals.MatchId(2), MatchTestDeals.MatchId(3) }));
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(3));
        }

        [Test]
        public void TheFactAgreesWithTheEntryOnTheRecord()
        {
            // The fact is built from the history entry that was just appended, not from the action's payload,
            // so the two must match.
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 4, position: 1, tricksWon: 2);

            MatchCompletion   fact  = Observer(player).ReceivedCompletions.Single();
            MatchHistoryEntry entry = player.MatchHistory.Single();

            Assert.That(fact.MatchId, Is.EqualTo(entry.MatchId));
            Assert.That(fact.CompletedAt, Is.EqualTo(entry.EndedAt));
            Assert.That(fact.Position, Is.EqualTo(entry.Position));
            Assert.That(fact.TricksWon, Is.EqualTo(entry.TricksWon));
            Assert.That(fact.IsWin, Is.EqualTo(entry.IsWin));
        }

        #endregion

        #region Exactly once

        [Test]
        public void ARedeliveredResultIsSeenOnce()
        {
            // The table re-offers a result until the player acknowledges it, so a retry after a lost answer or
            // a server restart reaches a player who already recorded it. Each retry executes the same action
            // again.
            PlayerModel       player   = NewPlayer();
            RecordingObserver observer = Observer(player);

            Assert.That(Deliver(player, matchIndex: 1), Is.EqualTo(MetaActionResult.Success));
            Assert.That(Deliver(player, matchIndex: 1), Is.EqualTo(ActionResults.MatchAlreadyRecorded));
            Assert.That(Deliver(player, matchIndex: 1), Is.EqualTo(ActionResults.MatchAlreadyRecorded));

            Assert.That(observer.ReceivedCompletions, Has.Count.EqualTo(1), "the consumer double-counted a re-delivered result");
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(1));
            Assert.That(player.MatchHistory, Has.Count.EqualTo(1));
        }

        [Test]
        public void ATableThatComesBackFromTheDatabaseIsSeenOnce()
        {
            // Runs the delivery as the player actor does: MatchHistoryRules.ShouldRecord first, then the action. A
            // table restored from the database re-offers the result to every seat that has not acknowledged it.
            PlayerModel       player   = NewPlayer();
            RecordingObserver observer = Observer(player);
            MatchSeatResult   result   = new MatchSeatResult(
                seat: 0, EntityId.Create(EntityKindCore.Player, 7), position: 0, tricksWon: 3,
                humanOpponents: 3, finishedByPlayer: true, MatchSeatLossReason.None);

            for (int delivery = 0; delivery < 4; delivery++)
            {
                if (MatchHistoryRules.ShouldRecord(MatchPhase.Ended, result, player.MatchHistory, player.DeclinedMatches, MatchTestDeals.MatchId(1)))
                    Deliver(player, matchIndex: 1);
            }

            Assert.That(observer.ReceivedCompletions, Has.Count.EqualTo(1));
        }

        [Test]
        public void AMatchAgedOffTheHistoryIsSeenASecondTime()
        {
            // The known gap in exactly-once delivery. Recording is deduplicated against the bounded match
            // history, so a result re-offered after the player completed HistoryCapacity further games is
            // recorded again and reaches the consumer again. MatchHistoryRules.HasRecorded describes the only paths
            // that reach this state, because a player with a match pointer set cannot enter matchmaking.
            //
            // The consumers accept the same limit as the lifetime record: a mission that over-counts by one is
            // capped by its own target. This test fails if the width of the gap changes.
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1);
            for (int index = 0; index < MatchHistoryRules.HistoryCapacity; index++)
                Deliver(player, matchIndex: 100 + index);

            Assert.That(player.HasRecordedMatch(MatchTestDeals.MatchId(1)), Is.False, "the entry has to age off for this to be the case under test");

            Deliver(player, matchIndex: 1);

            List<MatchCompletion> repeats = Observer(player).ReceivedCompletions.Where(fact => fact.MatchId == MatchTestDeals.MatchId(1)).ToList();
            Assert.That(repeats, Has.Count.EqualTo(2), "the disclosed window changed width");

            // A consumer can detect the repeat only by the match id. The record counted the game again too, so
            // a key derived from a counter would differ between the two deliveries.
            Assert.That(repeats[0].MatchId, Is.EqualTo(repeats[1].MatchId));
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(MatchHistoryRules.HistoryCapacity + 2));
        }

        [Test]
        public void TheDryRunPassTellsNobody()
        {
            // The SDK executes a model action twice and the passes must checksum identically, so the dry-run
            // pass must write nothing, emit nothing and consume no randomness. A consumer called on the dry-run
            // pass would count every game twice.
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Observer(player).EmitOnCompletion = new PlayerEventMatchSeatLost(MatchTestDeals.MatchId(1), MatchSeatLossReason.DeliberateLeave);

            PlayerRecordMatchResult action = TestPlayers.MatchResult(1, FinishedAt);

            Assert.That(action.Execute(player, commit: false), Is.EqualTo(MetaActionResult.Success));

            Assert.That(Observer(player).ReceivedCompletions, Is.Empty, "the dry-run pass handed the fact to a consumer");
            Assert.That(captured, Is.Empty, "the dry-run pass put an event on the player's log");
            Assert.That(player.MatchHistory, Is.Empty);
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(0));

            // The commit pass delivers exactly one fact. The real consumers also write to the log, so the
            // stand-in's row is selected by type.
            Assert.That(action.Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(Observer(player).ReceivedCompletions, Has.Count.EqualTo(1));
            Assert.That(captured.Count(payload => payload is PlayerEventMatchSeatLost), Is.EqualTo(1));
        }

        [Test]
        public void ATableWithNoResultIsNotACompletedGame()
        {
            // An abandoned table never started and has no standings. It is still delivered so that the player's
            // match pointer is cleared, but it must reach no consumer.
            PlayerModel player = NewPlayer();

            Assert.That(MatchHistoryRules.ShouldRecord(MatchPhase.Abandoned, result: null, player.MatchHistory, player.DeclinedMatches, MatchTestDeals.MatchId(1)), Is.False);
            Assert.That(Observer(player).ReceivedCompletions, Is.Empty);

            // An action with no match id is refused and reaches no consumer.
            PlayerRecordMatchResult action = new PlayerRecordMatchResult(
                EntityId.None, FinishedAt, position: 0, tricksWon: 3, humanOpponents: 3, finishedByPlayer: true);

            Assert.That(action.Execute(player, commit: true), Is.EqualTo(ActionResults.NoMatchId));
            Assert.That(Observer(player).ReceivedCompletions, Is.Empty);
        }

        #endregion

        #region A broken consumer cannot break the game

        [Test]
        public void OneBrokenConsumerDoesNotStarveTheOthers()
        {
            PlayerModel player = NewPlayer(observerCount: 3);
            Observer(player, 1).Throws = true;

            Deliver(player, matchIndex: 1);

            Assert.That(Observer(player).ReceivedCompletions, Has.Count.EqualTo(1));
            Assert.That(Observer(player, 1).ReceivedCompletions, Is.Empty);
            Assert.That(Observer(player, 2).ReceivedCompletions, Has.Count.EqualTo(1), "a consumer registered after the broken one was skipped");
        }

        [Test]
        public void AConsumerThatThrowsStillLeavesTheGameOnTheRecordAndCountsTheNextGame()
        {
            // A failure in an optional meta feature must not invalidate a committed match. The consumers run
            // inside the request that acknowledges the table's result, so an escaping exception would make the
            // table re-offer the result forever and leave the player's match pointer set.
            PlayerModel player = NewPlayer();
            Observer(player).Throws = true;

            Assert.That(Deliver(player, matchIndex: 1), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(1));
            Assert.That(player.MatchHistory, Has.Count.EqualTo(1));

            Observer(player).Throws = false;
            Deliver(player, matchIndex: 2);

            Assert.That(Observer(player).ReceivedCompletions.Single().MatchId, Is.EqualTo(MatchTestDeals.MatchId(2)), "the consumer missed the game it broke on and nothing else");
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(2), "both games are on the record whatever the consumer did");
        }

        #endregion

        #region What the context offers, and what it withholds

        [Test]
        public void TheContextReachesTheConsumersOwnConfigAndTheirIdentity()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1);

            Assert.That(Observer(player).ConfigSeen, Is.SameAs(player.GameConfig));
            Assert.That(Observer(player).PlayerSeen, Is.EqualTo(player.PlayerId));
            Assert.That(Observer(player).CorrelationSeen.IsSet, Is.True);
        }

        [Test]
        public void ACorrelationKeyIsAFunctionOfTheGameAndNotOfTheMomentItWasAsked()
        {
            // The client and server copies of the timeline execute this action at different ticks, so a key
            // derived from the model's clock would differ between them. The key is derived from the table's
            // completion stamp instead.
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1);

            Assert.That(Observer(player).CorrelationSeen,
                Is.EqualTo(AnalyticsCorrelationId.Create(player.PlayerId, FinishedAt, "test_feature")));
            Assert.That(player.CurrentTime, Is.Not.EqualTo(FinishedAt), "the model's own clock happens to agree, so this proves nothing");
        }

        [Test]
        public void TheCompletionIsAlsoOfferedAgainstThePlayersOwnCalendar()
        {
            // Mission resets are in player-local time, and the SDK's schedules take a PlayerLocalTime. The
            // context has no model, so it must carry the player's UTC offset for a consumer to find the
            // activation a game falls in.
            PlayerModel player = NewPlayer();
            player.UpdateTimeZone(new PlayerTimeZoneInfo(MetaDuration.FromHours(-5)), isFirstLogin: false);

            Deliver(player, matchIndex: 1);

            Assert.That(Observer(player).CompletedAtLocalSeen.Time, Is.EqualTo(FinishedAt), "the local time is the table's stamp, not the model's clock");
            Assert.That(Observer(player).CompletedAtLocalSeen.UtcOffset, Is.EqualTo(MetaDuration.FromHours(-5)));
        }

        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <summary>The names of the public instance members <paramref name="type"/> declares, methods and constructors included.</summary>
        static IEnumerable<string> PublicSurface(Type type) =>
            type.GetProperties(Declared).Select(property => property.Name)
                .Concat(type.GetFields(Declared).Select(field => field.Name))
                .Concat(type.GetMethods(Declared).Where(method => !method.IsSpecialName).Select(method => method.Name))
                .Concat(type.GetConstructors(Declared).Select(constructor => constructor.Name));

        [Test]
        public void TheContextOffersExactlySevenThings()
        {
            // MatchCompletionContext is safe because of what it leaves out (its own summary says why), so this is
            // an exact allowlist: a denylist would miss a new member nobody thought to forbid. A new member must
            // carry a value the consumer cannot get any other way, and nothing reachable through it may be
            // writable (see LiveOpsEventSnapshot for why get-only is not enough). A consumer that can keep a value
            // in its own [NoChecksum] state must do that instead of widening the context.
            IEnumerable<string> exposed = PublicSurface(typeof(MatchCompletionContext));

            Assert.That(exposed, Is.EquivalentTo(new[]
            {
                nameof(MatchCompletionContext.Completion),
                nameof(MatchCompletionContext.PlayerId),
                nameof(MatchCompletionContext.GameConfig),
                nameof(MatchCompletionContext.CompletedAtLocal),
                nameof(MatchCompletionContext.LiveOpsEvents),
                nameof(MatchCompletionContext.Emit),
                nameof(MatchCompletionContext.CorrelationIdFor),
            }), "the match-completion context changed shape; widening it is a design decision, not a diff");

            // The fact's members are an exact allowlist for the same reason.
            IEnumerable<string> carried = typeof(MatchCompletion).GetProperties(Declared).Select(property => property.Name)
                .Concat(typeof(MatchCompletion).GetFields(Declared).Select(field => field.Name));

            Assert.That(carried, Is.EquivalentTo(new[]
            {
                nameof(MatchCompletion.MatchId),
                nameof(MatchCompletion.CompletedAt),
                nameof(MatchCompletion.Position),
                nameof(MatchCompletion.TricksWon),
                nameof(MatchCompletion.IsWin),
            }));
        }

        /// <summary>
        /// Pins the members of <see cref="LiveOpsEventSnapshot"/>, which the context hands to consumers. The context
        /// allowlist does not look inside it, so a property added here that returned the SDK's
        /// <see cref="LiveOpsEventScheduleInfo"/> would put a mutable dictionary on checksummed player state back
        /// in a consumer's reach without failing any other test. An added member must meet the same two
        /// conditions as a new context member.
        /// </summary>
        [Test]
        public void TheHeldEventOffersExactlyTheSevenValuesItCopies()
        {
            IEnumerable<string> exposed = PublicSurface(typeof(LiveOpsEventSnapshot));

            Assert.That(exposed, Is.EquivalentTo(new[]
            {
                nameof(LiveOpsEventSnapshot.Id),
                nameof(LiveOpsEventSnapshot.Phase),
                nameof(LiveOpsEventSnapshot.Content),
                nameof(LiveOpsEventSnapshot.HasSchedule),
                nameof(LiveOpsEventSnapshot.EnabledStartsAt),
                nameof(LiveOpsEventSnapshot.EnabledEndsAt),
                nameof(LiveOpsEventSnapshot.ConcludesAt),
                nameof(LiveOpsEventSnapshot.WindowContains),
                nameof(LiveOpsEventSnapshot.ToString),
            }), "the seam's view of a held LiveOps event changed shape; handing out one more reference is a design decision, not a diff");

            // Checked separately so the failure message names the specific risk: the SDK's schedule object must
            // not be reachable.
            Assert.That(
                typeof(LiveOpsEventSnapshot).GetProperties(Declared).Select(property => property.PropertyType),
                Has.None.EqualTo(typeof(LiveOpsEventScheduleInfo)),
                "the SDK's schedule is reachable again, and its phase table is a mutable dictionary on checksummed state");
        }

        #endregion

        #region The registration contract

        /// <summary>
        /// Returns the <c>[MetaMember]</c> fields and properties declared on <see cref="PlayerModel"/> itself.
        /// <para>
        /// The two tests that use this cannot see members of <c>PlayerModelBase</c>, such as the checksummed
        /// <c>LiveOpsEvents</c>, or a consumer nested inside another member. <see cref="IMatchCompletionObserver"/>
        /// requires a consumer's progress to live in its own <c>[NoChecksum]</c> member declared directly on
        /// <see cref="PlayerModel"/>, and these tests check only consumers that follow that rule.
        /// </para>
        /// </summary>
        static IEnumerable<MemberInfo> DeclaredModelMembers() =>
            typeof(PlayerModel)
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(member => member is PropertyInfo || member is FieldInfo)
                .Where(member => member.GetCustomAttribute<MetaMemberAttribute>() != null);

        static Type MemberType(MemberInfo member) =>
            member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

        static object MemberValue(MemberInfo member, PlayerModel player) =>
            member is PropertyInfo property ? property.GetValue(player) : ((FieldInfo)member).GetValue(player);

        [Test]
        public void EveryConsumerStateOnTheModelIsRegistered()
        {
            // A consumer state that implements the interface but is missing from CollectMatchCompletionObservers
            // never progresses, and nothing else fails. DeclaredModelMembers says which members this cannot see.
            PlayerModel player = TestPlayers.New(MetaTime.Epoch);

            List<IMatchCompletionObserver> collected = new List<IMatchCompletionObserver>();
            player.CollectMatchCompletionObservers(collected);

            foreach (MemberInfo member in DeclaredModelMembers())
            {
                if (!typeof(IMatchCompletionObserver).IsAssignableFrom(MemberType(member)))
                    continue;

                object value = MemberValue(member, player);
                Assert.That(value, Is.Not.Null, $"{member.Name} watches for a finished game but is null on a fresh player");
                Assert.That(collected, Has.Some.SameAs(value),
                    $"{member.Name} implements {nameof(IMatchCompletionObserver)} but is not registered in {nameof(PlayerModel.CollectMatchCompletionObservers)}");
            }
        }

        [Test]
        public void EveryConsumerStateOnTheModelIsExcludedFromTheChecksum()
        {
            // The fact arrives on an unsynchronized server action, and the SDK allows such an action to write
            // only members excluded from the checksum. Progress kept in a checksummed member would desync the
            // client and server when they apply the action at different ticks, and a desync closes the session.
            //
            // DeclaredModelMembers says which members this cannot see. PlayerModelBase.LiveOpsEvents is one of
            // them. The context hands consumers LiveOpsEventSnapshot instead of the SDK's event model, and the
            // weekly event keeps its progress in its own member on PlayerModel, which this check does see.
            foreach (MemberInfo member in DeclaredModelMembers())
            {
                if (!typeof(IMatchCompletionObserver).IsAssignableFrom(MemberType(member)))
                    continue;

                Assert.That(member.GetCustomAttribute<NoChecksumAttribute>(), Is.Not.Null,
                    $"{member.Name} is written from the match-completion seam and must carry [NoChecksum]");
            }
        }

        [Test]
        public void FourFeatureStatesObserveMatchCompletion()
        {
            // Documents which feature states are registered as consumers.
            PlayerModel player = new PlayerModel();

            List<IMatchCompletionObserver> collected = new List<IMatchCompletionObserver>();
            player.CollectMatchCompletionObservers(collected);

            Assert.That(collected, Has.Exactly(1).InstanceOf<PlayerMissionState>());
            Assert.That(collected, Has.Exactly(1).InstanceOf<PlayerTournamentState>());
            Assert.That(collected, Has.Exactly(1).InstanceOf<PlayerFirstWeekState>());
            Assert.That(collected, Has.Exactly(1).InstanceOf<PlayerWeeklyEventState>());
            Assert.That(collected, Has.Count.EqualTo(4));
        }

        #endregion
    }
}
