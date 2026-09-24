using Metaplay.Core;
using Metaplay.Core.Model;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests <see cref="MatchHost"/>, the turn-flow driver shared by the server and browser hosts: the order in
    /// which it publishes an action and advances the engine, and the wake time it reports to a host. The game
    /// rules are tested in <see cref="MatchEngineTests"/>.
    /// </summary>
    [TestFixture]
    public class MatchHostTests
    {
        const ulong Seed = 4242UL;

        static MatchModel NewModel(MatchTimings timings)
            => MatchTestDeals.Model(MatchEngine.Create(Seed, timings, MatchTestDeals.T0), MatchTestDeals.Seat0PlayerId);

        #region The action goes on the timeline before the engine advances

        [Test]
        public void AMoveThatCannotBePublishedLeavesTheEngineWhereItWas()
        {
            // The engine is host-only and not checksummed, so no consistency check would catch an engine one
            // play ahead of the board. It would refuse every later move on the play index. MatchHost therefore
            // publishes the action first and advances the engine only after the publish succeeds.
            MatchModel model = HumanLeadsModel();
            Card       card  = model.Engine.GetLegalPlays(0)[0];

            Assert.That(
                MatchHost.TrySubmitMove(model, MatchTestDeals.Seat0PlayerId, 0, model.Board.PlayIndex, card, MatchTestDeals.T0, TestMatchHostEnvironments.Refusing),
                Is.EqualTo(MoveRefusalReason.NotPublished), "the submitter is told the move was not played");

            Assert.That(model.Engine.PlayIndex, Is.EqualTo(0), "the engine played a card the board never saw");
            Assert.That(model.Engine.GetHand(0), Does.Contain(card), "the card left the hand");
            Assert.That(model.Board.PlayIndex, Is.EqualTo(0));
            Assert.That(model.Engine.PlayIndex, Is.EqualTo(model.Board.PlayIndex), "the engine and the board disagree");

            // The same move succeeds through a publishing host, which shows the refusal above came from the
            // failed publish and not from an illegal move.
            Assert.That(
                MatchHost.TrySubmitMove(model, MatchTestDeals.Seat0PlayerId, 0, model.Board.PlayIndex, card, MatchTestDeals.T0, new TestMatchHost(model, Seed)),
                Is.EqualTo(MoveRefusalReason.None));
            Assert.That(model.Engine.PlayIndex, Is.EqualTo(1));
            Assert.That(model.Board.PlayIndex, Is.EqualTo(1));
        }

        [Test]
        public void APlayForSeatThatCannotBePublishedLeavesTheEngineWhereItWas()
        {
            MatchModel model  = NewModel(MatchTimings.Instant);
            int        onTurn = model.Engine.SeatOnTurn;
            Card       card   = model.Engine.GetLegalPlays(onTurn)[0];

            Assert.That(MatchHost.TryPlayForSeat(model, onTurn, card, MatchTestDeals.T0, TestMatchHostEnvironments.Refusing), Is.False, $"seed {Seed}");

            Assert.That(model.Engine.PlayIndex, Is.EqualTo(0), $"seed {Seed}");
            Assert.That(model.Engine.GetHand(onTurn), Does.Contain(card), $"seed {Seed}");
            Assert.That(model.Engine.PlayIndex, Is.EqualTo(model.Board.PlayIndex), $"seed {Seed}");
        }

        [Test]
        public void AnAdvanceThatCannotBePublishedLeavesTheEngineInThePause()
        {
            MatchModel model = NewModel(MatchTimings.Instant);
            PlayWholeTrick(model);

            Assert.That(model.Engine.TurnPhase, Is.EqualTo(MatchTurnPhase.ResolvingTrick), $"seed {Seed}");

            Assert.That(MatchHost.TryAdvance(model, MatchTestDeals.T0, TestMatchHostEnvironments.Refusing), Is.False, $"seed {Seed}");

            Assert.That(model.Engine.TurnPhase, Is.EqualTo(MatchTurnPhase.ResolvingTrick), $"seed {Seed}: the engine left a pause the board is still in");
            Assert.That(model.Board.TurnPhase, Is.EqualTo(model.Engine.TurnPhase), $"seed {Seed}");

            // The same call through a publishing host advances the engine and the board together.
            Assert.That(MatchHost.TryAdvance(model, MatchTestDeals.T0, new TestMatchHost(model, Seed)), Is.True, $"seed {Seed}");
            Assert.That(model.Engine.TurnPhase, Is.EqualTo(MatchTurnPhase.AwaitingMove), $"seed {Seed}");
            Assert.That(model.Board.TurnPhase, Is.EqualTo(model.Engine.TurnPhase), $"seed {Seed}");
        }

        #endregion

        #region What the table is waiting on

        [Test]
        public void AFreshTableWithNoDeadlineIsWaitingOnNothing()
        {
            // With no move deadline and no pause, the table has no timer to wake for, so GetNextWakeAt returns
            // MetaTime.Epoch.
            MatchModel model = NewModel(MatchTimings.Instant);

            Assert.That(model.Board.HasMoveDeadline, Is.False, $"seed {Seed}");
            Assert.That(MatchHost.GetNextWakeAt(model, null), Is.EqualTo(MetaTime.Epoch), $"seed {Seed}");
        }

        [Test]
        public void ATableWithADeadlineIsWaitingOnIt()
        {
            MatchTimings timings = MatchTimings.Instant;
            timings.MoveDeadline = MetaDuration.FromSeconds(20);

            MatchModel model = NewModel(timings);

            Assert.That(MatchHost.GetNextWakeAt(model, null), Is.EqualTo(MatchTestDeals.T0 + MetaDuration.FromSeconds(20)), $"seed {Seed}");
        }

        [Test]
        public void ATableInAResolvePauseIsWaitingOnTheEndOfIt()
        {
            MatchTimings timings = MatchTimings.Instant;
            timings.ResolvePause = MetaDuration.FromSeconds(2);

            MatchModel model = NewModel(timings);
            PlayWholeTrick(model);

            Assert.That(model.Board.TurnPhase, Is.EqualTo(MatchTurnPhase.ResolvingTrick), $"seed {Seed}");
            Assert.That(MatchHost.GetNextWakeAt(model, null), Is.EqualTo(MatchTestDeals.T0 + MetaDuration.FromSeconds(2)), $"seed {Seed}");
        }

        [Test]
        public void AFinishedTableIsWaitingOnNothing()
        {
            MatchModel model = NewModel(MatchTimings.Instant);
            PlayWholeGame(model);

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended), $"seed {Seed}");
            Assert.That(MatchHost.GetNextWakeAt(model, null), Is.EqualTo(MetaTime.Epoch), $"seed {Seed}");
        }

        #endregion

        #region The end stamp

        [Test]
        public void TheEndStampIsWrittenOnceWhenTheMatchEnds()
        {
            // The end stamp records when the game ended, so persisting or re-running a finished table must not
            // change it.
            MatchModel model = NewModel(MatchTimings.Instant);
            Assert.That(model.EndedAt, Is.EqualTo(MetaTime.Epoch), $"seed {Seed}: an unfinished match carries no end stamp");

            PlayWholeGame(model);

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended), $"seed {Seed}");
            Assert.That(model.EndedAt, Is.EqualTo(MatchTestDeals.T0), $"seed {Seed}: the stamp is the host's, carried in the action");

            // Replaying the same action against a finished match is refused, so nothing can restamp it.
            MetaActionResult replayed = new MatchAdvanced(MatchTurnPhase.Finished, MatchPhase.Ended, MetaTime.Epoch, MatchTestDeals.T0 + MetaDuration.FromMinutes(5))
                .InvokeExecute(model, commit: true);

            Assert.That(replayed.IsSuccess, Is.False, $"seed {Seed}");
            Assert.That(model.EndedAt, Is.EqualTo(MatchTestDeals.T0), $"seed {Seed}: the end stamp moved");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Creates a table from a fixed deal in which the human seat 0 leads, so a test can submit a move as
        /// the player without depending on which seat a shuffled deal draws as the leader.
        /// </summary>
        static MatchModel HumanLeadsModel()
        {
            List<Card> deck = MatchTestDeals.BuildDeck(
                seat0:     new Card[] { MatchTestDeals.Clubs(Rank.Ace),   MatchTestDeals.Clubs(Rank.King),  MatchTestDeals.Clubs(Rank.Queen), MatchTestDeals.Clubs(Rank.Jack), MatchTestDeals.Clubs(Rank.Ten) },
                seat1:     new Card[] { MatchTestDeals.Diamonds(Rank.Ace), MatchTestDeals.Diamonds(Rank.King), MatchTestDeals.Diamonds(Rank.Queen), MatchTestDeals.Diamonds(Rank.Jack), MatchTestDeals.Diamonds(Rank.Ten) },
                seat2:     new Card[] { MatchTestDeals.Hearts(Rank.Ace),  MatchTestDeals.Hearts(Rank.King), MatchTestDeals.Hearts(Rank.Queen), MatchTestDeals.Hearts(Rank.Jack), MatchTestDeals.Hearts(Rank.Ten) },
                seat3:     new Card[] { MatchTestDeals.Spades(Rank.Ace),  MatchTestDeals.Spades(Rank.King), MatchTestDeals.Spades(Rank.Queen), MatchTestDeals.Spades(Rank.Jack), MatchTestDeals.Spades(Rank.Ten) },
                trumpCard: MatchTestDeals.Spades(Rank.Two));

            return MatchTestDeals.Model(MatchTestDeals.Engine(Seed, deck, startingLeaderSeat: 0), MatchTestDeals.Seat0PlayerId);
        }

        /// <summary>Plays the bot policy's card for the seat on turn through a publishing host.</summary>
        static void PlayForSeatOnTurn(MatchModel model)
        {
            int  onTurn = model.Engine.SeatOnTurn;
            Card card   = BotPolicy.DecidePlayOut(MatchSeatView.ForSeat(model.Engine, onTurn), Seed).Card;
            Assert.That(MatchHost.TryPlayForSeat(model, onTurn, card, MatchTestDeals.T0, new TestMatchHost(model, Seed)), Is.True, $"seed {Seed}");
        }

        static void PlayWholeTrick(MatchModel model)
        {
            for (int play = 0; play < MatchRules.CardsPerTrick; play++)
                PlayForSeatOnTurn(model);
        }

        static void PlayWholeGame(MatchModel model) => MatchTestDeals.PlayWholeGame(model, new TestMatchHost(model, Seed), Seed);

        #endregion
    }
}
