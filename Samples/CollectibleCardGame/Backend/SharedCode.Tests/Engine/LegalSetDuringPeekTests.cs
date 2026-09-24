using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The legal set changes across a held peek, with the phase and hand unchanged, so a client cache of it
    /// cannot be keyed on those (<c>Client/Services/MatchService.LegalIntents</c> drops it on every
    /// board change instead).
    /// </summary>
    [TestFixture]
    public class LegalSetDuringPeekTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        /// <summary>
        /// Seat 0 on turn with mana to spare, holding Pebble Collector — whose Hello holds a peek — and two
        /// cards it can still afford afterwards, so the set after the pause is non-empty for a reason the deal
        /// cannot take away.
        /// </summary>
        Scenario Peeking()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");   // Hello: look at 3, keep 1
            scenario.Seat(0).Hand("MeadowMouse");       // cost 1, no chosen target
            scenario.Seat(0).Hand("BusyBeaver");        // cost 2, no chosen target
            scenario.Seat(0).DeckOf(6, "GreyOwl");
            scenario.Seat(0).Mana(9, 9);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            return scenario.OnTurn(0);
        }

        /// <summary>
        /// The reproduction. While the peek is held the walk answers nothing; the answer and the beat release
        /// that follows it both leave the phase and the hand's stamp exactly where they were — so a cache keyed
        /// on those serves that empty set for the rest of the action window, which is a hand rendered
        /// unplayable with the mana to play it.
        /// </summary>
        [Test]
        public void TheLegalSetChangesAcrossAHeldChoiceWithoutThePhaseOrTheHandMoving()
        {
            MatchEngine engine = Peeking().Build(MatchTimings.Default);

            engine.Play(0, engine.SecretHand(0)[0]);
            Assert.That(engine.Rules.PendingChoice, Is.Not.Null, "the fixture needs the peek to be held");

            MatchPhase phaseAtPause = engine.Phase;

            Assert.That(LegalFor(engine, 0), Is.Empty, "nothing is legal while a resolution is held on this seat");

            // The answer. It resolves the peek and the turn goes on in the same phase.
            Assert.That(engine.ChooseKept(0, engine.Revealed(0)[0]), Is.EqualTo(MatchIntentResults.Success));

            Assert.That(engine.Phase, Is.EqualTo(phaseAtPause));

            // Same key, different answer. This is the whole finding: the cards this seat can afford are legal
            // again, and nothing a (hand stamp, phase) key can see has changed.
            List<MatchIntent> afterChoice = LegalFor(engine, 0);
            Assert.That(afterChoice, Is.Not.Empty,
                "the seat has mana and affordable cards, so the walk answers a non-empty set once the peek is answered");


            // Recomputed rather than remembered, and the same answer twice: a caller that recomputes per
            // render sees this set, and a caller that keyed on the pair would still be serving the empty one.
            List<MatchIntent> afterRelease = LegalFor(engine, 0);
            Assert.That(Describe(afterRelease), Is.EqualTo(Describe(LegalFor(engine, 0))),
                "the walk is a pure function of the state it is asked about");
            Assert.That(afterRelease, Is.Not.Empty);
            Assert.That(Describe(afterRelease), Is.EqualTo(Describe(afterChoice)),
                "releasing the beat is pacing rather than legality, so the set itself does not move with it");
        }

        /// <summary>
        /// The hand this client would hold, walked the way the board walks it: the delivered hand as its one
        /// argument, and the un-gated walk, because a client highlights what would be legal if the table were
        /// open (<c>Docs/client.md</c>).
        /// </summary>
        static List<MatchIntent> LegalFor(MatchEngine engine, int seat)
            => Legality.EnumerateLegalIntents(engine.Model, seat, engine.BuildHandView(seat));

        /// <summary> A legal set as comparable text, so a difference names the intents rather than the count. </summary>
        static string Describe(List<MatchIntent> intents)
        {
            List<string> lines = new List<string>(intents.Count);
            foreach (MatchIntent intent in intents)
            {
                switch (intent)
                {
                    case PlayCardIntent play:
                        lines.Add($"play {play.Card.Value} at {Describe(play.Target)}");
                        break;

                    case AttackIntent attack:
                        lines.Add($"attack with {attack.Attacker.Value} at {Describe(attack.Target)}");
                        break;

                    case EndTurnIntent:
                        lines.Add("end turn");
                        break;

                    default:
                        lines.Add(intent.GetType().Name);
                        break;
                }
            }

            return string.Join("\n", lines);
        }

        static string Describe(EffectTargetRef target)
            => target.IsNone ? "nothing"
                : target.IsDen ? $"den {target.DenSeat}"
                : $"critter {target.Critter.Value}";
    }
}
