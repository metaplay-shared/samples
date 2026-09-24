using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The starter content, as it comes out of the checked-in archive: the catalogue is there, the effect
    /// grammars survived the config build as typed values, references resolve, and everything the archive
    /// carries round-trips the serializer.
    /// </summary>
    [TestFixture]
    public class GameConfigContentTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        #region The catalogue

        [Test]
        public void CritterGrowthIsProportionalAndPassiveCardsStayPassive()
        {
            foreach ((CardId id, CardInfo card) in Config.Cards)
            {
                if (card.Type != CardType.Critter || !card.Collectible || card.RankTrack.Ref.IsFlat)
                    continue;
                CardStats grown = card.GetStatsAtRank(Config.Global.RankMax);
                double target = (card.Attack + card.Health) * 0.16;
                int actual = grown.Attack + grown.Health - card.Attack - card.Health;
                Assert.That(System.Math.Abs(actual - target), Is.LessThanOrEqualTo(0.50001), id.Value);
                if (actual >= 4)
                    for (int rank = 2; rank <= 5; rank++)
                        Assert.That(card.RankTrack.Ref.GetStep(rank).IsEmpty, Is.False, $"{id} rank {rank}");
                if (card.Attack == 0)
                    Assert.That(grown.Attack, Is.Zero, id.Value);
            }
        }

        [Test]
        public void Catalogue_HasEveryClanAndKeyword()
        {
            Assert.That(Config.Clans.Count, Is.EqualTo(6), "five clans plus the Wanderers");
            Assert.That(Config.Clans.Values.Count(clan => clan.CountsTowardClanLimit), Is.EqualTo(5));
            Assert.That(Config.Clans[ClanId.FromString("Wanderer")].CountsTowardClanLimit, Is.False);

            Assert.That(Config.Keywords.Count, Is.EqualTo(7));
            Assert.That(Config.Keywords.Values.Count(keyword => keyword.Kind == KeywordKind.EngineFlag), Is.EqualTo(5));
        }

        [Test]
        public void Catalogue_HasTheStarterSlice()
        {
            Assert.That(Config.Cards.Count, Is.EqualTo(67));
            Assert.That(Config.Cards.Values.Count(card => card.Collectible), Is.EqualTo(65));
            Assert.That(Config.Cards.Values.Count(card => card.InStarterCollection), Is.EqualTo(45));
            Assert.That(Config.Weathers.Count, Is.EqualTo(6));
            Assert.That(Config.EffectSteps.Count, Is.EqualTo(25));
        }

        [Test]
        public void ExpansionPreservesTheStarterIdentitiesAndClanSplit()
        {
            string[] expected = new[]
            {
                "EmberKit", "Foxfire", "FlameDancer", "SizzleWhisker", "CinderStorm",
                "NineTailMatriarch", "PebbleCollector", "TideScholar", "Slipstream", "BubbleDrifter",
                "Undertow", "Riptide", "AcornHoard", "MossyYearling", "HoneyFeast",
                "BoulderBoar", "AcornForager", "OldMossback", "SunbeamRetriever", "WarmBiscuit",
                "PackCheer", "BiscuitHound", "SheepdogShepherd", "DawnShepherd", "MoonlitAlleycat",
                "SmokeBomb", "MoonlightSeance", "WhiskerThief", "DumpsterBandit", "RaccoonRingleader",
                "MeadowMouse", "GardenSnail", "BusyBeaver", "TrailRabbit", "RiverDuck",
                "PondFrog", "WiseTortoise", "PricklyHedgehog", "StrayGoat", "HillPony",
                "GreyOwl", "OldBadger", "MooseWanderer", "BerrySnack", "FieldNotes",
            };
            Assert.That(Config.Cards.Values.Where(card => card.InStarterCollection).Select(card => card.CardId.Value),
                Is.EquivalentTo(expected));
            Assert.That(Config.Cards.Values.Count(card => card.Collectible && !card.InStarterCollection), Is.EqualTo(20));
            foreach (ClanInfo clan in Config.Clans.Values)
                Assert.That(Config.Cards.Values.Count(card => card.Collectible && card.Clan.Ref.ClanId == clan.ClanId),
                    Is.EqualTo(clan.CountsTowardClanLimit ? 10 : 15), clan.ClanId.Value);
        }

        [TestCase("EmberEaredHare", "EmberKit", "Kitsune")]
        [TestCase("PaperLanternPrank", "Foxfire", "Kitsune")]
        [TestCase("BonfireBengal", "SizzleWhisker", "Kitsune")]
        [TestCase("TheHundredTailTale", "NineTailMatriarch", "Kitsune")]
        [TestCase("MapShellTurtle", "PebbleCollector", "Tidepool")]
        [TestCase("MoonpoolFrog", "BubbleDrifter", "Tidepool")]
        [TestCase("SealOfApproval", "Undertow", "Tidepool")]
        [TestCase("SpringTide", "Riptide", "Tidepool")]
        [TestCase("PocketShovelMole", "MossyYearling", "Mossback")]
        [TestCase("BuriedAcorn", "AcornHoard", "Mossback")]
        [TestCase("DeepdelverMole", "AcornForager", "Mossback")]
        [TestCase("HillRaiserMole", "OldMossback", "Mossback")]
        [TestCase("LongdogLookout", "SunbeamRetriever", "Sunny")]
        [TestCase("PicnicBasket", "BerrySnack", "Sunny")]
        [TestCase("RescueStBernard", "SheepdogShepherd", "Sunny")]
        [TestCase("HearthOfTheWholePack", "DawnShepherd", "Sunny")]
        [TestCase("KeyholeKitten", "MoonlitAlleycat", "Moonlight")]
        [TestCase("PossumEncore", "MoonlightSeance", "Moonlight")]
        [TestCase("VelvetRopeCat", "WhiskerThief", "Moonlight")]
        [TestCase("QueenOfBorrowedThings", "RaccoonRingleader", "Moonlight")]
        public void ExpansionCardCopiesItsCompleteGameplayTemplate(string id, string templateId, string clan)
        {
            CardInfo card = Config.Cards[CardId.FromString(id)];
            CardInfo template = Config.Cards[CardId.FromString(templateId)];
            Assert.That(card.InStarterCollection, Is.False);
            Assert.That(card.Collectible, Is.True);
            Assert.That(card.Clan.Ref.ClanId.Value, Is.EqualTo(clan));
            foreach (System.Reflection.PropertyInfo property in typeof(CardInfo).GetProperties())
            {
                if (new[] { "CardId", "ConfigKey", "DisplayName", "Clan", "InStarterCollection", "ArtEmoji", "Flavor" }.Contains(property.Name))
                    continue;
                Assert.That(property.GetValue(card), Is.EqualTo(property.GetValue(template)), $"{id}: {property.Name}");
            }
        }

        [TestCase("FireAndFoam", "EmberKit,Foxfire,FlameDancer,SizzleWhisker,CinderStorm,NineTailMatriarch,PebbleCollector,TideScholar,Slipstream,BubbleDrifter,Undertow,Riptide,BerrySnack,BusyBeaver,FieldNotes,GreyOwl,HillPony,MeadowMouse,MooseWanderer,OldBadger,PondFrog,PricklyHedgehog,StrayGoat,TrailRabbit,WiseTortoise")]
        [TestCase("SunlitThicket", "AcornHoard,MossyYearling,HoneyFeast,BoulderBoar,AcornForager,OldMossback,SunbeamRetriever,WarmBiscuit,PackCheer,BiscuitHound,SheepdogShepherd,DawnShepherd,BerrySnack,BusyBeaver,FieldNotes,GardenSnail,MeadowMouse,MooseWanderer,OldBadger,PondFrog,PricklyHedgehog,RiverDuck,StrayGoat,TrailRabbit,WiseTortoise")]
        [TestCase("AlleySparks", "MoonlitAlleycat,SmokeBomb,MoonlightSeance,WhiskerThief,DumpsterBandit,RaccoonRingleader,EmberKit,Foxfire,FlameDancer,SizzleWhisker,CinderStorm,NineTailMatriarch,BerrySnack,FieldNotes,GardenSnail,GreyOwl,HillPony,MooseWanderer,OldBadger,PondFrog,PricklyHedgehog,RiverDuck,StrayGoat,TrailRabbit,WiseTortoise")]
        [TestCase("PorchlightPack", "SunbeamRetriever,WarmBiscuit,PackCheer,BiscuitHound,SheepdogShepherd,DawnShepherd,MoonlitAlleycat,SmokeBomb,MoonlightSeance,WhiskerThief,DumpsterBandit,RaccoonRingleader,BerrySnack,BusyBeaver,FieldNotes,GardenSnail,GreyOwl,HillPony,OldBadger,PondFrog,PricklyHedgehog,RiverDuck,StrayGoat,TrailRabbit,WiseTortoise")]
        [TestCase("RiverbankPatience", "PebbleCollector,TideScholar,Slipstream,BubbleDrifter,Undertow,Riptide,AcornHoard,MossyYearling,HoneyFeast,BoulderBoar,AcornForager,OldMossback,BerrySnack,BusyBeaver,FieldNotes,GreyOwl,HillPony,MeadowMouse,MooseWanderer,PondFrog,PricklyHedgehog,RiverDuck,StrayGoat,TrailRabbit,WiseTortoise")]
        [TestCase("EmberAndOak", "EmberKit,Foxfire,FlameDancer,SizzleWhisker,CinderStorm,NineTailMatriarch,AcornHoard,MossyYearling,HoneyFeast,BoulderBoar,AcornForager,OldMossback,BerrySnack,BusyBeaver,FieldNotes,GardenSnail,GreyOwl,MooseWanderer,OldBadger,PondFrog,PricklyHedgehog,RiverDuck,StrayGoat,TrailRabbit,WiseTortoise")]
        public void PremadeDeckListsAreUnchanged(string id, string cards)
        {
            Assert.That(Config.StarterDecks[StarterDeckId.FromString(id)].ToCardIds().Select(card => card.Value),
                Is.EqualTo(cards.Split(',')));
        }

        [Test]
        public void Catalogue_HasTheStarterDecks()
        {
            Assert.That(Config.StarterDecks.Count, Is.EqualTo(6));

            // A config library enumerates in insertion order, which is sheet order, so pinning the sequence
            // rather than the set also pins that the sheet was not reordered under the picker.
            Assert.That(
                Config.StarterDecks.Values.Select(deck => deck.StarterDeckId.Value),
                Is.EqualTo(new[] { "FireAndFoam", "SunlitThicket", "AlleySparks", "PorchlightPack",
                                   "RiverbankPatience", "EmberAndOak" }));

            foreach (StarterDeckInfo deck in Config.StarterDecks.Values)
            {
                Assert.That(deck.Cards.Count, Is.EqualTo(Config.Global.DeckSize), deck.StarterDeckId.Value);
                Assert.That(DeckValidator.Validate(deck.ToCardIds(), Config).IsValid, Is.True, deck.StarterDeckId.Value);

                // The one authored invariant the six decks share: the same total cost, so a win-rate matrix
                // over them measures what the cards do rather than what they cost. Clan halves are not equal,
                // so each deck drops the two Wanderers that cancel its own clan pair's skew.
                Assert.That(deck.Cards.Sum(card => card.Ref.Cost), Is.EqualTo(79), deck.StarterDeckId.Value);
            }
        }

        [Test]
        public void Catalogue_ExercisesEveryPrimitiveAndTrigger()
        {
            // The starter slice exists to cover the vocabulary, so a missing verb here means the content no
            // longer proves the engine's whole surface is reachable from config.
            HashSet<EffectOp> ops = Config.EffectSteps.Values.Select(step => step.Op).ToHashSet();
            Assert.That(ops, Is.EquivalentTo(EnumUtil.GetValues<EffectOp>()));

            foreach (CardTrigger trigger in EffectVocabulary.AllCardTriggers)
                Assert.That(Config.Cards.Values.Any(card => card.BindsTrigger(trigger)), Is.True, $"no card binds {trigger}");

            HashSet<WeatherTrigger> weatherTriggers = Config.Weathers.Values.Select(weather => weather.Trigger).ToHashSet();
            Assert.That(weatherTriggers, Is.EquivalentTo(EnumUtil.GetValues<WeatherTrigger>()));
            Assert.That(Config.Weathers.Values.Any(weather => weather.HasAura), Is.True);
            Assert.That(Config.Weathers.Values.Any(weather => weather.HasCostRule), Is.True);
        }

        [Test]
        public void Global_CarriesTheDesignNumbers()
        {
            GlobalConfig global = Config.Global;

            // Health and hit points are counted in fifths of the old domain (F2), so the Den is 125 rather
            // than 25 while the deck is still 25 cards: the two 25s were never the same kind of number.
            Assert.That(global.DenStartingHp, Is.EqualTo(125));
            Assert.That(global.StatQuantum, Is.EqualTo(5));
            Assert.That(global.DeckSize, Is.EqualTo(25));
            Assert.That(global.MaxClansPerDeck, Is.EqualTo(2));
            Assert.That(global.MaxBoardCritters, Is.EqualTo(6));
            Assert.That(global.MaxHandSize, Is.EqualTo(9));
            Assert.That(global.OpeningHandFirstPlayer, Is.EqualTo(3));
            Assert.That(global.OpeningHandSecondPlayer, Is.EqualTo(4));
            // Max mana starts at zero and grows at each of a seat's own turn starts, so a seat has 1 on its
            // first turn.
            Assert.That(global.StartingMaxMana, Is.EqualTo(0));
            Assert.That(global.ManaGainPerTurn, Is.EqualTo(1));
            // The Tuckered Out schedule is a health quantity and scaled with the domain; the mana above is
            // not, which is the whole distinction F2 turns on.
            Assert.That(global.TuckeredOutFirstDamage, Is.EqualTo(5));
            Assert.That(global.TuckeredOutIncrement, Is.EqualTo(5));
            Assert.That(global.SecondPlayerBonusCard.Ref.CardId, Is.EqualTo(CardId.FromString("TheAcorn")));
            Assert.That(global.SecondPlayerBonusCard.Ref.Collectible, Is.False);

            // The queue's own numbers. The gap threshold and the shield are game-design.md's, untuned; the rating
            // seed and K-factor are the smallest thing that unblocks a band policy, and the gap is
            // in ranks, which the 5× stat domain deliberately left alone.
            Assert.That(global.PowerScoreGapThreshold, Is.EqualTo(15));
            Assert.That(global.NewcomerShieldMatches, Is.EqualTo(2));
            Assert.That(global.InitialRating, Is.EqualTo(1000));
            Assert.That(global.RatingKFactor, Is.EqualTo(32));
        }

        #endregion

        #region Parsed effect data

        [Test]
        public void EffectStep_ParsesALiteralAmount()
        {
            EffectStepInfo step = Config.EffectSteps[EffectStepId.FromString("DamageChosen3")];

            Assert.That(step.Op, Is.EqualTo(EffectOp.Damage));
            Assert.That(step.Target, Is.EqualTo(EffectTargetKind.Chosen));
            Assert.That(step.Amount.Literal, Is.EqualTo(15));
            Assert.That(step.Amount.IsLiteralOnly, Is.True);
            Assert.That(step.Amount.PerUnit, Is.EqualTo(0), "a plain number scales nothing per unit");
        }

        [Test]
        public void EffectStep_ParsesACountedAmountAndItsFilter()
        {
            EffectStepInfo step = Config.EffectSteps[EffectStepId.FromString("DenPerKitsune")];

            Assert.That(step.Amount.Literal, Is.EqualTo(0));
            Assert.That(step.Amount.Counter, Is.EqualTo(EffectCounter.PlayedThisMatch));
            // Each Kitsune played is worth a whole stat quantum, which is what the per-unit factor is for:
            // a bare count would be a rounding error against a Den measured in hundreds.
            Assert.That(step.Amount.PerUnit, Is.EqualTo(5));
            Assert.That(step.Filter, Is.Not.Null);
            Assert.That(step.Filter.Clan.Ref.ClanId, Is.EqualTo(ClanId.FromString("Kitsune")));
            Assert.That(step.Filter.Type, Is.EqualTo(CardTypeFilter.Any));
        }

        [Test]
        public void EffectStep_ParsesNegativeBuffAmounts()
        {
            EffectStepInfo step = Config.EffectSteps[EffectStepId.FromString("ShrinkChosen11")];

            Assert.That(step.Op, Is.EqualTo(EffectOp.Buff));
            Assert.That(step.Amount.Literal, Is.EqualTo(-5));
            Assert.That(step.Amount2.Literal, Is.EqualTo(-5));
        }

        [Test]
        public void EffectStep_ResolvesItsSummonedCard()
        {
            EffectStepInfo step = Config.EffectSteps[EffectStepId.FromString("SummonLambs2")];

            Assert.That(step.Amount.Literal, Is.EqualTo(2));
            Assert.That(step.Card.Ref.CardId, Is.EqualTo(CardId.FromString("LambToken")));
            Assert.That(step.Card.Ref.Type, Is.EqualTo(CardType.Critter));
            Assert.That(step.Card.Ref.Collectible, Is.False);
        }

        [Test]
        public void Card_BindsItsStepsInAuthoredOrder()
        {
            CardInfo undertow = Config.Cards[CardId.FromString("Undertow")];

            Assert.That(undertow.ChooseTarget, Is.EqualTo(ChooseTargetKind.AnyCritter));
            Assert.That(
                undertow.GetSteps(CardTrigger.Hello).Select(step => step.Ref.StepId.Value),
                Is.EqualTo(new[] { "BounceChosen", "DrawOne" }));
        }

        [Test]
        public void Card_FoldsItsPrintedKeywordsIntoEngineFlags()
        {
            Assert.That(Config.Cards[CardId.FromString("EmberKit")].GetKeywordFlags(), Is.EqualTo(KeywordFlags.Zoomies));
            Assert.That(Config.Cards[CardId.FromString("RaccoonRingleader")].GetKeywordFlags(), Is.EqualTo(KeywordFlags.Sneaky));
            Assert.That(Config.Cards[CardId.FromString("Foxfire")].GetKeywordFlags(), Is.EqualTo(KeywordFlags.None));
        }

        [Test]
        public void RankTrack_AccumulatesItsMilestones()
        {
            RankTrackInfo critterDefault = Config.RankTracks[RankTrackId.FromString("CritterDefault")];

            Assert.That(critterDefault.GetCumulativeDeltas(1).IsEmpty, Is.True);
            Assert.That(critterDefault.GetCumulativeDeltas(3).HealthDelta, Is.EqualTo(1));
            Assert.That(critterDefault.GetCumulativeDeltas(3).AttackDelta, Is.EqualTo(0));
            Assert.That(critterDefault.GetCumulativeDeltas(5).HealthDelta, Is.EqualTo(1));
            Assert.That(critterDefault.GetCumulativeDeltas(5).AttackDelta, Is.EqualTo(1));

            Assert.That(Config.RankTracks[RankTrackId.None].IsFlat, Is.True);
            Assert.That(Config.RankTracks[RankTrackId.FromString("TrickAmount3")].ScalesEffectAmount, Is.True);
        }

        [Test]
        public void CanonicalOrder_IsOrdinalOnCardIds()
        {
            Assert.That(CardInfo.CompareCanonical(CardId.FromString("AcornHoard"), CardId.FromString("BusyBeaver")), Is.LessThan(0));
            Assert.That(CardInfo.CompareCanonical(CardId.FromString("Riptide"), CardId.FromString("Riptide")), Is.Zero);
        }

        #endregion

        #region The built content validates

        [Test]
        public void BuiltContent_ProducesNoValidationErrors()
        {
            // The same checks the config build runs, over the archive that build produced. A shipped archive
            // that fails them means the gate in Tools/GameConfigGen stopped working.
            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(ContentPool.FromConfig(Config), sink);

            Assert.That(sink.Errors, Is.Empty, sink.Describe());
        }

        [Test]
        public void ExpandedContentCanBuildEveryMonoClanDeckWithoutWarnings()
        {
            // Ten cards per clan plus fifteen Wanderers now meet the 25-card deck size.
            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(ContentPool.FromConfig(Config), sink);

            Assert.That(sink.Warnings, Is.Empty, sink.Describe());
        }

        #endregion

        #region Serializer round trip

        [Test]
        public void Serializer_RoundTripsTheGlobalTunables()
        {
            GlobalConfig original = Config.Global;

            GlobalConfig copy = MetaSerialization.CloneTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: Config);

            Assert.That(copy.DenStartingHp, Is.EqualTo(original.DenStartingHp));
            Assert.That(copy.DeckSize, Is.EqualTo(original.DeckSize));
            Assert.That(copy.MaxHandSize, Is.EqualTo(original.MaxHandSize));
            Assert.That(copy.StartingMaxMana, Is.EqualTo(original.StartingMaxMana));
            Assert.That(copy.InitialLockSlots, Is.EqualTo(original.InitialLockSlots));
            // The config reference survives as a key and resolves against the config on the way back in.
            Assert.That(copy.SecondPlayerBonusCard.Ref.CardId, Is.EqualTo(CardId.FromString("TheAcorn")));
        }

        [Test]
        public void Serializer_RoundTripsAParsedAmountAndFilter()
        {
            EffectStepInfo step = Config.EffectSteps[EffectStepId.FromString("DenPerKitsune")];

            EffectAmount amount = MetaSerialization.CloneTagged(step.Amount, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: Config);
            EffectFilter filter = MetaSerialization.CloneTagged(step.Filter, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: Config);

            Assert.That(amount.Literal, Is.EqualTo(0));
            Assert.That(amount.Counter, Is.EqualTo(EffectCounter.PlayedThisMatch));
            Assert.That(filter.Clan.Ref.ClanId, Is.EqualTo(ClanId.FromString("Kitsune")));
            Assert.That(filter.Type, Is.EqualTo(CardTypeFilter.Any));
        }

        [Test]
        public void Serializer_RoundTripsARankTrackStep()
        {
            RankTrackStep original = Config.RankTracks[RankTrackId.FromString("TrickCheaper3")].Rank3;

            RankTrackStep copy = MetaSerialization.CloneTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: Config);

            Assert.That(copy.CostDelta, Is.EqualTo(-1));
            Assert.That(copy.AttackDelta, Is.Zero);
            Assert.That(copy.AmountDelta, Is.Zero);
        }

        [Test]
        public void Serializer_RoundTripsAModelGraphHoldingCardReferences()
        {
            // A deck list is the shape persisted on the player: config references inside a model.
            TestDeck original = new TestDeck(
                Config.Cards.Values
                    .Where(card => card.Collectible)
                    .OrderBy(card => card.CardId.Value, System.StringComparer.Ordinal)
                    .Take(Config.Global.DeckSize)
                    .Select(card => MetaRef<CardInfo>.FromItem(card))
                    .ToList());

            TestDeck copy = MetaSerialization.CloneTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: Config);

            Assert.That(copy.Cards.Count, Is.EqualTo(Config.Global.DeckSize));
            for (int ndx = 0; ndx < copy.Cards.Count; ndx++)
                Assert.That(copy.Cards[ndx].Ref.CardId, Is.EqualTo(original.Cards[ndx].Ref.CardId));
        }

        /// <summary> A stand-in for the persisted deck: a model holding config references. </summary>
        [MetaSerializable]
        public class TestDeck
        {
            [MetaMember(1)] public List<MetaRef<CardInfo>> Cards { get; private set; } = new List<MetaRef<CardInfo>>();

            public TestDeck() { }
            public TestDeck(List<MetaRef<CardInfo>> cards) { Cards = cards; }
        }

        #endregion
    }
}
