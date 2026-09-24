using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The negative controls for every content rule the config build enforces. A validator that rejects
    /// nothing passes everything, so each check here is given a deliberately broken pool and must complain
    /// about it — and must complain about the row that is actually wrong.
    /// <para>
    /// The fixtures are the shipped content with one thing changed. That keeps each test about one rule and
    /// keeps the healthy baseline honest: it is the content the game actually runs on.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ContentValidatorTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        #region Fixture helpers

        static MetaRef<ClanInfo>      Clan(string id)    => MetaRef<ClanInfo>.FromItem(Config.Clans[ClanId.FromString(id)]);
        static MetaRef<KeywordInfo>   Keyword(string id) => MetaRef<KeywordInfo>.FromItem(Config.Keywords[KeywordId.FromString(id)]);
        static MetaRef<RankTrackInfo> Track(string id)   => MetaRef<RankTrackInfo>.FromItem(Config.RankTracks[RankTrackId.FromString(id)]);
        static MetaRef<CardInfo>      Card(string id)    => MetaRef<CardInfo>.FromItem(Config.Cards[CardId.FromString(id)]);

        /// <summary> The shipped starter deck, as a fresh row a test may rebuild with one thing changed. </summary>
        static StarterDeckInfo StarterDeck(string id) => Config.StarterDecks[StarterDeckId.FromString(id)];

        /// <summary>
        /// A starter deck row built from card ids, for the negative controls. Ids not in the shipped catalogue
        /// are resolved against <paramref name="synthetic"/>, which is how a control reaches a rule the
        /// shipped pool cannot violate.
        /// </summary>
        static StarterDeckInfo StarterDeckOf(
            string id,
            string displayName,
            string description,
            string[] cardIds,
            IReadOnlyList<CardInfo> synthetic = null)
        {
            List<MetaRef<CardInfo>> cards = new List<MetaRef<CardInfo>>(cardIds.Length);
            foreach (string cardId in cardIds)
            {
                if (cardId == null)
                    cards.Add(null);
                else if (Config.Cards.ContainsKey(CardId.FromString(cardId)))
                    cards.Add(Card(cardId));
                else
                    cards.Add(MetaRef<CardInfo>.FromItem(synthetic.First(card => card.CardId.Value == cardId)));
            }

            return new StarterDeckInfo(StarterDeckId.FromString(id), displayName, description, cards);
        }

        /// <summary> The six shipped starter decks with one of them replaced by the row given. </summary>
        static IReadOnlyList<StarterDeckInfo> StarterDecksWith(StarterDeckInfo replacement)
        {
            return ContentPool.FromConfig(Config).StarterDecks
                .Select(deck => deck.StarterDeckId == replacement.StarterDeckId ? replacement : deck)
                .ToList();
        }

        static List<MetaRef<EffectStepInfo>> Steps(params EffectStepInfo[] steps)
            => steps.Select(step => MetaRef<EffectStepInfo>.FromItem(step)).ToList();

        static List<MetaRef<EffectStepInfo>> NamedSteps(params string[] stepIds)
            => stepIds.Select(id => MetaRef<EffectStepInfo>.FromItem(Config.EffectSteps[EffectStepId.FromString(id)])).ToList();

        /// <summary> The shipped pool, optionally with one list replaced. </summary>
        static ContentPool Pool(
            IReadOnlyList<CardInfo> cards = null,
            IReadOnlyList<EffectStepInfo> effectSteps = null,
            IReadOnlyList<WeatherInfo> weathers = null,
            IReadOnlyList<KeywordInfo> keywords = null,
            IReadOnlyList<RankTrackInfo> rankTracks = null,
            IReadOnlyList<ClanInfo> clans = null,
            IReadOnlyList<StarterDeckInfo> starterDecks = null,
            GlobalConfig global = null)
        {
            ContentPool shipped = ContentPool.FromConfig(Config);
            return new ContentPool(
                clans ?? shipped.Clans,
                keywords ?? shipped.Keywords,
                rankTracks ?? shipped.RankTracks,
                effectSteps ?? shipped.EffectSteps,
                cards ?? shipped.Cards,
                weathers ?? shipped.Weathers,
                starterDecks ?? shipped.StarterDecks,
                global ?? shipped.Global);
        }

        static IReadOnlyList<T> Plus<T>(IReadOnlyList<T> items, T extra) => items.Concat(new[] { extra }).ToList();

        /// <summary> The shipped globals as a fresh instance the test may edit. </summary>
        static GlobalConfig EditableGlobal()
        {
            GlobalConfig shipped = Config.Global;
            return new GlobalConfig
            {
                DenStartingHp           = shipped.DenStartingHp,
                DeckSize                = shipped.DeckSize,
                MaxClansPerDeck         = shipped.MaxClansPerDeck,
                MaxBoardCritters        = shipped.MaxBoardCritters,
                MaxHandSize             = shipped.MaxHandSize,
                OpeningHandFirstPlayer  = shipped.OpeningHandFirstPlayer,
                OpeningHandSecondPlayer = shipped.OpeningHandSecondPlayer,
                SecondPlayerBonusCard   = shipped.SecondPlayerBonusCard,
                StartingMaxMana         = shipped.StartingMaxMana,
                ManaGainPerTurn         = shipped.ManaGainPerTurn,
                DrawsPerTurn            = shipped.DrawsPerTurn,
                MulligansPerPlayer      = shipped.MulligansPerPlayer,
                TuckeredOutFirstDamage  = shipped.TuckeredOutFirstDamage,
                TuckeredOutIncrement    = shipped.TuckeredOutIncrement,
                RankMin                 = shipped.RankMin,
                RankMax                 = shipped.RankMax,
                InitialLockSlots        = shipped.InitialLockSlots,
                MinLockRank             = shipped.MinLockRank,
                StatQuantum             = shipped.StatQuantum,
                PowerScoreGapThreshold  = shipped.PowerScoreGapThreshold,
                NewcomerShieldMatches   = shipped.NewcomerShieldMatches,
                InitialRating           = shipped.InitialRating,
                RatingKFactor           = shipped.RatingKFactor,
            };
        }

        /// <summary> Runs the validator and asserts exactly which row it complained about. </summary>
        static void AssertRejects(ContentPool pool, string expectedFragment)
        {
            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(pool, sink);

            Assert.That(sink.Errors, Is.Not.Empty, "the validator accepted a pool it must reject");
            Assert.That(sink.Errors.Any(error => error.Contains(expectedFragment)), Is.True,
                $"no error mentioned '{expectedFragment}'. Reported:\n{sink.Describe()}");
        }

        /// <summary> Runs the validator and asserts it warned about the row without failing the build. </summary>
        static void AssertWarns(ContentPool pool, string expectedFragment)
        {
            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(pool, sink);

            Assert.That(sink.Errors, Is.Empty, sink.Describe());
            Assert.That(sink.Warnings.Any(warning => warning.Contains(expectedFragment)), Is.True,
                $"no warning mentioned '{expectedFragment}'. Reported:\n{sink.Describe()}");
        }

        #endregion

        [Test]
        public void ShippedContent_IsAccepted()
        {
            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(ContentPool.FromConfig(Config), sink);

            Assert.That(sink.Errors, Is.Empty, sink.Describe());
        }

        #region Vocabulary

        [Test]
        public void Rejects_EngineFlagKeywordTheEngineDoesNotImplement()
        {
            KeywordInfo bogus = new KeywordInfo(KeywordId.FromString("Bouncy"), KeywordKind.EngineFlag, "Bouncy", "Not a thing.");

            AssertRejects(Pool(keywords: Plus(ContentPool.FromConfig(Config).Keywords, bogus)), "Keywords/Bouncy");
        }

        [Test]
        public void Rejects_TwoKeywordRowsNamingOneEngineFlag()
        {
            // A second row whose id spells the same flag would give the engine two identities for one rule.
            List<KeywordInfo> keywords = ContentPool.FromConfig(Config).Keywords.ToList();
            keywords.Add(new KeywordInfo(KeywordId.FromString("Guard"), KeywordKind.EngineFlag, "Bodyguard", "A duplicate."));

            AssertRejects(Pool(keywords: keywords), "same engine flag");
        }

        [Test]
        public void Rejects_TriggerLabelThatNamesNoTrigger()
        {
            KeywordInfo bogus = new KeywordInfo(KeywordId.FromString("Farewell"), KeywordKind.TriggerLabel, "Farewell:", "Not a trigger.");

            AssertRejects(Pool(keywords: Plus(ContentPool.FromConfig(Config).Keywords, bogus)), "Keywords/Farewell");
        }

        #endregion

        #region Step shape

        static EffectStepInfo Step(
            string id,
            EffectOp op,
            EffectTargetKind target = EffectTargetKind.None,
            EffectAmount amount = null,
            EffectAmount amount2 = null,
            ManaDuration duration = ManaDuration.None,
            MetaRef<KeywordInfo> keyword = null,
            MetaRef<CardInfo> card = null,
            EffectFilter filter = null,
            GraveyardSelector selector = GraveyardSelector.None)
            => new EffectStepInfo(EffectStepId.FromString(id), op, target, amount, amount2, duration, keyword, card, filter, selector, notes: null);

        /// <summary> A pool holding one extra step, referenced by one extra card so it is not merely dead. </summary>
        static ContentPool PoolWithStep(EffectStepInfo step, ChooseTargetKind chooseTarget = ChooseTargetKind.None, CardType cardType = CardType.Critter)
        {
            ContentPool shipped = ContentPool.FromConfig(Config);
            CardInfo carrier = new CardInfo(
                CardId.FromString("TestCarrier"), "Test Carrier", Clan("Wanderer"), cardType, CardRarity.Common, cost: 1,
                rankTrack: Track("None"), attack: cardType == CardType.Critter ? 1 : 0, health: cardType == CardType.Critter ? 1 : 0,
                chooseTarget: chooseTarget, hello: Steps(step), collectible: false, inStarterCollection: false);

            return Pool(cards: Plus(shipped.Cards, carrier), effectSteps: Plus(shipped.EffectSteps, step));
        }

        [Test]
        public void Rejects_DamageStepWithNoAmount()
        {
            AssertRejects(PoolWithStep(Step("NoAmount", EffectOp.Damage, EffectTargetKind.EnemyDen)), "EffectSteps/NoAmount");
        }

        [Test]
        public void Rejects_NegativeAmountOnAnythingButBuff()
        {
            EffectStepInfo step = Step("NegativeHeal", EffectOp.Heal, EffectTargetKind.OwnDen, new EffectAmount(-2));

            AssertRejects(PoolWithStep(step), "must not be negative");
        }

        [Test]
        public void Rejects_SeatScopedStepWithATarget()
        {
            EffectStepInfo step = Step("TargetedDraw", EffectOp.Draw, EffectTargetKind.OwnDen, new EffectAmount(1));

            AssertRejects(PoolWithStep(step), "must have no Target");
        }

        [Test]
        public void Rejects_GainManaWithNoDuration()
        {
            EffectStepInfo step = Step("ManaNoDuration", EffectOp.GainMana, amount: new EffectAmount(1));

            AssertRejects(PoolWithStep(step), "requires a Duration");
        }

        [Test]
        public void Rejects_PeekThatKeepsMoreThanItLooksAt()
        {
            EffectStepInfo step = Step("GreedyPeek", EffectOp.Peek, amount: new EffectAmount(2), amount2: new EffectAmount(3));

            AssertRejects(PoolWithStep(step), "keeps 3 of the 2 cards");
        }

        [Test]
        public void Rejects_SummonOfATrick()
        {
            EffectStepInfo step = Step("SummonATrick", EffectOp.Summon, amount: new EffectAmount(1), card: Card("Foxfire"));

            AssertRejects(PoolWithStep(step), "Only critters can be summoned");
        }

        [Test]
        public void Rejects_GrantOfATriggerLabel()
        {
            EffectStepInfo step = Step("GrantHello", EffectOp.GrantKeyword, EffectTargetKind.Self, keyword: Keyword("Hello"));

            AssertRejects(PoolWithStep(step), "rather than an engine flag");
        }

        [Test]
        public void Rejects_CopyFromGraveyardPointedAtTheBoard()
        {
            EffectStepInfo step = Step("CopyFromBoard", EffectOp.CopyFromGraveyard, EffectTargetKind.AllEnemy, selector: GraveyardSelector.Cheapest);

            AssertRejects(PoolWithStep(step), "OwnGraveyard or EnemyGraveyard");
        }

        [Test]
        public void Rejects_CopyFromGraveyardWithNoSelector()
        {
            EffectStepInfo step = Step("CopyAnything", EffectOp.CopyFromGraveyard, EffectTargetKind.EnemyGraveyard);

            AssertRejects(PoolWithStep(step), "requires a Selector");
        }

        [Test]
        public void Rejects_BounceAimedAtADen()
        {
            EffectStepInfo step = Step("BounceADen", EffectOp.Bounce, EffectTargetKind.EnemyDen);

            AssertRejects(PoolWithStep(step), "needs a critter target");
        }

        #endregion

        #region Step usage

        [Test]
        public void Rejects_CritterOnlyStepFedAChoiceThatMayBeADen()
        {
            // AnyTarget admits a Den, and a Den cannot be bounced. The card, not the step, is at fault.
            EffectStepInfo bounce = Config.EffectSteps[EffectStepId.FromString("BounceChosen")];
            CardInfo card = new CardInfo(
                CardId.FromString("BadBounce"), "Bad Bounce", Clan("Tidepool"), CardType.Trick, CardRarity.Common, cost: 2,
                rankTrack: Track("None"), chooseTarget: ChooseTargetKind.AnyTarget, hello: Steps(bounce),
                collectible: false, inStarterCollection: false);

            AssertRejects(Pool(cards: Plus(ContentPool.FromConfig(Config).Cards, card)), "Cards/BadBounce");
        }

        [Test]
        public void Rejects_SelfTargetOnATrick()
        {
            EffectStepInfo grow = Config.EffectSteps[EffectStepId.FromString("GrowSelf1")];
            CardInfo card = new CardInfo(
                CardId.FromString("SelfishTrick"), "Selfish Trick", Clan("Mossback"), CardType.Trick, CardRarity.Common, cost: 2,
                rankTrack: Track("None"), hello: Steps(grow), collectible: false, inStarterCollection: false);

            AssertRejects(Pool(cards: Plus(ContentPool.FromConfig(Config).Cards, card)), "targets Self");
        }

        [Test]
        public void Rejects_WeatherStepThatWantsAChosenTarget()
        {
            WeatherInfo weather = new WeatherInfo(
                WeatherId.FromString("PickyWeather"), "Picky Weather",
                trigger: WeatherTrigger.TurnStart, steps: NamedSteps("DamageChosen3"));

            AssertRejects(Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers, weather)), "nobody to choose");
        }

        [Test]
        public void Rejects_WeatherSelfTargetOutsideADeathHook()
        {
            WeatherInfo weather = new WeatherInfo(
                WeatherId.FromString("SelfishWeather"), "Selfish Weather",
                trigger: WeatherTrigger.TurnStart, steps: NamedSteps("GrowSelf1"));

            AssertRejects(Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers, weather)), "only a CritterDies hook");
        }

        #endregion

        #region Card shape

        static CardInfo BadCard(
            string id,
            CardType type = CardType.Critter,
            int cost = 2,
            int attack = 5,
            int health = 5,
            bool isSnack = false,
            List<MetaRef<KeywordInfo>> keywords = null,
            ChooseTargetKind chooseTarget = ChooseTargetKind.None,
            List<MetaRef<EffectStepInfo>> hello = null,
            List<MetaRef<EffectStepInfo>> goodbye = null,
            string track = "None",
            bool collectible = false,
            bool inStarter = false)
            => new CardInfo(
                CardId.FromString(id), id, Clan("Wanderer"), type, CardRarity.Common, cost, Track(track),
                attack: attack, health: health, isSnack: isSnack, keywords: keywords, chooseTarget: chooseTarget,
                hello: hello, goodbye: goodbye, collectible: collectible, inStarterCollection: inStarter);

        static ContentPool PoolWithCard(CardInfo card) => Pool(cards: Plus(ContentPool.FromConfig(Config).Cards, card));

        [Test]
        public void Rejects_CritterThatEntersPlayDead()
        {
            AssertRejects(PoolWithCard(BadCard("DeadOnArrival", health: 0)), "Cards/DeadOnArrival");
        }

        #region The stat domain

        // The stat domain is bounded at both ends, and the two ends fail differently: a number the match
        // cannot contain is an error, and a number that looks like it was authored before the domain was
        // widened is a warning, because a deliberately tiny one is legal.

        [Test]
        public void Rejects_CritterTougherThanTheEnemyDen()
        {
            CardInfo card = BadCard("Unkillable", health: Config.Global.DenStartingHp + 1);

            AssertRejects(PoolWithCard(card), "above the Den's");
        }

        [Test]
        public void Rejects_CritterHittingHarderThanADenHasHitPoints()
        {
            CardInfo card = BadCard("OneShot", attack: Config.Global.DenStartingHp + 1);

            AssertRejects(PoolWithCard(card), "above the Den's");
        }

        [Test]
        public void Accepts_ACritterExactlyAsToughAsTheDen()
        {
            // The boundary is inclusive on the legal side: a body worth exactly a Den is extreme content,
            // not broken content, and the error above must not fire one point early.
            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(PoolWithCard(BadCard("Exactly", health: Config.Global.DenStartingHp)), sink);

            Assert.That(sink.Errors, Is.Empty, sink.Describe());
        }

        [Test]
        public void Warns_CritterAuthoredInTheOldStatDomain()
        {
            // A 2/3 was a real card before F2 multiplied the domain by five. It still parses, still plays,
            // and is almost certainly a row somebody forgot to scale — so it is reported without failing
            // the build.
            CardInfo card = BadCard("Unscaled", attack: 2, health: 3);

            AssertWarns(PoolWithCard(card), "below the stat quantum");
        }

        [Test]
        public void Rejects_ABlowLargerThanTheWholeDen()
        {
            EffectStepInfo step = Step("Annihilate", EffectOp.Damage, EffectTargetKind.EnemyDen,
                amount: new EffectAmount(Config.Global.DenStartingHp + 1));

            AssertRejects(PoolWithStep(step), "the Den's whole");
        }

        [Test]
        public void Warns_AHealAuthoredInTheOldStatDomain()
        {
            EffectStepInfo step = Step("TinyHeal", EffectOp.Heal, EffectTargetKind.OwnDen,
                amount: new EffectAmount(2));

            AssertWarns(PoolWithStep(step), "below the stat quantum");
        }

        [Test]
        public void Warns_ACounterWorthLessThanTheQuantumPerUnit()
        {
            // The per-unit factor is the number a scaling card actually adds per unit, so it is held to the
            // same domain as a literal: a bare count against a Den in the hundreds does nothing visible.
            EffectStepInfo step = Step("BareCount", EffectOp.Damage, EffectTargetKind.EnemyDen,
                amount: new EffectAmount(0, EffectCounter.FriendlyCritters));

            AssertWarns(PoolWithStep(step), "below the stat quantum");
        }

        [Test]
        public void Warns_ABuffAuthoredInTheOldStatDomain()
        {
            // A +1/+1 growth was a real card before the domain was widened. Buff is where the risk actually
            // lives, because its numbers look like the rank-track deltas that are *meant* to be sub-quantum.
            EffectStepInfo step = Step("TinyGrowth", EffectOp.Buff, EffectTargetKind.Self,
                amount: new EffectAmount(1), amount2: new EffectAmount(1));

            AssertWarns(PoolWithStep(step), "below the stat quantum");
        }

        [Test]
        public void Warns_ANegativeBuffAuthoredInTheOldStatDomain()
        {
            // The bounds are on the magnitude, so a shrink that was never scaled is caught too — the sign
            // says which way the stat moves, not what domain it is written in.
            EffectStepInfo step = Step("TinyShrink", EffectOp.Buff, EffectTargetKind.Chosen,
                amount: new EffectAmount(-2), amount2: new EffectAmount(-2));

            AssertWarns(PoolWithStep(step, chooseTarget: ChooseTargetKind.EnemyCritter), "below the stat quantum");
        }

        [Test]
        public void Warns_ASecondAmountAuthoredInTheOldStatDomain()
        {
            // Amount2 is a stat in its own right and was unchecked for every op: a Buff scaled in one column
            // and forgotten in the other is the likeliest way to half-scale a card.
            EffectStepInfo step = Step("HalfScaled", EffectOp.Buff, EffectTargetKind.Self,
                amount: new EffectAmount(10), amount2: new EffectAmount(2));

            AssertWarns(PoolWithStep(step), "Amount2");
        }

        [Test]
        public void Rejects_ABuffLargerThanTheWholeDen()
        {
            EffectStepInfo step = Step("Colossal", EffectOp.Buff, EffectTargetKind.Self,
                amount: new EffectAmount(Config.Global.DenStartingHp + 1), amount2: new EffectAmount(5));

            AssertRejects(PoolWithStep(step), "past the Den's whole");
        }

        [Test]
        public void Rejects_ASecondAmountLargerThanTheWholeDen()
        {
            EffectStepInfo step = Step("ColossalHealth", EffectOp.Buff, EffectTargetKind.Self,
                amount: new EffectAmount(5), amount2: new EffectAmount(Config.Global.DenStartingHp + 1));

            AssertRejects(PoolWithStep(step), "Amount2");
        }

        [Test]
        public void Rejects_AHandLimitThatDoesNotCoverTheCompensationCard()
        {
            // The grant does not go through the full-hand rule — a seat compensated into its own deck has not
            // been compensated — so the room for it is a content invariant and this is where it is kept.
            GlobalConfig global = EditableGlobal();
            global.OpeningHandSecondPlayer = global.MaxHandSize;

            AssertRejects(Pool(global: global), "compensation card does not fit the hand limit");
        }

        [Test]
        public void Accepts_AHandLimitWithExactlyRoomForTheCompensationCard()
        {
            // The boundary is inclusive on the legal side: a hand of exactly the limit once the card is in is
            // legal, and the error above must not fire one card early.
            GlobalConfig global = EditableGlobal();
            global.OpeningHandSecondPlayer = global.MaxHandSize - 1;

            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(Pool(global: global), sink);

            Assert.That(sink.Errors, Is.Empty, sink.Describe());
        }

        [Test]
        public void Rejects_NonPositiveStatQuantum()
        {
            GlobalConfig global = EditableGlobal();
            global.StatQuantum = 0;

            AssertRejects(Pool(global: global), "StatQuantum");
        }

        [Test]
        public void Rejects_NonPositivePowerScoreGapThreshold()
        {
            GlobalConfig global = EditableGlobal();
            global.PowerScoreGapThreshold = 0;

            AssertRejects(Pool(global: global), "PowerScoreGapThreshold");
        }

        [Test]
        public void Rejects_APowerScoreGapThresholdNoTwoDecksCouldStraddle()
        {
            // The other way a threshold lies: every match would be Even and the asymmetric tiers would be
            // content nothing can reach, which is the same defect MinLockRank outside the rank range is.
            GlobalConfig global = EditableGlobal();
            global.PowerScoreGapThreshold = global.DeckSize * (global.RankMax - global.RankMin);

            AssertRejects(Pool(global: global), "PowerScoreGapThreshold");
        }

        [Test]
        public void Rejects_ANegativeNewcomerShield()
        {
            GlobalConfig global = EditableGlobal();
            global.NewcomerShieldMatches = -1;

            AssertRejects(Pool(global: global), "NewcomerShieldMatches");
        }

        [Test]
        public void Accepts_ANewcomerShieldOfZero()
        {
            // Zero is a supported configuration rather than a degenerate one: it means the shield is off, and
            // the tier is simply never chosen. The positivity check must not fire on it.
            GlobalConfig global = EditableGlobal();
            global.NewcomerShieldMatches = 0;

            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(Pool(global: global), sink);

            Assert.That(sink.Errors, Is.Empty, sink.Describe());
        }

        [Test]
        public void Rejects_ANonPositiveInitialRating()
        {
            GlobalConfig global = EditableGlobal();
            global.InitialRating = 0;

            AssertRejects(Pool(global: global), "InitialRating");
        }

        [Test]
        public void Rejects_ANonPositiveRatingKFactor()
        {
            // A K of zero is a rating that never moves, which makes every band a permanent one.
            GlobalConfig global = EditableGlobal();
            global.RatingKFactor = 0;

            AssertRejects(Pool(global: global), "RatingKFactor");
        }

        #endregion

        [Test]
        public void Rejects_TrickWithCombatStats()
        {
            AssertRejects(PoolWithCard(BadCard("BeefyTrick", CardType.Trick, attack: 2, health: 2)), "only critters have Attack and Health");
        }

        [Test]
        public void Rejects_TrickThatBindsMoreThanHello()
        {
            CardInfo card = BadCard("LingeringTrick", CardType.Trick, attack: 0, health: 0, goodbye: NamedSteps("BurnDen1"));

            AssertRejects(PoolWithCard(card), "may bind Hello only");
        }

        [Test]
        public void Rejects_CritterMarkedAsASnack()
        {
            AssertRejects(PoolWithCard(BadCard("SnackCritter", isSnack: true)), "a critter cannot be a Snack");
        }

        [Test]
        public void Rejects_SnackThatPointsAtTheEnemy()
        {
            CardInfo card = BadCard("HostileSnack", CardType.Trick, attack: 0, health: 0, isSnack: true,
                chooseTarget: ChooseTargetKind.EnemyCritter, hello: NamedSteps("ShrinkChosen11"));

            AssertRejects(PoolWithCard(card), "they point at your own side");
        }

        [Test]
        public void Rejects_TrickThatPrintsKeywords()
        {
            CardInfo card = BadCard("KeywordedTrick", CardType.Trick, attack: 0, health: 0,
                keywords: new List<MetaRef<KeywordInfo>> { Keyword("Guard") });

            AssertRejects(PoolWithCard(card), "only critters carry them");
        }

        [Test]
        public void Rejects_ChosenStepOnACardThatAsksForNoTarget()
        {
            CardInfo card = BadCard("SilentChoice", hello: NamedSteps("DamageChosen3"));

            AssertRejects(PoolWithCard(card), "the player is never asked to pick one");
        }

        [Test]
        public void Rejects_ChooseTargetNoStepUses()
        {
            CardInfo card = BadCard("PointlessChoice", chooseTarget: ChooseTargetKind.AnyCritter, hello: NamedSteps("DrawOne"));

            AssertRejects(PoolWithCard(card), "the pick would do nothing");
        }

        [Test]
        public void Rejects_CardThatPrintsATriggerLabelAsAKeyword()
        {
            CardInfo card = BadCard("SelfDescribing", keywords: new List<MetaRef<KeywordInfo>> { Keyword("Goodbye") });

            AssertRejects(PoolWithCard(card), "derives those labels");
        }

        #endregion

        #region Rank tracks

        [TestCase(2)]
        [TestCase(4)]
        public void Rejects_InvalidIntermediateRankEvenWhenLaterRankRepairsIt(int rank)
        {
            RankTrackStep invalid = new RankTrackStep(-6, 0, 0, 0);
            RankTrackStep repair = new RankTrackStep(6, 0, 0, 0);
            RankTrackInfo track = new RankTrackInfo(RankTrackId.FromString("Intermediate"),
                rank2: rank == 2 ? invalid : null,
                rank3: rank == 2 ? repair : null,
                rank4: rank == 4 ? invalid : null,
                rank5: rank == 4 ? repair : null);
            CardInfo card = new CardInfo(CardId.FromString("IntermediateCard"), "Intermediate Card",
                Clan("Wanderer"), CardType.Critter, CardRarity.Common, 2,
                MetaRef<RankTrackInfo>.FromItem(track), attack: 5, health: 5);
            AssertRejects(Pool(cards: Plus(ContentPool.FromConfig(Config).Cards, card),
                rankTracks: Plus(ContentPool.FromConfig(Config).RankTracks, track)), $"at rank {rank}");
        }

        [Test]
        public void Rejects_TrackThatTakesACardBelowZeroCost()
        {
            // TrickCheaper3 takes a point off the cost at rank 3, which a zero-cost card cannot pay.
            CardInfo card = BadCard("FreeAndCheaper", CardType.Trick, cost: 0, attack: 0, health: 0,
                hello: NamedSteps("DrawOne"), track: "TrickCheaper3");

            AssertRejects(PoolWithCard(card), "RankTracks/TrickCheaper3");
        }

        [Test]
        public void Rejects_AmountScalingTrackOnACardWithNoAmountToScale()
        {
            CardInfo card = BadCard("NothingToScale", CardType.Trick, attack: 0, health: 0,
                hello: NamedSteps("BounceAll"), track: "TrickAmount3");

            AssertRejects(PoolWithCard(card), "no literal amount to scale");
        }

        #endregion

        #region Weathers

        [Test]
        public void Rejects_CostRuleThatChangesNothing()
        {
            WeatherInfo weather = new WeatherInfo(WeatherId.FromString("FlatRain"), "Flat Rain", costScope: WeatherCostScope.AllTricks, costDelta: 0);

            AssertRejects(Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers, weather)), "the rule changes nothing");
        }

        [Test]
        public void Rejects_TriggerWithNoSteps()
        {
            WeatherInfo weather = new WeatherInfo(WeatherId.FromString("SilentDawn"), "Silent Dawn", trigger: WeatherTrigger.TurnStart);

            AssertRejects(Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers, weather)), "binds no steps");
        }

        [Test]
        public void Rejects_WeatherWithNoHooksAtAll()
        {
            WeatherInfo weather = new WeatherInfo(WeatherId.FromString("PlainDay"), "Plain Day");

            AssertRejects(Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers, weather)), "no hooks at all");
        }

        [Test]
        public void Rejects_EmptyWeatherPool()
        {
            AssertRejects(Pool(weathers: new List<WeatherInfo>()), "Weather pool is empty");
        }

        #endregion

        #region The pool and the globals

        [Test]
        public void Rejects_PoolTooSmallToBuildALegalDeck()
        {
            List<CardInfo> justAFew = ContentPool.FromConfig(Config).Cards.Take(4).ToList();

            AssertRejects(Pool(cards: justAFew), "No allowed clan combination");
        }

        [Test]
        public void Rejects_NonCollectibleCardInTheStarterCollection()
        {
            CardInfo card = BadCard("StarterToken", collectible: false, inStarter: true);

            AssertRejects(PoolWithCard(card), "never live in a collection");
        }

        [Test]
        public void Rejects_NonCollectibleCardOnARealRankTrack()
        {
            CardInfo card = BadCard("RankedToken", track: "CritterDefault", collectible: false);

            AssertRejects(PoolWithCard(card), "no rank to grow");
        }

        [Test]
        public void Rejects_StarterCollectionThatCannotBuildADeck()
        {
            // Every card is still collectible, so the pool is fine; only the starter grant is too thin.
            List<CardInfo> cards = ContentPool.FromConfig(Config).Cards
                .Select(card => new CardInfo(
                    card.CardId, card.DisplayName, card.Clan, card.Type, card.Rarity, card.Cost, card.RankTrack,
                    attack: card.Attack, health: card.Health, isSnack: card.IsSnack, keywords: card.Keywords,
                    chooseTarget: card.ChooseTarget, hello: card.Hello, goodbye: card.Goodbye, onAttack: card.OnAttack,
                    turnStart: card.TurnStart, turnEnd: card.TurnEnd, collectible: card.Collectible,
                    inStarterCollection: false))
                .ToList();

            AssertRejects(Pool(cards: cards), "the starter collection");
        }

        [Test]
        public void Rejects_OpeningHandThatDoesNotFitTheHandLimit()
        {
            GlobalConfig global = EditableGlobal();
            global.OpeningHandFirstPlayer = global.MaxHandSize + 1;

            AssertRejects(Pool(global: global), "does not fit the hand limit");
        }

        [Test]
        public void Rejects_DeckThatDoesNotCoverTheOpeningHand()
        {
            GlobalConfig global = EditableGlobal();
            global.DeckSize = 3;

            AssertRejects(Pool(global: global), "does not cover an opening hand");
        }

        [Test]
        public void Rejects_RankRangeThatRunsBackwards()
        {
            GlobalConfig global = EditableGlobal();
            global.RankMax = 0;

            AssertRejects(Pool(global: global), "below RankMin");
        }

        [Test]
        public void Rejects_LockThresholdAboveTheRankCeiling()
        {
            // A threshold outside the rank range is a knob that lies: above the ceiling no card in the game
            // could ever be locked, and below the floor the rule protects nothing.
            GlobalConfig global = EditableGlobal();
            global.MinLockRank = global.RankMax + 1;

            AssertRejects(Pool(global: global), "outside the rank range");
        }

        [Test]
        public void Rejects_LockThresholdBelowTheRankFloor()
        {
            GlobalConfig global = EditableGlobal();
            global.MinLockRank = global.RankMin - 1;

            AssertRejects(Pool(global: global), "outside the rank range");
        }

        [Test]
        public void Rejects_CollectibleSecondPlayerBonusCard()
        {
            GlobalConfig global = EditableGlobal();
            global.SecondPlayerBonusCard = Card("Foxfire");

            AssertRejects(Pool(global: global), "must not be buildable into a deck");
        }

        [Test]
        public void Rejects_SecondPlayerBonusCardThatIsNotATrick()
        {
            GlobalConfig global = EditableGlobal();
            global.SecondPlayerBonusCard = Card("LambToken");

            AssertRejects(Pool(global: global), "must be a trick");
        }

        #endregion

        #region Dead rows

        [Test]
        public void Warns_AboutAStepNothingUses()
        {
            EffectStepInfo orphan = Step("OrphanDraw", EffectOp.Draw, amount: new EffectAmount(1));

            AssertWarns(Pool(effectSteps: Plus(ContentPool.FromConfig(Config).EffectSteps, orphan)), "EffectSteps/OrphanDraw");
        }

        [Test]
        public void Warns_AboutARankTrackNothingUses()
        {
            RankTrackInfo orphan = new RankTrackInfo(RankTrackId.FromString("OrphanTrack"), rank3: new RankTrackStep(1, 0, 0, 0));

            AssertWarns(Pool(rankTracks: Plus(ContentPool.FromConfig(Config).RankTracks, orphan)), "RankTracks/OrphanTrack");
        }

        [Test]
        public void Warns_AboutTwoCardsWithTheSameName()
        {
            CardInfo twin = new CardInfo(
                CardId.FromString("SecondFoxfire"), "Foxfire", Clan("Kitsune"), CardType.Trick, CardRarity.Common, cost: 2,
                rankTrack: Track("None"), chooseTarget: ChooseTargetKind.AnyTarget, hello: NamedSteps("DamageChosen3"),
                collectible: false, inStarterCollection: false);

            AssertWarns(PoolWithCard(twin), "is also used by");
        }

        #endregion

        #region Starter decks

        /// <summary>
        /// The twenty-five card ids of a shipped starter deck, so a control can change one of them.
        /// </summary>
        static string[] CardsOf(string deckId) => StarterDeck(deckId).ToCardIds().Select(card => card.Value).ToArray();

        /// <summary> The same list with one position replaced. </summary>
        static string[] With(string[] cards, int index, string cardId)
        {
            string[] copy = cards.ToArray();
            copy[index] = cardId;
            return copy;
        }

        [Test]
        public void Accepts_TheShippedStarterDecks()
        {
            // The narrow version of ShippedContent_IsAccepted, and the control that catches an authoring
            // typo in StarterDecks.csv: a card id that means nothing fails the config build outright, but a
            // real card in the wrong deck only shows up here.
            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(ContentPool.FromConfig(Config), sink);

            Assert.That(sink.Errors.Where(error => error.Contains(ContentValidator.StarterDecksSheet)), Is.Empty, sink.Describe());
        }

        [Test]
        public void Rejects_AStarterDeckOfTheWrongSize()
        {
            string[] short24 = CardsOf("FireAndFoam").Take(24).ToArray();

            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf("FireAndFoam", "Fire & Foam", "One short.", short24))),
                "WrongSize");
        }

        [Test]
        public void Rejects_AStarterDeckWithADuplicateCard()
        {
            string[] cards = CardsOf("FireAndFoam");

            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf("FireAndFoam", "Fire & Foam", "Twice over.", With(cards, 24, cards[0])))),
                $"DuplicateCard ({cards[0]})");
        }

        [Test]
        public void Rejects_AStarterDeckWithANullCard()
        {
            // This is what a blank row inside a deck block produces: the vertical layout fills a skipped
            // element with a default one, so an accidental empty line is a null card rather than a short deck.
            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf("FireAndFoam", "Fire & Foam", "A hole in it.", With(CardsOf("FireAndFoam"), 7, null)))),
                "UnknownCard");
        }

        [Test]
        public void Rejects_AStarterDeckWithANullCard_AndNamesWhichPosition()
        {
            // A null entry carries no card to name, so without the position the author is left finding the
            // hole by eye in a twenty-five-line block. Two blanks, so the message has to list rather than
            // report the first.
            string[] holed = With(With(CardsOf("FireAndFoam"), 11, null), 23, null);

            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf("FireAndFoam", "Fire & Foam", "Two holes.", holed))),
                "Cards 12, 24 of 25 are blank");
        }

        [Test]
        public void Rejects_AStarterDeckHoldingANonStarterCard()
        {
            // A collectible card outside the starter collection, which the shipped pool has none of: every
            // collectible card is granted today. TheAcorn cannot serve as this control because it is also
            // non-collectible, so the shared validator would refuse the deck before this rule was reached.
            CardInfo outsideTheGrant = BadCard("ExtraWanderer", collectible: true, inStarter: false);

            AssertRejects(
                Pool(
                    cards: Plus(ContentPool.FromConfig(Config).Cards, outsideTheGrant),
                    starterDecks: StarterDecksWith(StarterDeckOf(
                        "FireAndFoam", "Fire & Foam", "Not yours yet.",
                        With(CardsOf("FireAndFoam"), 24, "ExtraWanderer"),
                        synthetic: new[] { outsideTheGrant }))),
                "not in the starter collection");
        }

        [Test]
        public void Rejects_AStarterDeckDrawingOnThreeClans()
        {
            // One Wanderer swapped for a third clan's card, which is the shared clan limit rather than the
            // exactly-two-clans rule.
            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf(
                    "FireAndFoam", "Fire & Foam", "One clan too many.",
                    With(CardsOf("FireAndFoam"), 24, "MoonlitAlleycat")))),
                "TooManyClans");
        }

        [Test]
        public void Rejects_AMonoClanStarterDeck()
        {
            // Six clan cards plus all fifteen Wanderers is 21, so the pool cannot express a mono-clan deck at
            // all and the shared size rule catches this before the two-clan lower bound does. The lower bound needs a
            // pool the shipped content cannot build — see Rejects_AStarterDeckOfWanderersAlone.
            string[] monoClan = CardsOf("FireAndFoam").Take(6)
                .Concat(CardsOf("FireAndFoam").Skip(12))
                .ToArray();
            Assert.That(monoClan.Length, Is.EqualTo(19), "six Kitsune plus thirteen Wanderers");

            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf("FireAndFoam", "Fire & Foam", "Kitsune alone.", monoClan))),
                "WrongSize");
        }

        [Test]
        public void Rejects_AStarterDeckOfWanderersAlone()
        {
            // The two-clan lower bound: exactly two clans, not at most two. No list the shipped pool can build
            // violates it — 15 Wanderers is short of 25 and singleton forbids padding — so the control pads
            // the pool with twelve more Wanderers, which is the only way to reach the rule at all.
            List<CardInfo> padded = ContentPool.FromConfig(Config).Cards.ToList();
            List<string>   filler = new List<string>();
            for (int ndx = 0; ndx < 12; ndx++)
            {
                string id = $"FillerWanderer{ndx.ToString(CultureInfo.InvariantCulture)}";
                padded.Add(BadCard(id, collectible: true, inStarter: true));
                filler.Add(id);
            }

            string[] wanderersOnly = CardsOf("FireAndFoam").Skip(12).Concat(filler).ToArray();
            Assert.That(wanderersOnly.Length, Is.EqualTo(Config.Global.DeckSize), "thirteen real Wanderers and twelve filler");

            AssertRejects(
                Pool(
                    cards: padded,
                    starterDecks: StarterDecksWith(StarterDeckOf(
                        "FireAndFoam", "Fire & Foam", "Nobody's clan.", wanderersOnly, synthetic: padded))),
                "draws on 0 clan-limited clans");
        }

        [Test]
        public void Rejects_AClanNoStarterDeckPlays()
        {
            // The two Moonlight decks dropped. The four that remain are legal and on distinct pairs, so the
            // only thing wrong with this pool is that a fresh account can never play Moonlight.
            IReadOnlyList<StarterDeckInfo> withoutMoonlight = ContentPool.FromConfig(Config).StarterDecks
                .Where(deck => deck.StarterDeckId != StarterDeckId.FromString("AlleySparks")
                            && deck.StarterDeckId != StarterDeckId.FromString("PorchlightPack"))
                .ToList();

            AssertRejects(Pool(starterDecks: withoutMoonlight), "Clans/Moonlight/: Clan 'Moonlight' is in no starter deck");
        }

        [Test]
        public void Rejects_TwoStarterDecksOnTheSameClanPair()
        {
            // The sixth deck re-authored onto the first deck's clan pair.
            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf(
                    "EmberAndOak", "Ember & Oak", "Fire and foam again.", CardsOf("FireAndFoam")))),
                "'FireAndFoam' and 'EmberAndOak' draw on the same two clans");
        }

        [Test]
        public void Rejects_AStarterDeckWithNoDisplayName()
        {
            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf("FireAndFoam", "", "No name.", CardsOf("FireAndFoam")))),
                "has no display name");
        }

        [Test]
        public void Rejects_AStarterDeckWithADisplayNameThePickerCannotFit()
        {
            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf(
                    "FireAndFoam", new string('x', PlayerDeck.MaxNameLength + 1), "Too long.", CardsOf("FireAndFoam")))),
                "the picker is laid out for at most");
        }

        [Test]
        public void Rejects_AStarterDeckWithNoDescription()
        {
            AssertRejects(
                Pool(starterDecks: StarterDecksWith(StarterDeckOf("FireAndFoam", "Fire & Foam", "", CardsOf("FireAndFoam")))),
                "has no description");
        }

        [Test]
        public void Rejects_AnEmptyStarterDeckPool()
        {
            AssertRejects(Pool(starterDecks: new List<StarterDeckInfo>()), "There are no starter decks");
        }

        #endregion
    }
}
