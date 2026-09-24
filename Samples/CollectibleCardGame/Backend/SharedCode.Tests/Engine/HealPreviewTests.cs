using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// What the board tells a player a heal would do, against what the resolution then does. The heal cap is real and invisible — a Den at its starting hit points is a legal target that
    /// restores nothing — so the preview exists to say so at the moment of choice. Every case here asserts the
    /// preview <em>and</em> plays the card, because the point of the query is that the two agree.
    /// </summary>
    [TestFixture]
    public class HealPreviewTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        Scenario Fresh()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(9, 9).DeckOf(8);
            scenario.Seat(1).Mana(9, 9).DeckOf(8);
            return scenario.OnTurn(0);
        }

        CardInfo Card(string cardId) => Config.Cards[CardId.FromString(cardId)];

        [Test]
        public void ADenAtFullHealth_PreviewsNothing_AndTheHealDoesNothing()
        {
            Scenario       scenario = Fresh();
            CardInstanceId biscuit  = scenario.Seat(0).Hand("WarmBiscuit"); // restore 20
            MatchEngine    engine   = scenario.Build();

            int? preview = HealPreview.ForTarget(engine.Rules, Config.Global, Card("WarmBiscuit"), 1, EffectTargetRef.Den(0));
            Assert.That(preview, Is.EqualTo(0), "the Den starts at the cap, so there is nothing to restore");

            int before = engine.Rules.Seat(0).DenHp;
            engine.Clear();
            engine.Play(0, biscuit, EffectTargetRef.Den(0));

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(before));
            Assert.That(engine.EventsOf<HealedEvent>(), Is.Empty, "nothing was healed, so nothing was announced");
        }

        [Test]
        public void ADamagedDen_PreviewsWhatTheCapLeaves()
        {
            Scenario       scenario = Fresh();
            CardInstanceId biscuit  = scenario.Seat(0).Hand("WarmBiscuit"); // restore 20
            scenario.Seat(0).Den(Config.Global.DenStartingHp - 10);
            MatchEngine engine = scenario.Build();

            int? preview = HealPreview.ForTarget(engine.Rules, Config.Global, Card("WarmBiscuit"), 1, EffectTargetRef.Den(0));
            Assert.That(preview, Is.EqualTo(10), "twenty points offered, ten points of room");

            engine.Play(0, biscuit, EffectTargetRef.Den(0));

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(Config.Global.DenStartingHp));
        }

        [Test]
        public void ADamagedCritter_PreviewsItsDamage_NotTheCardsAmount()
        {
            Scenario       scenario = Fresh();
            CardInstanceId biscuit  = scenario.Seat(0).Hand("WarmBiscuit"); // restore 20
            CardInstanceId tortoise = scenario.Seat(0).Board("WiseTortoise", damage: 5);
            MatchEngine    engine   = scenario.Build();

            int? preview = HealPreview.ForTarget(engine.Rules, Config.Global, Card("WarmBiscuit"), 1, EffectTargetRef.OnCritter(tortoise));
            Assert.That(preview, Is.EqualTo(5));

            engine.Clear();
            engine.Play(0, biscuit, EffectTargetRef.OnCritter(tortoise));

            Assert.That(engine.Critter(tortoise).Damage, Is.EqualTo(0));
            Assert.That(engine.EventsOf<HealedEvent>()[0].Amount, Is.EqualTo(5), "the beat says the same number the preview did");
        }

        [Test]
        public void AnUndamagedCritter_PreviewsNothing()
        {
            Scenario scenario = Fresh();
            scenario.Seat(0).Hand("WarmBiscuit");
            CardInstanceId tortoise = scenario.Seat(0).Board("WiseTortoise");
            MatchEngine    engine   = scenario.Build();

            int? preview = HealPreview.ForTarget(engine.Rules, Config.Global, Card("WarmBiscuit"), 1, EffectTargetRef.OnCritter(tortoise));
            Assert.That(preview, Is.EqualTo(0));
        }

        [Test]
        public void TheRankTracksAmountIsInThePreview()
        {
            // Warm Biscuit is on TrickAmount3: +2 to its one number by rank 3, which the engine applies to the
            // first step of a card's Hello. A preview that ignored the rank would under-promise on every copy
            // a player has actually grown.
            Scenario       scenario = Fresh();
            CardInstanceId biscuit  = scenario.Seat(0).Hand("WarmBiscuit", rank: 3);
            CardInstanceId tortoise = scenario.Seat(0).Board("WiseTortoise", maxHealth: 45, damage: 40);
            MatchEngine    engine   = scenario.Build();

            int? preview = HealPreview.ForTarget(engine.Rules, Config.Global, Card("WarmBiscuit"), 3, EffectTargetRef.OnCritter(tortoise));
            Assert.That(preview, Is.EqualTo(22), "twenty printed, two from the rank track at rank three");

            engine.Clear();
            engine.Play(0, biscuit, EffectTargetRef.OnCritter(tortoise));

            Assert.That(engine.EventsOf<HealedEvent>()[0].Amount, Is.EqualTo(22));
        }

        [Test]
        public void ACardThatHealsNoChosenTarget_HasNoPreview()
        {
            // The board draws a badge only where there is a number to draw: an attack target, a critter being
            // played, a burn trick's victim all answer null rather than zero.
            Assert.That(HealPreview.ChosenHealAmount(Card("EmberKit"), 1), Is.Null);
            Assert.That(HealPreview.ChosenHealAmount(Card("Foxfire"), 1), Is.Null, "a damage trick is not a heal");
            Assert.That(HealPreview.ChosenHealAmount(Card("WarmBiscuit"), 1), Is.EqualTo(20));
            Assert.That(HealPreview.ChosenHealAmount(Card("BerrySnack"), 1), Is.EqualTo(10));
        }

        [Test]
        public void AMissingTarget_HasNoPreview()
        {
            Scenario    scenario = Fresh();
            MatchEngine engine   = scenario.Build();

            Assert.That(HealPreview.ForTarget(engine.Rules, Config.Global, Card("WarmBiscuit"), 1,
                EffectTargetRef.OnCritter(new CardInstanceId(4242))), Is.Null);
        }
    }
}
