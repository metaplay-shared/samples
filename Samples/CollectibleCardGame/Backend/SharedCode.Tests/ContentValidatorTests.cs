using Metaplay.Core;
using NUnit.Framework;
using System;
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
        public void ShippedContent_ValidatesWithNoErrorsOrWarnings()
        {
            // The same checks the config build runs, over the archive that build produced. Ten cards per clan
            // plus fifteen Wanderers meet the deck size, so no mono-clan deck is short either.
            CollectingIssueSink sink = new CollectingIssueSink();
            ContentValidator.Validate(ContentPool.FromConfig(Config), sink);

            Assert.That(sink.Errors, Is.Empty, sink.Describe());
            Assert.That(sink.Warnings, Is.Empty, sink.Describe());
        }

        #region The tables

        /// <summary> What the validator must say about a pool: fail the build, warn without failing, or accept. </summary>
        public enum Verdict { Rejects, Warns, Accepts }

        /// <summary> One control: a named pool with one thing changed, and what the validator must say about it. </summary>
        static TestCaseData Row(string name, Verdict verdict, string fragment, Func<ContentPool> pool)
            => new TestCaseData(verdict, fragment, pool).SetName(name);

        static void AssertVerdict(Verdict verdict, string fragment, Func<ContentPool> pool)
        {
            switch (verdict)
            {
                case Verdict.Rejects:
                    AssertRejects(pool(), fragment);
                    break;
                case Verdict.Warns:
                    AssertWarns(pool(), fragment);
                    break;
                default:
                    CollectingIssueSink sink = new CollectingIssueSink();
                    ContentValidator.Validate(pool(), sink);
                    Assert.That(sink.Errors, Is.Empty, sink.Describe());
                    break;
            }
        }

        [TestCaseSource(nameof(StepShape))]
        public void AStepIsHeldToItsOpsShape(Verdict verdict, string fragment, Func<ContentPool> pool) => AssertVerdict(verdict, fragment, pool);

        [TestCaseSource(nameof(StatDomain))]
        public void AStatIsHeldInsideTheMatchsDomain(Verdict verdict, string fragment, Func<ContentPool> pool) => AssertVerdict(verdict, fragment, pool);

        [TestCaseSource(nameof(Globals))]
        public void TheGlobalsAndThePoolAreHeldToTheirInvariants(Verdict verdict, string fragment, Func<ContentPool> pool) => AssertVerdict(verdict, fragment, pool);

        [TestCaseSource(nameof(CardShape))]
        public void ACardTrackOrWeatherIsHeldToItsShape(Verdict verdict, string fragment, Func<ContentPool> pool) => AssertVerdict(verdict, fragment, pool);

        [TestCaseSource(nameof(StarterDecks))]
        public void AStarterDeckIsHeldToTheStarterRules(Verdict verdict, string fragment, Func<ContentPool> pool) => AssertVerdict(verdict, fragment, pool);

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

        /// <summary> The vocabulary, each step's own shape, and the shape of what a card or weather feeds it. </summary>
        static IEnumerable<TestCaseData> StepShape()
        {
            yield return Row("Rejects_EngineFlagKeywordTheEngineDoesNotImplement", Verdict.Rejects, "Keywords/Bouncy",
                () => Pool(keywords: Plus(ContentPool.FromConfig(Config).Keywords,
                    new KeywordInfo(KeywordId.FromString("Bouncy"), KeywordKind.EngineFlag, "Bouncy", "Not a thing."))));

            // A second row whose id spells the same flag would give the engine two identities for one rule.
            yield return Row("Rejects_TwoKeywordRowsNamingOneEngineFlag", Verdict.Rejects, "same engine flag",
                () => Pool(keywords: Plus(ContentPool.FromConfig(Config).Keywords,
                    new KeywordInfo(KeywordId.FromString("Guard"), KeywordKind.EngineFlag, "Bodyguard", "A duplicate."))));

            yield return Row("Rejects_TriggerLabelThatNamesNoTrigger", Verdict.Rejects, "Keywords/Farewell",
                () => Pool(keywords: Plus(ContentPool.FromConfig(Config).Keywords,
                    new KeywordInfo(KeywordId.FromString("Farewell"), KeywordKind.TriggerLabel, "Farewell:", "Not a trigger."))));

            yield return Row("Rejects_DamageStepWithNoAmount", Verdict.Rejects, "EffectSteps/NoAmount",
                () => PoolWithStep(Step("NoAmount", EffectOp.Damage, EffectTargetKind.EnemyDen)));

            yield return Row("Rejects_NegativeAmountOnAnythingButBuff", Verdict.Rejects, "must not be negative",
                () => PoolWithStep(Step("NegativeHeal", EffectOp.Heal, EffectTargetKind.OwnDen, new EffectAmount(-2))));

            yield return Row("Rejects_SeatScopedStepWithATarget", Verdict.Rejects, "must have no Target",
                () => PoolWithStep(Step("TargetedDraw", EffectOp.Draw, EffectTargetKind.OwnDen, new EffectAmount(1))));

            yield return Row("Rejects_GainManaWithNoDuration", Verdict.Rejects, "requires a Duration",
                () => PoolWithStep(Step("ManaNoDuration", EffectOp.GainMana, amount: new EffectAmount(1))));

            yield return Row("Rejects_PeekThatKeepsMoreThanItLooksAt", Verdict.Rejects, "keeps 3 of the 2 cards",
                () => PoolWithStep(Step("GreedyPeek", EffectOp.Peek, amount: new EffectAmount(2), amount2: new EffectAmount(3))));

            yield return Row("Rejects_SummonOfATrick", Verdict.Rejects, "Only critters can be summoned",
                () => PoolWithStep(Step("SummonATrick", EffectOp.Summon, amount: new EffectAmount(1), card: Card("Foxfire"))));

            yield return Row("Rejects_GrantOfATriggerLabel", Verdict.Rejects, "rather than an engine flag",
                () => PoolWithStep(Step("GrantHello", EffectOp.GrantKeyword, EffectTargetKind.Self, keyword: Keyword("Hello"))));

            yield return Row("Rejects_CopyFromGraveyardPointedAtTheBoard", Verdict.Rejects, "OwnGraveyard or EnemyGraveyard",
                () => PoolWithStep(Step("CopyFromBoard", EffectOp.CopyFromGraveyard, EffectTargetKind.AllEnemy, selector: GraveyardSelector.Cheapest)));

            yield return Row("Rejects_CopyFromGraveyardWithNoSelector", Verdict.Rejects, "requires a Selector",
                () => PoolWithStep(Step("CopyAnything", EffectOp.CopyFromGraveyard, EffectTargetKind.EnemyGraveyard)));

            yield return Row("Rejects_BounceAimedAtADen", Verdict.Rejects, "needs a critter target",
                () => PoolWithStep(Step("BounceADen", EffectOp.Bounce, EffectTargetKind.EnemyDen)));

            // AnyTarget admits a Den, and a Den cannot be bounced. The card, not the step, is at fault.
            yield return Row("Rejects_CritterOnlyStepFedAChoiceThatMayBeADen", Verdict.Rejects, "Cards/BadBounce",
                () => Pool(cards: Plus(ContentPool.FromConfig(Config).Cards, new CardInfo(
                    CardId.FromString("BadBounce"), "Bad Bounce", Clan("Tidepool"), CardType.Trick, CardRarity.Common, cost: 2,
                    rankTrack: Track("None"), chooseTarget: ChooseTargetKind.AnyTarget,
                    hello: Steps(Config.EffectSteps[EffectStepId.FromString("BounceChosen")]),
                    collectible: false, inStarterCollection: false))));

            yield return Row("Rejects_SelfTargetOnATrick", Verdict.Rejects, "targets Self",
                () => Pool(cards: Plus(ContentPool.FromConfig(Config).Cards, new CardInfo(
                    CardId.FromString("SelfishTrick"), "Selfish Trick", Clan("Mossback"), CardType.Trick, CardRarity.Common, cost: 2,
                    rankTrack: Track("None"), hello: Steps(Config.EffectSteps[EffectStepId.FromString("GrowSelf1")]),
                    collectible: false, inStarterCollection: false))));

            yield return Row("Rejects_WeatherStepThatWantsAChosenTarget", Verdict.Rejects, "nobody to choose",
                () => Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers, new WeatherInfo(
                    WeatherId.FromString("PickyWeather"), "Picky Weather",
                    trigger: WeatherTrigger.TurnStart, steps: NamedSteps("DamageChosen3")))));

            yield return Row("Rejects_WeatherSelfTargetOutsideADeathHook", Verdict.Rejects, "only a CritterDies hook",
                () => Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers, new WeatherInfo(
                    WeatherId.FromString("SelfishWeather"), "Selfish Weather",
                    trigger: WeatherTrigger.TurnStart, steps: NamedSteps("GrowSelf1")))));
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

        /// <summary>
        /// The stat domain is bounded at both ends, and the two ends fail differently: a number the match cannot
        /// contain is an error, and a number below the stat quantum is a warning, because a deliberately tiny
        /// one is legal. The boundaries are inclusive on the legal side.
        /// </summary>
        static IEnumerable<TestCaseData> StatDomain()
        {
            yield return Row("Rejects_CritterThatEntersPlayDead", Verdict.Rejects, "Cards/DeadOnArrival",
                () => PoolWithCard(BadCard("DeadOnArrival", health: 0)));

            yield return Row("Rejects_CritterTougherThanTheEnemyDen", Verdict.Rejects, "above the Den's",
                () => PoolWithCard(BadCard("Unkillable", health: Config.Global.DenStartingHp + 1)));

            yield return Row("Rejects_CritterHittingHarderThanADenHasHitPoints", Verdict.Rejects, "above the Den's",
                () => PoolWithCard(BadCard("OneShot", attack: Config.Global.DenStartingHp + 1)));

            // A body worth exactly a Den is extreme content, not broken content.
            yield return Row("Accepts_ACritterExactlyAsToughAsTheDen", Verdict.Accepts, null,
                () => PoolWithCard(BadCard("Exactly", health: Config.Global.DenStartingHp)));

            // A 2/3 still parses and still plays, and is almost certainly a row somebody forgot to scale.
            yield return Row("Warns_CritterBelowTheStatQuantum", Verdict.Warns, "below the stat quantum",
                () => PoolWithCard(BadCard("Unscaled", attack: 2, health: 3)));

            yield return Row("Rejects_ABlowLargerThanTheWholeDen", Verdict.Rejects, "the Den's whole",
                () => PoolWithStep(Step("Annihilate", EffectOp.Damage, EffectTargetKind.EnemyDen,
                    amount: new EffectAmount(Config.Global.DenStartingHp + 1))));

            yield return Row("Warns_AHealBelowTheStatQuantum", Verdict.Warns, "below the stat quantum",
                () => PoolWithStep(Step("TinyHeal", EffectOp.Heal, EffectTargetKind.OwnDen, amount: new EffectAmount(2))));

            // The per-unit factor is the number a scaling card actually adds per unit, so it is held to the same
            // domain as a literal: a bare count against a Den in the hundreds does nothing visible.
            yield return Row("Warns_ACounterWorthLessThanTheQuantumPerUnit", Verdict.Warns, "below the stat quantum",
                () => PoolWithStep(Step("BareCount", EffectOp.Damage, EffectTargetKind.EnemyDen,
                    amount: new EffectAmount(0, EffectCounter.FriendlyCritters))));

            // Buff is where the risk lives, because its numbers look like the rank-track deltas that are meant
            // to be sub-quantum.
            yield return Row("Warns_ABuffBelowTheStatQuantum", Verdict.Warns, "below the stat quantum",
                () => PoolWithStep(Step("TinyGrowth", EffectOp.Buff, EffectTargetKind.Self,
                    amount: new EffectAmount(1), amount2: new EffectAmount(1))));

            // The bounds are on the magnitude: the sign says which way the stat moves.
            yield return Row("Warns_ANegativeBuffBelowTheStatQuantum", Verdict.Warns, "below the stat quantum",
                () => PoolWithStep(Step("TinyShrink", EffectOp.Buff, EffectTargetKind.Chosen,
                    amount: new EffectAmount(-2), amount2: new EffectAmount(-2)), chooseTarget: ChooseTargetKind.EnemyCritter));

            // Amount2 is a stat in its own right: a Buff scaled in one column and not the other is the likeliest
            // way to half-scale a card.
            yield return Row("Warns_ASecondAmountBelowTheStatQuantum", Verdict.Warns, "Amount2",
                () => PoolWithStep(Step("HalfScaled", EffectOp.Buff, EffectTargetKind.Self,
                    amount: new EffectAmount(10), amount2: new EffectAmount(2))));

            yield return Row("Rejects_ABuffLargerThanTheWholeDen", Verdict.Rejects, "past the Den's whole",
                () => PoolWithStep(Step("Colossal", EffectOp.Buff, EffectTargetKind.Self,
                    amount: new EffectAmount(Config.Global.DenStartingHp + 1), amount2: new EffectAmount(5))));

            yield return Row("Rejects_ASecondAmountLargerThanTheWholeDen", Verdict.Rejects, "Amount2",
                () => PoolWithStep(Step("ColossalHealth", EffectOp.Buff, EffectTargetKind.Self,
                    amount: new EffectAmount(5), amount2: new EffectAmount(Config.Global.DenStartingHp + 1))));
        }

        /// <summary> The shipped globals with one edit applied. </summary>
        static ContentPool PoolWithGlobal(Action<GlobalConfig> edit)
        {
            GlobalConfig global = EditableGlobal();
            edit(global);
            return Pool(global: global);
        }

        /// <summary>
        /// The globals, and the pool-level rules. A threshold outside its range is a knob that lies: a lock rank
        /// above the ceiling locks nothing, a Power Score gap no two decks can straddle makes every match Even.
        /// Zero is a supported newcomer shield: it means the shield is off.
        /// </summary>
        static IEnumerable<TestCaseData> Globals()
        {
            // The grant does not go through the full-hand rule — a seat compensated into its own deck has not
            // been compensated — so the room for it is a content invariant.
            yield return Row("Rejects_AHandLimitThatDoesNotCoverTheCompensationCard", Verdict.Rejects, "compensation card does not fit the hand limit",
                () => PoolWithGlobal(global => global.OpeningHandSecondPlayer = global.MaxHandSize));

            yield return Row("Accepts_AHandLimitWithExactlyRoomForTheCompensationCard", Verdict.Accepts, null,
                () => PoolWithGlobal(global => global.OpeningHandSecondPlayer = global.MaxHandSize - 1));

            yield return Row("Rejects_NonPositiveStatQuantum", Verdict.Rejects, "StatQuantum",
                () => PoolWithGlobal(global => global.StatQuantum = 0));

            yield return Row("Rejects_NonPositivePowerScoreGapThreshold", Verdict.Rejects, "PowerScoreGapThreshold",
                () => PoolWithGlobal(global => global.PowerScoreGapThreshold = 0));

            yield return Row("Rejects_APowerScoreGapThresholdNoTwoDecksCouldStraddle", Verdict.Rejects, "PowerScoreGapThreshold",
                () => PoolWithGlobal(global => global.PowerScoreGapThreshold = global.DeckSize * (global.RankMax - global.RankMin)));

            yield return Row("Rejects_ANegativeNewcomerShield", Verdict.Rejects, "NewcomerShieldMatches",
                () => PoolWithGlobal(global => global.NewcomerShieldMatches = -1));

            yield return Row("Accepts_ANewcomerShieldOfZero", Verdict.Accepts, null,
                () => PoolWithGlobal(global => global.NewcomerShieldMatches = 0));

            yield return Row("Rejects_ANonPositiveInitialRating", Verdict.Rejects, "InitialRating",
                () => PoolWithGlobal(global => global.InitialRating = 0));

            // A K of zero is a rating that never moves, which makes every band a permanent one.
            yield return Row("Rejects_ANonPositiveRatingKFactor", Verdict.Rejects, "RatingKFactor",
                () => PoolWithGlobal(global => global.RatingKFactor = 0));

            yield return Row("Rejects_OpeningHandThatDoesNotFitTheHandLimit", Verdict.Rejects, "does not fit the hand limit",
                () => PoolWithGlobal(global => global.OpeningHandFirstPlayer = global.MaxHandSize + 1));

            yield return Row("Rejects_DeckThatDoesNotCoverTheOpeningHand", Verdict.Rejects, "does not cover an opening hand",
                () => PoolWithGlobal(global => global.DeckSize = 3));

            yield return Row("Rejects_RankRangeThatRunsBackwards", Verdict.Rejects, "below RankMin",
                () => PoolWithGlobal(global => global.RankMax = 0));

            yield return Row("Rejects_LockThresholdAboveTheRankCeiling", Verdict.Rejects, "outside the rank range",
                () => PoolWithGlobal(global => global.MinLockRank = global.RankMax + 1));

            yield return Row("Rejects_LockThresholdBelowTheRankFloor", Verdict.Rejects, "outside the rank range",
                () => PoolWithGlobal(global => global.MinLockRank = global.RankMin - 1));

            yield return Row("Rejects_CollectibleSecondPlayerBonusCard", Verdict.Rejects, "must not be buildable into a deck",
                () => PoolWithGlobal(global => global.SecondPlayerBonusCard = Card("Foxfire")));

            yield return Row("Rejects_SecondPlayerBonusCardThatIsNotATrick", Verdict.Rejects, "must be a trick",
                () => PoolWithGlobal(global => global.SecondPlayerBonusCard = Card("LambToken")));

            yield return Row("Rejects_PoolTooSmallToBuildALegalDeck", Verdict.Rejects, "No allowed clan combination",
                () => Pool(cards: ContentPool.FromConfig(Config).Cards.Take(4).ToList()));

            yield return Row("Rejects_NonCollectibleCardInTheStarterCollection", Verdict.Rejects, "never live in a collection",
                () => PoolWithCard(BadCard("StarterToken", collectible: false, inStarter: true)));

            yield return Row("Rejects_NonCollectibleCardOnARealRankTrack", Verdict.Rejects, "no rank to grow",
                () => PoolWithCard(BadCard("RankedToken", track: "CritterDefault", collectible: false)));

            // Every card is still collectible, so the pool is fine; only the starter grant is too thin.
            yield return Row("Rejects_StarterCollectionThatCannotBuildADeck", Verdict.Rejects, "the starter collection",
                () => Pool(cards: ContentPool.FromConfig(Config).Cards
                    .Select(card => new CardInfo(
                        card.CardId, card.DisplayName, card.Clan, card.Type, card.Rarity, card.Cost, card.RankTrack,
                        attack: card.Attack, health: card.Health, isSnack: card.IsSnack, keywords: card.Keywords,
                        chooseTarget: card.ChooseTarget, hello: card.Hello, goodbye: card.Goodbye, onAttack: card.OnAttack,
                        turnStart: card.TurnStart, turnEnd: card.TurnEnd, collectible: card.Collectible,
                        inStarterCollection: false))
                    .ToList()));
        }

        /// <summary> A critter whose track breaks at <paramref name="rank"/> and is repaired one rank later. </summary>
        static ContentPool PoolWithAnIntermediateRankRepaired(int rank)
        {
            RankTrackStep invalid = new RankTrackStep(-6, 0, 0, 0);
            RankTrackStep repair  = new RankTrackStep(6, 0, 0, 0);
            RankTrackInfo track = new RankTrackInfo(RankTrackId.FromString("Intermediate"),
                rank2: rank == 2 ? invalid : null,
                rank3: rank == 2 ? repair : null,
                rank4: rank == 4 ? invalid : null,
                rank5: rank == 4 ? repair : null);
            CardInfo card = new CardInfo(CardId.FromString("IntermediateCard"), "Intermediate Card",
                Clan("Wanderer"), CardType.Critter, CardRarity.Common, 2,
                MetaRef<RankTrackInfo>.FromItem(track), attack: 5, health: 5);

            return Pool(cards: Plus(ContentPool.FromConfig(Config).Cards, card),
                rankTracks: Plus(ContentPool.FromConfig(Config).RankTracks, track));
        }

        /// <summary> A card's shape, its rank track, a weather's hooks, and the rows nothing uses. </summary>
        static IEnumerable<TestCaseData> CardShape()
        {
            yield return Row("Rejects_TrickWithCombatStats", Verdict.Rejects, "only critters have Attack and Health",
                () => PoolWithCard(BadCard("BeefyTrick", CardType.Trick, attack: 2, health: 2)));

            yield return Row("Rejects_TrickThatBindsMoreThanHello", Verdict.Rejects, "may bind Hello only",
                () => PoolWithCard(BadCard("LingeringTrick", CardType.Trick, attack: 0, health: 0, goodbye: NamedSteps("BurnDen1"))));

            yield return Row("Rejects_CritterMarkedAsASnack", Verdict.Rejects, "a critter cannot be a Snack",
                () => PoolWithCard(BadCard("SnackCritter", isSnack: true)));

            yield return Row("Rejects_SnackThatPointsAtTheEnemy", Verdict.Rejects, "they point at your own side",
                () => PoolWithCard(BadCard("HostileSnack", CardType.Trick, attack: 0, health: 0, isSnack: true,
                    chooseTarget: ChooseTargetKind.EnemyCritter, hello: NamedSteps("ShrinkChosen11"))));

            yield return Row("Rejects_TrickThatPrintsKeywords", Verdict.Rejects, "only critters carry them",
                () => PoolWithCard(BadCard("KeywordedTrick", CardType.Trick, attack: 0, health: 0,
                    keywords: new List<MetaRef<KeywordInfo>> { Keyword("Guard") })));

            yield return Row("Rejects_ChosenStepOnACardThatAsksForNoTarget", Verdict.Rejects, "the player is never asked to pick one",
                () => PoolWithCard(BadCard("SilentChoice", hello: NamedSteps("DamageChosen3"))));

            yield return Row("Rejects_ChooseTargetNoStepUses", Verdict.Rejects, "the pick would do nothing",
                () => PoolWithCard(BadCard("PointlessChoice", chooseTarget: ChooseTargetKind.AnyCritter, hello: NamedSteps("DrawOne"))));

            yield return Row("Rejects_CardThatPrintsATriggerLabelAsAKeyword", Verdict.Rejects, "derives those labels",
                () => PoolWithCard(BadCard("SelfDescribing", keywords: new List<MetaRef<KeywordInfo>> { Keyword("Goodbye") })));

            yield return Row("Rejects_InvalidIntermediateRankEvenWhenLaterRankRepairsIt_AtRank2", Verdict.Rejects, "at rank 2",
                () => PoolWithAnIntermediateRankRepaired(2));

            yield return Row("Rejects_InvalidIntermediateRankEvenWhenLaterRankRepairsIt_AtRank4", Verdict.Rejects, "at rank 4",
                () => PoolWithAnIntermediateRankRepaired(4));

            // TrickCheaper3 takes a point off the cost at rank 3, which a zero-cost card cannot pay.
            yield return Row("Rejects_TrackThatTakesACardBelowZeroCost", Verdict.Rejects, "RankTracks/TrickCheaper3",
                () => PoolWithCard(BadCard("FreeAndCheaper", CardType.Trick, cost: 0, attack: 0, health: 0,
                    hello: NamedSteps("DrawOne"), track: "TrickCheaper3")));

            yield return Row("Rejects_AmountScalingTrackOnACardWithNoAmountToScale", Verdict.Rejects, "no literal amount to scale",
                () => PoolWithCard(BadCard("NothingToScale", CardType.Trick, attack: 0, health: 0,
                    hello: NamedSteps("BounceAll"), track: "TrickAmount3")));

            yield return Row("Rejects_CostRuleThatChangesNothing", Verdict.Rejects, "the rule changes nothing",
                () => Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers,
                    new WeatherInfo(WeatherId.FromString("FlatRain"), "Flat Rain", costScope: WeatherCostScope.AllTricks, costDelta: 0))));

            yield return Row("Rejects_TriggerWithNoSteps", Verdict.Rejects, "binds no steps",
                () => Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers,
                    new WeatherInfo(WeatherId.FromString("SilentDawn"), "Silent Dawn", trigger: WeatherTrigger.TurnStart))));

            yield return Row("Rejects_WeatherWithNoHooksAtAll", Verdict.Rejects, "no hooks at all",
                () => Pool(weathers: Plus(ContentPool.FromConfig(Config).Weathers,
                    new WeatherInfo(WeatherId.FromString("PlainDay"), "Plain Day"))));

            yield return Row("Rejects_EmptyWeatherPool", Verdict.Rejects, "Weather pool is empty",
                () => Pool(weathers: new List<WeatherInfo>()));

            yield return Row("Warns_AboutAStepNothingUses", Verdict.Warns, "EffectSteps/OrphanDraw",
                () => Pool(effectSteps: Plus(ContentPool.FromConfig(Config).EffectSteps,
                    Step("OrphanDraw", EffectOp.Draw, amount: new EffectAmount(1)))));

            yield return Row("Warns_AboutARankTrackNothingUses", Verdict.Warns, "RankTracks/OrphanTrack",
                () => Pool(rankTracks: Plus(ContentPool.FromConfig(Config).RankTracks,
                    new RankTrackInfo(RankTrackId.FromString("OrphanTrack"), rank3: new RankTrackStep(1, 0, 0, 0)))));

            yield return Row("Warns_AboutTwoCardsWithTheSameName", Verdict.Warns, "is also used by",
                () => PoolWithCard(new CardInfo(
                    CardId.FromString("SecondFoxfire"), "Foxfire", Clan("Kitsune"), CardType.Trick, CardRarity.Common, cost: 2,
                    rankTrack: Track("None"), chooseTarget: ChooseTargetKind.AnyTarget, hello: NamedSteps("DamageChosen3"),
                    collectible: false, inStarterCollection: false)));
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

        /// <summary> The shipped starter decks with Fire &amp; Foam re-authored. </summary>
        static ContentPool PoolWithFireAndFoam(string displayName, string description, string[] cards, IReadOnlyList<CardInfo> synthetic = null)
            => Pool(starterDecks: StarterDecksWith(StarterDeckOf("FireAndFoam", displayName, description, cards, synthetic)));

        static IEnumerable<TestCaseData> StarterDecks()
        {
            yield return Row("Rejects_AStarterDeckOfTheWrongSize", Verdict.Rejects, "WrongSize",
                () => PoolWithFireAndFoam("Fire & Foam", "One short.", CardsOf("FireAndFoam").Take(24).ToArray()));

            // A null entry carries no card to name, so without the position the author is left finding the hole
            // by eye in a twenty-five-line block. Two blanks, so the message has to list rather than report the
            // first.
            yield return Row("Rejects_AStarterDeckWithANullCard_AndNamesWhichPosition", Verdict.Rejects, "Cards 12, 24 of 25 are blank",
                () => PoolWithFireAndFoam("Fire & Foam", "Two holes.", With(With(CardsOf("FireAndFoam"), 11, null), 23, null)));

            // A collectible card outside the starter collection. TheAcorn cannot serve as this control because it
            // is also non-collectible, so the shared validator would refuse the deck before this rule was reached.
            yield return Row("Rejects_AStarterDeckHoldingANonStarterCard", Verdict.Rejects, "not in the starter collection",
                () =>
                {
                    CardInfo outsideTheGrant = BadCard("ExtraWanderer", collectible: true, inStarter: false);
                    return Pool(
                        cards: Plus(ContentPool.FromConfig(Config).Cards, outsideTheGrant),
                        starterDecks: StarterDecksWith(StarterDeckOf(
                            "FireAndFoam", "Fire & Foam", "Not yours yet.",
                            With(CardsOf("FireAndFoam"), 24, "ExtraWanderer"),
                            synthetic: new[] { outsideTheGrant })));
                });

            // One Wanderer swapped for a third clan's card, which is the shared clan limit rather than the
            // exactly-two-clans rule.
            yield return Row("Rejects_AStarterDeckDrawingOnThreeClans", Verdict.Rejects, "TooManyClans",
                () => PoolWithFireAndFoam("Fire & Foam", "One clan too many.", With(CardsOf("FireAndFoam"), 24, "MoonlitAlleycat")));

            // Six clan cards plus the deck's thirteen Wanderers is 19, so the shared size rule catches a mono-clan
            // deck before the two-clan lower bound does; the lower bound needs a padded pool (the next row).
            yield return Row("Rejects_AMonoClanStarterDeck", Verdict.Rejects, "WrongSize",
                () =>
                {
                    string[] monoClan = CardsOf("FireAndFoam").Take(6).Concat(CardsOf("FireAndFoam").Skip(12)).ToArray();
                    Assert.That(monoClan.Length, Is.EqualTo(19), "six Kitsune plus thirteen Wanderers");
                    return PoolWithFireAndFoam("Fire & Foam", "Kitsune alone.", monoClan);
                });

            // The two-clan lower bound: exactly two clans, not at most two. No list the shipped pool can build
            // violates it — 15 Wanderers is short of 25 and singleton forbids padding — so the control pads the
            // pool with twelve more Wanderers, which is the only way to reach the rule at all.
            yield return Row("Rejects_AStarterDeckOfWanderersAlone", Verdict.Rejects, "draws on 0 clan-limited clans",
                () =>
                {
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

                    return Pool(
                        cards: padded,
                        starterDecks: StarterDecksWith(StarterDeckOf("FireAndFoam", "Fire & Foam", "Nobody's clan.", wanderersOnly, synthetic: padded)));
                });

            // The two Moonlight decks dropped. The four that remain are legal and on distinct pairs, so the only
            // thing wrong with this pool is that a fresh account can never play Moonlight.
            yield return Row("Rejects_AClanNoStarterDeckPlays", Verdict.Rejects, "Clans/Moonlight/: Clan 'Moonlight' is in no starter deck",
                () => Pool(starterDecks: ContentPool.FromConfig(Config).StarterDecks
                    .Where(deck => deck.StarterDeckId != StarterDeckId.FromString("AlleySparks")
                                && deck.StarterDeckId != StarterDeckId.FromString("PorchlightPack"))
                    .ToList()));

            // The sixth deck re-authored onto the first deck's clan pair.
            yield return Row("Rejects_TwoStarterDecksOnTheSameClanPair", Verdict.Rejects, "'FireAndFoam' and 'EmberAndOak' draw on the same two clans",
                () => Pool(starterDecks: StarterDecksWith(StarterDeckOf(
                    "EmberAndOak", "Ember & Oak", "Fire and foam again.", CardsOf("FireAndFoam")))));

            yield return Row("Rejects_AStarterDeckWithNoDisplayName", Verdict.Rejects, "has no display name",
                () => PoolWithFireAndFoam("", "No name.", CardsOf("FireAndFoam")));

            yield return Row("Rejects_AStarterDeckWithADisplayNameThePickerCannotFit", Verdict.Rejects, "the picker is laid out for at most",
                () => PoolWithFireAndFoam(new string('x', PlayerDeck.MaxNameLength + 1), "Too long.", CardsOf("FireAndFoam")));

            yield return Row("Rejects_AStarterDeckWithNoDescription", Verdict.Rejects, "has no description",
                () => PoolWithFireAndFoam("Fire & Foam", "", CardsOf("FireAndFoam")));

            yield return Row("Rejects_AnEmptyStarterDeckPool", Verdict.Rejects, "There are no starter decks",
                () => Pool(starterDecks: new List<StarterDeckInfo>()));
        }

        [Test]
        public void Rejects_AStarterDeckWithADuplicateCard()
        {
            string[] cards = CardsOf("FireAndFoam");

            AssertRejects(PoolWithFireAndFoam("Fire & Foam", "Twice over.", With(cards, 24, cards[0])), $"DuplicateCard ({cards[0]})");
        }

        #endregion
    }
}
