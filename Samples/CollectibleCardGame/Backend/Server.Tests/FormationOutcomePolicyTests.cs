using Game.Server.Matchmaking;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The dissolved-pairing table, every row of it. With two seats a decline does not thin the roster, it
    /// dissolves the pairing — so what each seat is owed is a function of its own answer and of whether the
    /// formation committed, and the whole table is small enough to state outright.
    /// </summary>
    [TestFixture]
    public class FormationOutcomePolicyTests
    {
        static readonly MatchmakingSeatAnswer[] Answers =
        {
            MatchmakingSeatAnswer.Accepted,
            MatchmakingSeatAnswer.Declined,
            MatchmakingSeatAnswer.TimedOut,
        };

        [Test]
        public void BothAcceptedAndTheMintSucceeded_SeatsBoth()
        {
            FormationOutcome outcome = FormationOutcomePolicy.Decide(MatchmakingSeatAnswer.Accepted, MatchmakingSeatAnswer.Accepted, mintOk: true);

            Assert.That(outcome.Formed, Is.True);
            Assert.That(outcome.SeatA, Is.EqualTo(FormationSeatAction.Seated));
            Assert.That(outcome.SeatB, Is.EqualTo(FormationSeatAction.Seated));
        }

        [Test]
        public void AMintThatFailedRequeuesBoth()
        {
            // Both seats committed, so both answered, so both are demonstrably responsive — they go back with
            // the stamps they arrived with rather than being left staring at a searching screen forever.
            FormationOutcome outcome = FormationOutcomePolicy.Decide(MatchmakingSeatAnswer.Accepted, MatchmakingSeatAnswer.Accepted, mintOk: false);

            Assert.That(outcome.Formed, Is.False);
            Assert.That(outcome.SeatA, Is.EqualTo(FormationSeatAction.Requeue));
            Assert.That(outcome.SeatB, Is.EqualTo(FormationSeatAction.Requeue));
        }

        [Test]
        public void EverySeatCombinationBelowTheMint()
        {
            // The mint is only attempted when both accepted, so for every other combination it cannot matter
            // what a mint would have done — asserted for both values of it rather than assumed.
            foreach (MatchmakingSeatAnswer a in Answers)
            {
                foreach (MatchmakingSeatAnswer b in Answers)
                {
                    if (a == MatchmakingSeatAnswer.Accepted && b == MatchmakingSeatAnswer.Accepted)
                        continue;

                    foreach (bool mintOk in new[] { true, false })
                    {
                        FormationOutcome outcome = FormationOutcomePolicy.Decide(a, b, mintOk);

                        Assert.That(outcome.Formed, Is.False, $"{a}/{b} formed a table");
                        Assert.That(outcome.SeatA, Is.EqualTo(Expected(a)), $"{a}/{b}, seat A");
                        Assert.That(outcome.SeatB, Is.EqualTo(Expected(b)), $"{a}/{b}, seat B");
                    }
                }
            }
        }

        [Test]
        public void AnAnsweredSurvivorIsRequeuedAndALostAnswerIsNot()
        {
            // The whole rule, in the two rows the design names: a definite answer proves the actor is
            // responsive right now, and a timeout proves nothing about whether it ever will be.
            FormationOutcome declined = FormationOutcomePolicy.Decide(MatchmakingSeatAnswer.Accepted, MatchmakingSeatAnswer.Declined, mintOk: true);

            Assert.That(declined.SeatA, Is.EqualTo(FormationSeatAction.Requeue), "the survivor answered");
            Assert.That(declined.SeatB, Is.EqualTo(FormationSeatAction.Nothing), "and the decliner is owed nothing");

            FormationOutcome lost = FormationOutcomePolicy.Decide(MatchmakingSeatAnswer.TimedOut, MatchmakingSeatAnswer.Declined, mintOk: true);

            Assert.That(lost.SeatA, Is.EqualTo(FormationSeatAction.TellSeatGone),
                "an actor that could not answer once would be re-taken by the next formation and stall that one too");
        }

        [Test]
        public void TheSingleSeatFormIsTheSameFunction()
        {
            // A bot-fallback formation asks one human, so there is one answer and no pairing to dissolve.
            Assert.That(FormationOutcomePolicy.ForSeat(MatchmakingSeatAnswer.Accepted, formed: true), Is.EqualTo(FormationSeatAction.Seated));
            Assert.That(FormationOutcomePolicy.ForSeat(MatchmakingSeatAnswer.Accepted, formed: false), Is.EqualTo(FormationSeatAction.Requeue));
            Assert.That(FormationOutcomePolicy.ForSeat(MatchmakingSeatAnswer.Declined, formed: false), Is.EqualTo(FormationSeatAction.Nothing));
            Assert.That(FormationOutcomePolicy.ForSeat(MatchmakingSeatAnswer.TimedOut, formed: false), Is.EqualTo(FormationSeatAction.TellSeatGone));
        }

        [Test]
        public void EverySeatActionIsReachable()
        {
            // A table with an unreachable row is a table with a rule nothing enforces.
            HashSet<FormationSeatAction> seen = new HashSet<FormationSeatAction>();

            foreach (MatchmakingSeatAnswer a in Answers)
            {
                foreach (bool formed in new[] { true, false })
                    seen.Add(FormationOutcomePolicy.ForSeat(a, formed));
            }

            Assert.That(seen, Is.EquivalentTo(new[]
            {
                FormationSeatAction.Seated,
                FormationSeatAction.Requeue,
                FormationSeatAction.TellSeatGone,
                FormationSeatAction.Nothing,
            }));
        }

        static FormationSeatAction Expected(MatchmakingSeatAnswer answer) => answer switch
        {
            MatchmakingSeatAnswer.Accepted => FormationSeatAction.Requeue,
            MatchmakingSeatAnswer.TimedOut => FormationSeatAction.TellSeatGone,
            _                              => FormationSeatAction.Nothing,
        };
    }
}
