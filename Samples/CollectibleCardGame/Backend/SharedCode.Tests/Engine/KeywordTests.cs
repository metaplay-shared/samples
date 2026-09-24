using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary> Summons one critter that has a Hello of its own, and nothing else. </summary>
    public sealed class SummonsATideScholarInterpreter : IEffectInterpreter
    {
        public void Resolve(EffectQueueItem item, IEffectContext ctx, IEffectMutations mut)
            => mut.Summon(item.ResolvingSeat, ctx.Config.Cards[CardId.FromString("TideScholar")], 1);
    }

    /// <summary> The five engine flags and the two trigger labels, one named test per ruling. </summary>
    [TestFixture]
    public class KeywordTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        Scenario Fresh()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(9, 9).DeckOf(8);
            scenario.Seat(1).Mana(9, 9).DeckOf(8);
            return scenario.OnTurn(0);
        }

        // ---------------------------------------------------------------- Guard

        [Test]
        public void DenIsUnreachableWhileAnAttackableGuardStands()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(1).Board("SunbeamRetriever");
            MatchEngine engine = scenario.Build();

            Assert.That(engine.Attack(0, frog, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.DenProtectedByGuard));
        }

        [Test]
        public void NonGuardCrittersAreAttackableWhileAGuardStands()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(1).Board("SunbeamRetriever");
            CardInstanceId mouse    = scenario.Seat(1).Board("MeadowMouse");
            MatchEngine    engine   = scenario.Build();

            // Guard protects the Den, not the team.
            Assert.That(engine.Attack(0, frog, EffectTargetRef.OnCritter(mouse)), Is.EqualTo(MatchIntentResults.Success));
        }

        [Test]
        public void TwoGuardsGateExactlyLikeOne()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");   // 15/15
            CardInstanceId goat     = scenario.Seat(0).Board("StrayGoat");  // 20/15
            CardInstanceId guardA   = scenario.Seat(1).Board("SunbeamRetriever");
            CardInstanceId guardB   = scenario.Seat(1).Board("GardenSnail", maxHealth: 15);
            MatchEngine engine = scenario.Build();

            Assert.That(engine.Attack(0, frog, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.DenProtectedByGuard));

            // Either Guard is attackable — the second is not hiding behind the first.
            Assert.That(engine.Attack(0, frog, EffectTargetRef.OnCritter(guardB)), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Critter(guardB), Is.Null);

            // One down, and the gate is still shut: it is binary, not a count.
            Assert.That(engine.Attack(0, goat, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.DenProtectedByGuard));
            Assert.That(engine.Critter(guardA), Is.Not.Null);
        }

        [Test]
        public void SneakyGuardDoesNotProtectTheDen()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(1).Board("SunbeamRetriever", keywords: KeywordFlags.Guard | KeywordFlags.Sneaky);
            MatchEngine engine = scenario.Build();

            // The gate counts only Guards the attacker could legally have attacked instead.
            Assert.That(engine.Attack(0, frog, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.Success));
        }

        [Test]
        public void ASmokeBombedGuardStopsGatingTheDen()
        {
            // The only route in the content to a Sneaky Guard: Smoke Bomb grants Sneaky in play, so a seat can
            // cloak its own Guard and open its own Den. Reading the gate as "is Guard set" would make the
            // cloaked Guard an unanswerable lock; counting only attackable Guards makes it a race.
            Assert.That(DenAfterCloaking("SunbeamRetriever"), Is.EqualTo(MatchIntentResults.Success));

            // Falsifiable: aimed at the other body, the same Guard still shuts the same Den.
            Assert.That(DenAfterCloaking("MeadowMouse"), Is.EqualTo(MatchIntentResults.DenProtectedByGuard));
        }

        /// <summary> Seat 0 cloaks one of its two critters, then seat 1 swings at seat 0's Den. </summary>
        MatchIntentResult DenAfterCloaking(string cloaked)
        {
            Scenario       scenario  = Fresh();
            CardInstanceId bomb      = scenario.Seat(0).Hand("SmokeBomb");
            CardInstanceId retriever = scenario.Seat(0).Board("SunbeamRetriever");   // 10/15 Guard
            CardInstanceId mouse     = scenario.Seat(0).Board("MeadowMouse");
            CardInstanceId frog      = scenario.Seat(1).Board("PondFrog");
            MatchEngine    engine    = scenario.Build();

            CardInstanceId target = cloaked == "SunbeamRetriever" ? retriever : mouse;
            Assert.That(engine.Play(0, bomb, EffectTargetRef.OnCritter(target)), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Critter(target).HasSneaky, Is.True, "the grant landed where the test aimed it");

            engine.PassTurn();
            return engine.Attack(1, frog, EffectTargetRef.Den(0));
        }

        [Test]
        public void SleepyGuardStillProtectsTheDen()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(1).Board("SunbeamRetriever", sleepy: true);
            MatchEngine engine = scenario.Build();

            Assert.That(engine.Attack(0, frog, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.DenProtectedByGuard));
        }

        [Test]
        public void EffectDamageReachesTheDenThroughAGuard()
        {
            Scenario       scenario = Fresh();
            CardInstanceId foxfire  = scenario.Seat(0).Hand("Foxfire"); // 3 damage to a critter or a Den
            scenario.Seat(1).Board("SunbeamRetriever");
            MatchEngine engine = scenario.Build();

            // The keyword says "can't attack your Den"; an effect is not an attack.
            Assert.That(engine.Play(0, foxfire, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(Config.Global.DenStartingHp - 15));
        }

        // ---------------------------------------------------------------- Zoomies

        [Test]
        public void ZoomiesCritterMayAttackImmediately()
        {
            Scenario       scenario = Fresh();
            CardInstanceId kit      = scenario.Seat(0).Hand("EmberKit");
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, kit);
            Assert.That(engine.Critter(kit).IsSleepy, Is.False);
            Assert.That(engine.Attack(0, kit, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.Success));
        }

        [Test]
        public void EffectSummonedZoomiesCritterMayAttackThisTurn()
        {
            Scenario       scenario = new Scenario(Config, weatherId: "SugarRush"); // every critter has Zoomies
            CardInstanceId shepherd = scenario.Seat(0).Hand("SheepdogShepherd");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, shepherd);

            List<BoardCritter> board = engine.Rules.Seat(0).Board;
            Assert.That(board.Count, Is.EqualTo(3), "the shepherd plus its two lambs");
            foreach (BoardCritter critter in board)
                Assert.That(critter.IsSleepy, Is.False, "a summoned critter with Zoomies may attack the turn it arrives");
        }

        // ---------------------------------------------------------------- Bubble

        [Test]
        public void BubbleAbsorbsTheWholeFirstDamageInstance()
        {
            // Case C: a 15/10 with a Bubble attacked by a 15/10.
            Scenario       scenario = Fresh();
            CardInstanceId bubbled  = scenario.Seat(1).Board("EmberKit", keywords: KeywordFlags.Bubble);
            CardInstanceId attacker = scenario.Seat(0).Board("SizzleWhisker", keywords: KeywordFlags.None); // 15/10
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, attacker, EffectTargetRef.OnCritter(bubbled));

            Assert.That(engine.Critter(bubbled).Damage, Is.EqualTo(0), "the Bubble ignores the whole instance, not one point of it");
            Assert.That(engine.Critter(bubbled).BubbleIntact, Is.False);
            Assert.That(engine.Critter(attacker), Is.Null, "the counterattack still killed the 3/2");
        }

        [Test]
        public void SecondDamageLandsAfterTheBubblePops()
        {
            Scenario       scenario = Fresh();
            CardInstanceId bubbled  = scenario.Seat(1).Board("BubbleDrifter"); // 15/10 Bubble
            CardInstanceId first    = scenario.Seat(0).Board("MeadowMouse");   // 5/10
            CardInstanceId second   = scenario.Seat(0).Board("MeadowMouse");
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, first, EffectTargetRef.OnCritter(bubbled));
            Assert.That(engine.Critter(bubbled).Damage, Is.EqualTo(0));

            engine.Attack(0, second, EffectTargetRef.OnCritter(bubbled));
            Assert.That(engine.Critter(bubbled).Damage, Is.EqualTo(5));
        }

        [Test]
        public void TwoBubbledCrittersTradeWithoutDamage()
        {
            // Case D.
            Scenario       scenario = Fresh();
            CardInstanceId mine     = scenario.Seat(0).Board("BubbleDrifter");
            CardInstanceId theirs   = scenario.Seat(1).Board("BubbleDrifter");
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, mine, EffectTargetRef.OnCritter(theirs));

            Assert.That(engine.Critter(mine).Damage, Is.EqualTo(0));
            Assert.That(engine.Critter(theirs).Damage, Is.EqualTo(0));
            Assert.That(engine.Critter(mine).BubbleIntact, Is.False);
            Assert.That(engine.Critter(theirs).BubbleIntact, Is.False);
        }

        [Test]
        public void ZeroDamageDoesNotPopABubble()
        {
            // Case E: a 0-attack critter is attacked by something with a Bubble.
            Scenario       scenario = Fresh();
            CardInstanceId attacker = scenario.Seat(0).Board("BubbleDrifter"); // 15/10 Bubble
            CardInstanceId snail    = scenario.Seat(1).Board("GardenSnail", maxHealth: 25); // 0-attack, and it survives
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, attacker, EffectTargetRef.OnCritter(snail));

            Assert.That(engine.Critter(snail).Damage, Is.EqualTo(15));
            Assert.That(engine.Critter(attacker).BubbleIntact, Is.True, "zero damage is not a damage instance");
        }

        [Test]
        public void BubbleAbsorbsEffectDamage()
        {
            Scenario       scenario = Fresh();
            CardInstanceId foxfire  = scenario.Seat(0).Hand("Foxfire");
            CardInstanceId bubbled  = scenario.Seat(1).Board("BubbleDrifter");
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, foxfire, EffectTargetRef.OnCritter(bubbled));

            Assert.That(engine.Critter(bubbled).Damage, Is.EqualTo(0), "the first damage from any source");
            Assert.That(engine.Critter(bubbled).BubbleIntact, Is.False);
        }

        [Test]
        public void StatReductionDoesNotPopABubble()
        {
            // A Bubble absorbs the first *damage*, not the first anything. A stat change is not damage.
            Scenario       scenario = Fresh();
            CardInstanceId thief    = scenario.Seat(0).Hand("WhiskerThief"); // Hello: an enemy critter gets -1/-1
            CardInstanceId bubbled  = scenario.Seat(1).Board("BubbleDrifter");
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, thief, EffectTargetRef.OnCritter(bubbled));

            Assert.That(engine.Critter(bubbled).Attack, Is.EqualTo(10), "the stats did move");
            Assert.That(engine.Critter(bubbled).MaxHealth, Is.EqualTo(5));
            Assert.That(engine.Critter(bubbled).BubbleIntact, Is.True, "and the Bubble is still there for real damage");
        }

        [Test]
        public void SnacktimeHealsForItsOwnEffectDamage()
        {
            // Snacktime applies to any damage the critter sources, including its own triggered effect.
            // Flame Dancer burns the enemy Den for one after it attacks.
            Scenario       scenario = Fresh();
            CardInstanceId dancer   = scenario.Seat(0).Board("FlameDancer", keywords: KeywordFlags.Snacktime); // 10/15
            scenario.Seat(0).Den(100);
            MatchEngine engine = scenario.Build();

            engine.Attack(0, dancer, EffectTargetRef.Den(1));

            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(Config.Global.DenStartingHp - 15), "two from the swing, one from the trigger");
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(115), "and Snacktime healed for both");
        }

        [Test]
        public void GuardNeverRedirectsDamageOntoItself()
        {
            // Guard protects the Den, not the team: an attack aimed past it lands where it was aimed.
            Scenario       scenario = Fresh();
            CardInstanceId badger    = scenario.Seat(0).Board("OldBadger");   // 20/25
            CardInstanceId guard     = scenario.Seat(1).Board("OldMossback"); // 35/40 Guard
            CardInstanceId bystander = scenario.Seat(1).Board("GreyOwl");     // 15/30
            MatchEngine    engine    = scenario.Build();

            engine.Attack(0, badger, EffectTargetRef.OnCritter(bystander));

            Assert.That(engine.Critter(bystander).Damage, Is.EqualTo(20), "the damage landed where it was aimed");
            Assert.That(engine.Critter(guard).Damage, Is.EqualTo(0), "the Guard took none of it");
            Assert.That(engine.Critter(badger).Damage, Is.EqualTo(15), "and the bystander, not the Guard, hit back");
        }

        // ---------------------------------------------------------------- Sneaky

        [Test]
        public void SneakyCritterCannotBeAttacked()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            CardInstanceId cat      = scenario.Seat(1).Board("MoonlitAlleycat");
            MatchEngine    engine   = scenario.Build();

            Assert.That(engine.Attack(0, frog, EffectTargetRef.OnCritter(cat)), Is.EqualTo(MatchIntentResults.TargetIsSneaky));
            Assert.That(engine.Attack(0, frog, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.Success),
                "and it is not a Guard, so the Den is open");
        }

        [Test]
        public void EnemyEffectCannotTargetASneakyCritter()
        {
            Scenario       scenario = Fresh();
            CardInstanceId foxfire  = scenario.Seat(0).Hand("Foxfire");
            CardInstanceId cat      = scenario.Seat(1).Board("MoonlitAlleycat");
            MatchEngine    engine   = scenario.Build();

            Assert.That(engine.Play(0, foxfire, EffectTargetRef.OnCritter(cat)), Is.EqualTo(MatchIntentResults.TargetIsSneaky));
        }

        [Test]
        public void FriendlyEffectMayTargetOwnSneakyCritter()
        {
            Scenario       scenario = Fresh();
            CardInstanceId honey    = scenario.Seat(0).Hand("HoneyFeast"); // Snack: +2/+2 to a friendly critter
            CardInstanceId cat      = scenario.Seat(0).Board("MoonlitAlleycat");
            MatchEngine    engine   = scenario.Build();

            // The keyword says "by the enemy".
            Assert.That(engine.Play(0, honey, EffectTargetRef.OnCritter(cat)), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Critter(cat).Attack, Is.EqualTo(25));
        }

        [Test]
        public void BoardSweepHitsSneakyCritters()
        {
            Scenario       scenario = Fresh();
            CardInstanceId storm    = scenario.Seat(0).Hand("CinderStorm"); // 5 damage to every enemy critter
            CardInstanceId cat      = scenario.Seat(1).Board("MoonlitAlleycat"); // 15/5 Sneaky
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, storm);

            // Sneaky blocks targeting, not an untargeted sweep.
            Assert.That(engine.Critter(cat), Is.Null);
        }

        [Test]
        public void SneakyIsLostWhenTheCritterDealsDamage()
        {
            // Case H: Moonlit Alleycat 15/5 Sneaky into the enemy Den.
            Scenario       scenario = Fresh();
            CardInstanceId cat      = scenario.Seat(0).Board("MoonlitAlleycat");
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, cat, EffectTargetRef.Den(1));

            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(Config.Global.DenStartingHp - 15));
            Assert.That(engine.Critter(cat).HasSneaky, Is.False, "it is attackable from now on");
        }

        [Test]
        public void SneakyIsNotLostByTakingDamage()
        {
            // The keyword lasts until the critter *deals* damage. Taking some is not dealing any, which is the
            // reachable half of the rule — a Sneaky critter can never deal zero, because it may not attack with
            // no attack and the enemy may not attack it at all.
            Scenario       scenario = Fresh();
            CardInstanceId storm    = scenario.Seat(0).Hand("CinderStorm");                 // 1 to every enemy critter
            CardInstanceId owl      = scenario.Seat(1).Board("GreyOwl", keywords: KeywordFlags.Sneaky); // 15/30
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, storm);

            Assert.That(engine.Critter(owl).Damage, Is.EqualTo(5));
            Assert.That(engine.Critter(owl).HasSneaky, Is.True, "it has dealt no damage, so it is still hidden");
        }

        [Test]
        public void SneakyAttackerTakesTheCounterattack()
        {
            Scenario       scenario = Fresh();
            CardInstanceId cat      = scenario.Seat(0).Board("MoonlitAlleycat"); // 15/5 Sneaky
            CardInstanceId tortoise = scenario.Seat(1).Board("WiseTortoise");    // 10/20
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, cat, EffectTargetRef.OnCritter(tortoise));

            // Sneaky prevents being declared a target, not the exchange it started.
            Assert.That(engine.Critter(cat), Is.Null);
            Assert.That(engine.Critter(tortoise).Damage, Is.EqualTo(15));
        }

        // ---------------------------------------------------------------- Snacktime

        [Test]
        public void SnacktimeHealsTheDenForDamageDealt()
        {
            Scenario       scenario = Fresh();
            CardInstanceId hound    = scenario.Seat(0).Board("BiscuitHound"); // 15/15 Snacktime
            scenario.Seat(0).Den(100);
            MatchEngine engine = scenario.Build();

            engine.Attack(0, hound, EffectTargetRef.Den(1));

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(115));
        }

        [Test]
        public void DyingSnacktimeCritterStillHealsItsDen()
        {
            // Case F: a 15/10 Snacktime into a 20/20, Den at 100.
            Scenario       scenario = Fresh();
            CardInstanceId hound    = scenario.Seat(0).Board("BiscuitHound", attack: 15, maxHealth: 10, keywords: KeywordFlags.Snacktime);
            CardInstanceId bandit   = scenario.Seat(1).Board("RaccoonRingleader", keywords: KeywordFlags.None); // 20/20
            scenario.Seat(0).Den(100);
            MatchEngine engine = scenario.Build();

            engine.Attack(0, hound, EffectTargetRef.OnCritter(bandit));

            Assert.That(engine.Critter(hound), Is.Null);
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(115), "its damage landed, so the heal happened");
        }

        [Test]
        public void SnacktimeHealsWhenDefendingToo()
        {
            Scenario       scenario = Fresh();
            CardInstanceId attacker = scenario.Seat(0).Board("MeadowMouse");  // 5/10
            CardInstanceId hound    = scenario.Seat(1).Board("BiscuitHound"); // 15/15 Snacktime
            scenario.Seat(1).Den(100);
            MatchEngine engine = scenario.Build();

            engine.Attack(0, attacker, EffectTargetRef.OnCritter(hound));

            // Any damage the critter sources, attack or counterattack.
            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(115));
        }

        [Test]
        public void DenHealCapsAtStartingHp()
        {
            // Case G: the same 3-point heal with the Den at 24 and the cap at 25.
            Scenario       scenario = Fresh();
            CardInstanceId hound    = scenario.Seat(0).Board("BiscuitHound");
            scenario.Seat(0).Den(120);
            MatchEngine engine = scenario.Build();

            engine.Attack(0, hound, EffectTargetRef.Den(1));

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(Config.Global.DenStartingHp));
        }

        [Test]
        public void CritterHealCapsAtMaxHealth()
        {
            Scenario       scenario = Fresh();
            CardInstanceId biscuit  = scenario.Seat(0).Hand("WarmBiscuit"); // restore 20
            CardInstanceId tortoise = scenario.Seat(0).Board("WiseTortoise", damage: 5); // 10/20 with 5 damage
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, biscuit, EffectTargetRef.OnCritter(tortoise));

            Assert.That(engine.Critter(tortoise).Damage, Is.EqualTo(0));
            Assert.That(engine.Critter(tortoise).CurrentHealth, Is.EqualTo(20), "healing cannot take it above its maximum");
        }

        // ---------------------------------------------------------------- Hello and Goodbye

        [Test]
        public void HelloTriggersOnPlayFromHand()
        {
            Scenario       scenario = Fresh();
            CardInstanceId scholar  = scenario.Seat(0).Hand("TideScholar"); // Hello: draw a card
            MatchEngine    engine   = scenario.Build();

            int handBefore = engine.SecretHand(0).Count;
            engine.Play(0, scholar);

            Assert.That(engine.SecretHand(0).Count, Is.EqualTo(handBefore), "one card left the hand and one was drawn");
            Assert.That(engine.SecretDeck(0).Count, Is.EqualTo(7));
        }

        [Test]
        public void HelloDoesNotFireForASummonedCopy()
        {
            // Summoning a card that *has* a Hello is the only way to test this. No launch content does it —
            // the Lamb token is blank — so the summon comes from a stub interpreter.
            SummonsATideScholarInterpreter interpreter = new SummonsATideScholarInterpreter();

            Scenario       scenario = Fresh();
            CardInstanceId trigger  = scenario.Seat(0).Hand("CinderStorm");
            MatchEngine    engine   = scenario.Build(MatchTimings.Instant).WithInterpreter(interpreter);

            int deckBefore = engine.SecretDeck(0).Count;
            engine.Play(0, trigger);

            Assert.That(engine.Rules.Seat(0).Board.Count, Is.EqualTo(1), "the Tide Scholar entered play");
            Assert.That(engine.SecretDeck(0).Count, Is.EqualTo(deckBefore),
                "…and its Hello did not draw, because it was summoned rather than played from hand");
        }

        [Test]
        public void GoodbyeTriggersOnDeath()
        {
            Scenario       scenario = Fresh();
            CardInstanceId whisker  = scenario.Seat(0).Board("SizzleWhisker", attack: 15, maxHealth: 5); // Goodbye: 5 to the enemy Den
            CardInstanceId goat     = scenario.Seat(1).Board("StrayGoat");
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, whisker, EffectTargetRef.OnCritter(goat));

            Assert.That(engine.Critter(whisker), Is.Null);
            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(Config.Global.DenStartingHp - 5));
        }

        [Test]
        public void GoodbyeSeesItsOwnStatsAtTheMomentOfDeath()
        {

            Scenario       scenario = Fresh();
            CardInstanceId whisker  = scenario.Seat(0).Board("SizzleWhisker", attack: 15, maxHealth: 5);
            CardInstanceId goat     = scenario.Seat(1).Board("StrayGoat");
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, whisker, EffectTargetRef.OnCritter(goat));

            EffectResolvedEvent resolved = engine.LastEventOf<EffectResolvedEvent>();
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.Source, Is.EqualTo(whisker),
                "the Goodbye resolved with its own critter as its source even though the board no longer has it");
            Assert.That(engine.ZoneOf(whisker), Is.EqualTo(AuthorityZone.Graveyard));
        }
    }
}
