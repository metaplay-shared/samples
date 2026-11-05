using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests the replicated <see cref="MatchModel"/>: the board it builds from the engine, the server-only state
    /// it keeps off the wire, and the member private state that delivers a player's hand.
    /// </summary>
    [TestFixture]
    public class MatchModelTests
    {
        const ulong Seed = 909UL;

        static MatchModel NewModel()
        {
            MatchEngine engine = MatchEngine.Create(Seed, MatchTimings.Instant, MatchTestDeals.T0);
            return MatchTestDeals.Model(engine, MatchTestDeals.Seat0PlayerId);
        }

        #region The projection

        [Test]
        public void TheBoardProjectsTheEnginesPublicState()
        {
            MatchModel  model  = NewModel();
            MatchEngine engine = model.Engine;
            MatchBoard  board  = model.Board;

            Assert.That(board.TrumpCard, Is.EqualTo(engine.TrumpCard), $"seed {Seed}");
            Assert.That(board.TurnPhase, Is.EqualTo(engine.TurnPhase), $"seed {Seed}");
            Assert.That(board.StartingLeaderSeat, Is.EqualTo(engine.StartingLeaderSeat), $"seed {Seed}");
            Assert.That(board.CurrentLeaderSeat, Is.EqualTo(engine.CurrentLeaderSeat), $"seed {Seed}");
            Assert.That(board.SeatOnTurn, Is.EqualTo(engine.SeatOnTurn), $"seed {Seed}");
            Assert.That(board.PlayIndex, Is.EqualTo(engine.PlayIndex), $"seed {Seed}");
            Assert.That(board.Plays, Is.EqualTo(engine.Plays), $"seed {Seed}");
            Assert.That(board.TrickWinnerSeats, Is.EqualTo(engine.TrickWinnerSeats), $"seed {Seed}");
            Assert.That(board.MoveDeadlineAt, Is.EqualTo(engine.MoveDeadlineAt), $"seed {Seed}");
            Assert.That(board.ResolvePauseEndsAt, Is.EqualTo(engine.ResolvePauseEndsAt), $"seed {Seed}");

            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
            {
                Assert.That(board.GetCardsRemaining(seat), Is.EqualTo(engine.GetCardsRemaining(seat)), $"seed {Seed}, seat {seat}");
                Assert.That(board.GetTricksWon(seat), Is.EqualTo(engine.GetTricksWon(seat)), $"seed {Seed}, seat {seat}");
            }
        }

        [Test]
        public void TheBoardTracksTheEngineThroughAWholeGame()
        {
            MatchModel model = NewModel();
            MatchTestDeals.PlayWholeGame(model, new TestMatchHost(model, Seed), Seed);

            MatchEngine engine = model.Engine;
            MatchBoard  board  = model.Board;

            Assert.That(board.Plays, Is.EqualTo(engine.Plays), $"seed {Seed}");
            Assert.That(board.TrickWinnerSeats, Is.EqualTo(engine.TrickWinnerSeats), $"seed {Seed}");
            Assert.That(board.TurnPhase, Is.EqualTo(MatchTurnPhase.Finished), $"seed {Seed}");
            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended), $"seed {Seed}");
            Assert.That(board.Standings, Is.EqualTo(engine.ComputeStandings()), $"seed {Seed}");
        }

        #endregion

        #region The two private channels

        [Test]
        public void PrivateStateRoundTripsAHandToItsOwnSeat()
        {
            MatchModel host = NewModel();

            MultiplayerMemberPrivateStateBase state = host.GetMemberPrivateState(MatchTestDeals.Seat0PlayerId);
            Assert.That(state, Is.Not.Null, $"seed {Seed}: no private state for a seated player");

            // Same steps as the SDK: serialize per subscriber, deserialize on the client, and apply to the
            // client's model, which has no server-only state.
            byte[] bytes = MetaSerialization.SerializeTagged(state, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
            MultiplayerMemberPrivateStateBase received = MetaSerialization.DeserializeTagged<MultiplayerMemberPrivateStateBase>(
                bytes, MetaSerializationFlags.SendOverNetwork, resolver: null, logicVersion: null);

            MatchModel client = OverTheWire(host);
            received.ApplyToModel(client);

            Assert.That(client.OwnSeat, Is.EqualTo(0), $"seed {Seed}");
            Assert.That(client.GetOwnHand(), Is.EqualTo(host.Engine.GetHand(0)), $"seed {Seed}");
        }

        [Test]
        public void PrivateStateIsOnlyBuiltForASeatedPlayer()
        {
            MatchModel host = NewModel();
            Assert.That(host.GetMemberPrivateState(EntityId.Create(EntityKindCore.Player, 999UL)), Is.Null, $"seed {Seed}");
            Assert.That(host.GetMemberPrivateState(EntityId.None), Is.Null, $"seed {Seed}");
        }

        [Test]
        public void AConfirmedPlayLeavesTheOwnHandWithNoNewDelivery()
        {
            // The client's own hand is derived from the delivered hand and the board's plays, so a confirmed
            // play leaves the hand without a new delivery.
            MatchModel host   = NewModel();
            MatchModel client = OverTheWire(host);
            host.GetMemberPrivateState(MatchTestDeals.Seat0PlayerId).ApplyToModel(client);

            int  seat = host.Engine.SeatOnTurn;
            Card card = host.Engine.GetLegalPlays(seat)[0];
            MatchTestDeals.Apply(client, PlayAndApply(host, seat, card), Seed);

            if (seat == 0)
                Assert.That(client.GetOwnHand(), Has.Count.EqualTo(MatchRules.CardsPerSeat - 1).And.No.Member(card), $"seed {Seed}");
            else
                Assert.That(client.GetOwnHand(), Has.Count.EqualTo(MatchRules.CardsPerSeat), $"seed {Seed}");
        }

        [Test]
        public void TheOwnHandFollowsTheHostThroughAGameTheHostPlays()
        {
            // The host plays every card here, including seat 0's, as it does for a covering bot or a deadline
            // auto-play. A hand changes only through public plays, so the hand delivered at subscribe stays correct
            // after every action, with no further delivery.
            MatchModel    host   = NewModel();
            MatchModel    client = OverTheWire(host);
            TestMatchHost shell  = new TestMatchHost(host, Seed);
            host.GetMemberPrivateState(MatchTestDeals.Seat0PlayerId).ApplyToModel(client);

            int numApplied = 0;
            for (int guard = 0; host.Phase == MatchPhase.Playing; guard++)
            {
                Assert.That(guard, Is.LessThan(MatchRules.NumPlays * 4), $"seed {Seed}: the game did not finish");

                if (host.Engine.TurnPhase == MatchTurnPhase.AwaitingMove)
                {
                    int  onTurn = host.Engine.SeatOnTurn;
                    Card card   = BotPolicy.DecidePlayOut(MatchSeatView.ForSeat(host.Engine, onTurn), Seed).Card;
                    Assert.That(MatchHost.TryPlayForSeat(host, onTurn, card, MatchTestDeals.T0, shell), Is.True, $"seed {Seed}");
                }
                else
                {
                    Assert.That(MatchHost.TryAdvance(host, MatchTestDeals.T0, shell), Is.True, $"seed {Seed}");
                }

                for (; numApplied < shell.PublishedActions.Count; numApplied++)
                    MatchTestDeals.Apply(client, shell.PublishedActions[numApplied], Seed);

                Assert.That(client.GetOwnHand(), Is.EqualTo(host.Engine.GetHand(0)), $"seed {Seed}: the client's hand diverged at play index {host.Board.PlayIndex}");
            }
        }

        #endregion

        #region Actions replay against a stripped model

        [Test]
        public void EveryActionOfAWholeGameReplaysOnAModelWithNoServerOnlyState()
        {
            // Every action must carry the values it needs in its payload and touch only replicated members. The
            // client model here was serialized over the wire, so its engine is null and an action that used the
            // engine would throw.
            MatchModel    host   = NewModel();
            MatchModel    client = OverTheWire(host);
            TestMatchHost shell  = new TestMatchHost(host, Seed);

            Assert.That(client.Engine, Is.Null, $"seed {Seed}: the follower was not actually stripped");

            MatchTestDeals.PlayWholeGame(host, shell, Seed);
            List<MatchAction> actions = shell.PublishedActions;
            foreach (MatchAction action in actions)
                MatchTestDeals.Apply(client, action, Seed);

            Assert.That(actions, Has.Count.EqualTo(MatchRules.NumPlays + MatchRules.NumTricks), $"seed {Seed}");
            Assert.That(BoardBytes(client), Is.EqualTo(BoardBytes(host)), $"seed {Seed}: the follower's board diverged from the host's");
            Assert.That(client.Phase, Is.EqualTo(MatchPhase.Ended), $"seed {Seed}");
            Assert.That(client.Board.Standings, Is.EqualTo(host.Board.Standings), $"seed {Seed}");
        }

        [Test]
        public void EveryMatchActionIsLeaderSynchronizedOnly()
        {
            // A client can enqueue actions on any multiplayer entity, and the SDK's validation hook allows them by
            // default, so the LeaderSynchronized flag is required to keep the host the only writer. The check reads
            // each concrete action's spec from ModelActionRepository, because the generator uses the nearest
            // attribute in the type chain and an action could override its base's.
            foreach (System.Type type in MatchActionTypes())
            {
                ModelActionSpec spec = ModelActionRepository.Instance.SpecFromType[type];
                Assert.That(spec.ExecuteFlags, Is.EqualTo(ModelActionExecuteFlags.LeaderSynchronized), $"{type.Name} allows an issuer other than the timeline leader");
            }
        }

        [Test]
        public void NoMatchActionKeepsAMemberOffTheWire()
        {
            // An action is broadcast as-is and re-executed on every client. A member excluded from the wire by a
            // Hidden flag or by [IgnoreDataMember] would have its real value on the host and the default value on
            // every client, so the clients would compute a different result.
            foreach (System.Type type in MatchActionTypes())
            {
                if (MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType spec) && spec.Members != null)
                {
                    foreach (MetaSerializableMember member in spec.Members)
                        Assert.That(member.Flags.HasFlag(MetaMemberFlags.Hidden), Is.False, $"{type.Name}.{member.Name} is hidden from the wire");
                }

                foreach (System.Reflection.MemberInfo member in type.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    Assert.That(member.GetCustomAttributes(typeof(System.Runtime.Serialization.IgnoreDataMemberAttribute), inherit: false), Is.Empty, $"{type.Name}.{member.Name} is kept off the wire");
            }
        }

        static IEnumerable<System.Type> MatchActionTypes()
        {
            foreach (System.Type type in typeof(MatchAction).Assembly.GetTypes())
            {
                if (!type.IsAbstract && typeof(MatchAction).IsAssignableFrom(type))
                    yield return type;
            }
        }

        [Test]
        public void TheAuthoritativeStateIsServerOnlyAndNotMerelyHidden()
        {
            // A Hidden member is still checksummed, and the SDK's desync report serializes every member that is
            // not excluded from the checksum. A Hidden engine would put the whole deck in a desync report.
            foreach (string memberName in new string[] { "_engine", "_seatBotProfiles", "_ownHand" })
            {
                System.Reflection.FieldInfo field = typeof(MatchModel).GetField(memberName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, $"{memberName} is gone; this test needs updating with it");

                MetaMemberAttribute member = (MetaMemberAttribute)System.Attribute.GetCustomAttribute(field, typeof(MetaMemberAttribute));
                Assert.That(member, Is.Not.Null, $"{memberName} is not a serialized member");

                // Flags can be set inline on [MetaMember(n, flags)] or as separate attributes, so both are
                // collected.
                MetaMemberFlags flags = member.Flags;
                foreach (MetaMemberFlagAttribute flagAttribute in field.GetCustomAttributes(typeof(MetaMemberFlagAttribute), inherit: false))
                    flags |= flagAttribute.Flags;

                Assert.That(flags, Is.EqualTo(MetaMemberFlags.ServerOnly), $"{memberName} is not ServerOnly");
            }
        }

        [Test]
        public void TheDirectedMessagesRoundTripOverTheWire()
        {
            // No other test in this project serializes these messages, and the host sends MatchMoveRefused only
            // when a move is refused. A message that fails to serialize fails at run time, not at build time.
            MatchModel host = NewModel();

            Card             card    = host.Engine.GetHand(0)[0];
            MatchMoveRefused refused = RoundTrip(new MatchMoveRefused(3, card, MoveRefusalReason.StalePlayIndex));
            Assert.That(refused.PlayIndex, Is.EqualTo(3), $"seed {Seed}");
            Assert.That(refused.Card, Is.EqualTo(card), $"seed {Seed}");
            Assert.That(refused.Reason, Is.EqualTo(MoveRefusalReason.StalePlayIndex), $"seed {Seed}");

            MatchPlayCardRequest request = RoundTrip(new MatchPlayCardRequest(7, card));
            Assert.That(request.PlayIndex, Is.EqualTo(7), $"seed {Seed}");
            Assert.That(request.Card, Is.EqualTo(card), $"seed {Seed}");
        }

        #endregion

        #region The submitter's seat

        /// <summary>
        /// Checks that a move from a player with no seat at the table is refused. A bot seat's owner is
        /// <see cref="EntityId.None"/>, so a submitter of <see cref="EntityId.None"/> must not match a bot seat.
        /// The host plays bot seats through <see cref="MatchHost.TryPlayForSeat"/>, which takes no submitter.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void AMoveFromAPlayerWithNoSeatIsRefused(bool submitterIsNone)
        {
            MatchModel model     = NewModel();
            Card       card      = model.Engine.GetLegalPlays(model.Engine.SeatOnTurn)[0];
            EntityId   submitter = submitterIsNone ? EntityId.None : EntityId.Create(EntityKindCore.Player, 999UL);

            Assert.That(
                MatchHost.TrySubmitMove(model, submitter, model.Board.PlayIndex, card, MatchTestDeals.T0, TestMatchHostEnvironments.Publishing(NeverPublished)),
                Is.EqualTo(MoveRefusalReason.NotYourSeat), $"seed {Seed}");
        }

        [Test]
        public void AMoveNamingAStalePlayIndexIsRefusedBeforeLegality()
        {
            MatchModel model = NewModel();
            MatchTestDeals.PlayUntilSeatIsOnTurn(model, 0, new TestMatchHost(model, Seed), Seed);

            Card card = model.Engine.GetLegalPlays(0)[0];
            Assert.That(
                MatchHost.TrySubmitMove(model, MatchTestDeals.Seat0PlayerId, model.Board.PlayIndex - 1, card, MatchTestDeals.T0, TestMatchHostEnvironments.Publishing(NeverPublished)),
                Is.EqualTo(MoveRefusalReason.StalePlayIndex), $"seed {Seed}");

            TestMatchHost host = new TestMatchHost(model, Seed);
            Assert.That(
                MatchHost.TrySubmitMove(model, MatchTestDeals.Seat0PlayerId, model.Board.PlayIndex, card, MatchTestDeals.T0, host),
                Is.EqualTo(MoveRefusalReason.None), $"seed {Seed}");
            Assert.That(((MatchCardPlayed)host.PublishedActions.Single()).Card, Is.EqualTo(card), $"seed {Seed}");
        }

        #endregion

        #region Helpers

        static MatchModel OverTheWire(MatchModel model) => TestMatchHost.OverTheWire(model);

        static T RoundTrip<T>(T message) where T : MetaMessage
        {
            byte[] bytes = MetaSerialization.SerializeTagged<MetaMessage>(message, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
            return (T)MetaSerialization.DeserializeTagged<MetaMessage>(bytes, MetaSerializationFlags.SendOverNetwork, resolver: null, logicVersion: null);
        }

        static byte[] BoardBytes(MatchModel model)
            => MetaSerialization.SerializeTagged(model.Board, MetaSerializationFlags.IncludeAll, logicVersion: null);

        /// <summary>
        /// A publish callback that fails the test when called. A refused move must not be published.
        /// </summary>
        static bool NeverPublished(MatchAction action)
        {
            Assert.Fail($"seed {Seed}: a refused move must not be published, but {action.GetType().Name} was");
            return false;
        }

        /// <summary>Plays <paramref name="card"/> for <paramref name="seat"/> and returns the action the host published.</summary>
        static MatchCardPlayed PlayAndApply(MatchModel model, int seat, Card card)
        {
            TestMatchHost host = new TestMatchHost(model, Seed);
            Assert.That(MatchHost.TryPlayForSeat(model, seat, card, MatchTestDeals.T0, host), Is.True, $"seed {Seed}: the engine refused seat {seat} playing {card}");
            return (MatchCardPlayed)host.PublishedActions.Single();
        }

        #endregion
    }
}
