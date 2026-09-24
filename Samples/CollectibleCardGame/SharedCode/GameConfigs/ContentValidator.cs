using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Game.Logic
{
    /// <summary> Where <see cref="ContentValidator"/> reports what it found. </summary>
    public interface IContentIssueSink
    {
        /// <summary> Content that cannot mean anything. Fails the config build. </summary>
        void Error(string sheet, string row, string column, string message);

        /// <summary> Content that is legal but suspicious, such as a row nothing references. </summary>
        void Warning(string sheet, string row, string column, string message);
    }

    /// <summary>
    /// Everything the content rules are stated over, as plain lists. Taking a snapshot rather than the config
    /// object is what lets the negative-control tests hand the validator a deliberately broken pool without
    /// building a config archive first.
    /// </summary>
    public sealed class ContentPool
    {
        public IReadOnlyList<ClanInfo>        Clans        { get; }
        public IReadOnlyList<KeywordInfo>     Keywords     { get; }
        public IReadOnlyList<RankTrackInfo>   RankTracks   { get; }
        public IReadOnlyList<EffectStepInfo>  EffectSteps  { get; }
        public IReadOnlyList<CardInfo>        Cards        { get; }
        public IReadOnlyList<WeatherInfo>     Weathers     { get; }
        public IReadOnlyList<StarterDeckInfo> StarterDecks { get; }
        public GlobalConfig                   Global       { get; }

        public ContentPool(
            IReadOnlyList<ClanInfo> clans,
            IReadOnlyList<KeywordInfo> keywords,
            IReadOnlyList<RankTrackInfo> rankTracks,
            IReadOnlyList<EffectStepInfo> effectSteps,
            IReadOnlyList<CardInfo> cards,
            IReadOnlyList<WeatherInfo> weathers,
            IReadOnlyList<StarterDeckInfo> starterDecks,
            GlobalConfig global)
        {
            Clans        = clans;
            Keywords     = keywords;
            RankTracks   = rankTracks;
            EffectSteps  = effectSteps;
            Cards        = cards;
            Weathers     = weathers;
            StarterDecks = starterDecks;
            Global       = global;
        }

        public static ContentPool FromConfig(SharedGameConfig config)
        {
            return new ContentPool(
                config.Clans.Values.ToList(),
                config.Keywords.Values.ToList(),
                config.RankTracks.Values.ToList(),
                config.EffectSteps.Values.ToList(),
                config.Cards.Values.ToList(),
                config.Weathers.Values.ToList(),
                config.StarterDecks.Values.ToList(),
                config.Global);
        }
    }

    /// <summary>
    /// Everything the config build must prove about the card content before it is allowed to ship. The rules
    /// are listed in <c>Docs/effects.md</c> ("What config-time validation proves"): every reference
    /// resolves, nothing names vocabulary the engine does not implement, every step and card is shape-legal,
    /// rank tracks stay in range, the pool can build legal decks, Weathers are well-formed, and the global
    /// tunables are mutually consistent.
    /// <para>
    /// Reference resolution itself comes free — an unresolvable <c>MetaRef</c> already fails the build — so
    /// what remains here is everything a reference type cannot express.
    /// </para>
    /// </summary>
    public static class ContentValidator
    {
        public const string ClansSheet        = "Clans";
        public const string KeywordsSheet     = "Keywords";
        public const string RankTracksSheet   = "RankTracks";
        public const string EffectStepsSheet  = "EffectSteps";
        public const string CardsSheet        = "Cards";
        public const string WeathersSheet     = "Weathers";
        public const string StarterDecksSheet = "StarterDecks";
        public const string GlobalSheet       = "Global";

        public static void Validate(ContentPool pool, IContentIssueSink sink)
        {
            List<StepUsage> usages = CollectStepUsages(pool);

            ValidateKeywords(pool, sink);
            ValidateSteps(pool, sink);
            ValidateStepUsage(pool, usages, sink);
            ValidateCards(pool, sink);
            ValidateRankTracks(pool, sink);
            ValidateWeathers(pool, sink);
            ValidatePool(pool, sink);
            ValidateStarterDecks(pool, sink);
            ValidateGlobal(pool, sink);
            ValidateDeadRows(pool, usages, sink);
        }

        #region Step usage

        /// <summary> One place a step is referenced from: a card's trigger binding, or a Weather's hook. </summary>
        sealed class StepUsage
        {
            public EffectStepInfo Step;
            public CardInfo       Card;        // null when the referrer is a Weather
            public CardTrigger    Trigger;     // meaningful only when Card is set
            public WeatherInfo    Weather;     // null when the referrer is a card

            /// <summary> The referring row, for attributing an error to the sheet a designer edits. </summary>
            public string Row    => Card != null ? Card.CardId.Value : Weather.WeatherId.Value;
            public string Column => Card != null ? Trigger.ToString() : "Steps";
        }

        static List<StepUsage> CollectStepUsages(ContentPool pool)
        {
            List<StepUsage> usages = new List<StepUsage>();

            foreach (CardInfo card in pool.Cards)
            {
                foreach (CardTrigger trigger in EffectVocabulary.AllCardTriggers)
                {
                    foreach (MetaRef<EffectStepInfo> stepRef in card.GetSteps(trigger))
                    {
                        if (stepRef?.MaybeRef == null)
                            continue;
                        usages.Add(new StepUsage { Step = stepRef.Ref, Card = card, Trigger = trigger });
                    }
                }
            }

            foreach (WeatherInfo weather in pool.Weathers)
            {
                if (weather.Steps == null)
                    continue;
                foreach (MetaRef<EffectStepInfo> stepRef in weather.Steps)
                {
                    if (stepRef?.MaybeRef == null)
                        continue;
                    usages.Add(new StepUsage { Step = stepRef.Ref, Weather = weather });
                }
            }

            return usages;
        }

        #endregion

        #region Vocabulary

        /// <summary>
        /// Every engine-flag keyword names a flag the engine implements, no two rows alias one flag, and every
        /// trigger label names a trigger.
        /// </summary>
        static void ValidateKeywords(ContentPool pool, IContentIssueSink sink)
        {
            Dictionary<KeywordFlags, KeywordInfo> flagOwners = new Dictionary<KeywordFlags, KeywordInfo>();

            foreach (KeywordInfo keyword in pool.Keywords)
            {
                string row = keyword.KeywordId?.Value;

                if (keyword.Kind == KeywordKind.EngineFlag)
                {
                    KeywordFlags flag = keyword.EngineFlag;
                    if (flag == KeywordFlags.None)
                    {
                        sink.Error(KeywordsSheet, row, nameof(KeywordInfo.KeywordId),
                            $"'{row}' is declared an EngineFlag keyword but the engine implements no such flag. An engine-flag keyword's id must be one of: {string.Join(", ", EngineFlagNames)}.");
                    }
                    else if (flagOwners.TryGetValue(flag, out KeywordInfo other))
                    {
                        sink.Error(KeywordsSheet, row, nameof(KeywordInfo.KeywordId),
                            $"'{row}' names the same engine flag {flag} as '{other.KeywordId}'. One flag, one keyword row.");
                    }
                    else
                        flagOwners.Add(flag, keyword);
                }
                else
                {
                    if (!EffectVocabulary.TryParseName(row, out CardTrigger _))
                    {
                        sink.Error(KeywordsSheet, row, nameof(KeywordInfo.Kind),
                            $"'{row}' is declared a TriggerLabel but names no trigger. A trigger label's id must be a trigger the engine raises.");
                    }
                }

                if (string.IsNullOrWhiteSpace(keyword.DisplayName))
                    sink.Error(KeywordsSheet, row, nameof(KeywordInfo.DisplayName), "Keyword has no display name.");
            }
        }

        static readonly string[] EngineFlagNames =
        {
            nameof(KeywordFlags.Guard),
            nameof(KeywordFlags.Zoomies),
            nameof(KeywordFlags.Bubble),
            nameof(KeywordFlags.Sneaky),
            nameof(KeywordFlags.Snacktime),
        };

        #endregion

        #region Step shape

        /// <summary>
        /// Every step carries exactly the parameters its primitive takes: the required ones present, the
        /// inapplicable ones absent, and a target of a kind the primitive accepts.
        /// </summary>
        static void ValidateSteps(ContentPool pool, IContentIssueSink sink)
        {
            foreach (EffectStepInfo step in pool.EffectSteps)
            {
                StepChecker check = new StepChecker(step, sink);

                // Damage, Heal and Buff are the ops whose amounts are hit points and stat mass; Draw,
                // GainMana, Peek and Summon count cards, mana and critters and are none of this rule's
                // business. Both amount columns are checked, because Buff's second one is a stat too.
                if (step.Op == EffectOp.Damage || step.Op == EffectOp.Heal || step.Op == EffectOp.Buff)
                {
                    CheckAmountAgainstTheDomain(pool, sink, step, nameof(EffectStepInfo.Amount), step.Amount);
                    CheckAmountAgainstTheDomain(pool, sink, step, nameof(EffectStepInfo.Amount2), step.Amount2);
                }

                switch (step.Op)
                {
                    case EffectOp.Damage:
                    case EffectOp.Heal:
                        check.RequireTargetOf(TargetClass.CritterOrDen);
                        check.RequireAmount(minLiteral: 0);
                        check.ForbidAmount2();
                        check.ForbidDuration();
                        check.ForbidKeyword();
                        check.ForbidCard();
                        check.ForbidSelector();
                        break;

                    case EffectOp.Draw:
                        check.RequireNoTarget();
                        check.RequireAmount(minLiteral: 1, literalOnly: true);
                        check.ForbidAmount2();
                        check.ForbidDuration();
                        check.ForbidKeyword();
                        check.ForbidCard();
                        check.ForbidFilter();
                        check.ForbidSelector();
                        break;

                    case EffectOp.GainMana:
                        check.RequireNoTarget();
                        check.RequireAmount(minLiteral: 1, literalOnly: true);
                        check.ForbidAmount2();
                        check.RequireDuration();
                        check.ForbidKeyword();
                        check.ForbidCard();
                        check.ForbidFilter();
                        check.ForbidSelector();
                        break;

                    case EffectOp.Buff:
                        check.RequireTargetOf(TargetClass.CritterOnly);
                        check.RequireAmount(minLiteral: int.MinValue);
                        check.RequireAmount2(minLiteral: int.MinValue);
                        check.ForbidDuration();
                        check.ForbidKeyword();
                        check.ForbidCard();
                        check.ForbidSelector();
                        break;

                    case EffectOp.GrantKeyword:
                        check.RequireTargetOf(TargetClass.CritterOnly);
                        check.ForbidAmount();
                        check.ForbidAmount2();
                        check.ForbidDuration();
                        check.RequireEngineFlagKeyword();
                        check.ForbidCard();
                        check.ForbidFilter();
                        check.ForbidSelector();
                        break;

                    case EffectOp.Summon:
                        check.RequireNoTarget();
                        check.RequireAmount(minLiteral: 1, literalOnly: true);
                        check.ForbidAmount2();
                        check.ForbidDuration();
                        check.ForbidKeyword();
                        check.RequireCritterCard();
                        check.ForbidFilter();
                        check.ForbidSelector();
                        break;

                    case EffectOp.Bounce:
                        check.RequireTargetOf(TargetClass.CritterOnly);
                        check.ForbidAmount();
                        check.ForbidAmount2();
                        check.ForbidDuration();
                        check.ForbidKeyword();
                        check.ForbidCard();
                        check.ForbidFilter();
                        check.ForbidSelector();
                        break;

                    case EffectOp.CopyFromGraveyard:
                        check.RequireTargetOf(TargetClass.Graveyard);
                        check.AllowOptionalAmount(minLiteral: 1, literalOnly: true);
                        check.ForbidAmount2();
                        check.ForbidDuration();
                        check.ForbidKeyword();
                        check.ForbidCard();
                        check.RequireSelector();
                        break;

                    case EffectOp.Peek:
                        check.RequireNoTarget();
                        check.RequireAmount(minLiteral: 1, literalOnly: true);
                        check.RequireAmount2(minLiteral: 1, literalOnly: true);
                        check.RequirePeekKeepFitsLook();
                        check.ForbidDuration();
                        check.ForbidKeyword();
                        check.ForbidCard();
                        check.ForbidFilter();
                        check.ForbidSelector();
                        break;
                }
            }
        }

        /// <summary>
        /// One health-domain amount against the domain it is written in, on the same two-sided rule the card
        /// stats get: a magnitude above the Den's own hit points is a step that ends a match from full in one
        /// resolution, and one below the stat quantum is almost always an amount that was never scaled. A
        /// counter's per-unit factor is checked the same way, because that is the number a scaling card
        /// actually adds per unit.
        /// <para>
        /// Both bounds are on the <b>magnitude</b>, because a Buff's amounts are signed: a −2 shrink is as
        /// much a number somebody forgot to scale as a +2 growth is, and a −200 one breaks a board just as
        /// thoroughly as +200.
        /// </para>
        /// </summary>
        static void CheckAmountAgainstTheDomain(ContentPool pool, IContentIssueSink sink, EffectStepInfo step, string column, EffectAmount amount)
        {
            GlobalConfig global = pool.Global;
            if (global == null || amount == null)
                return;

            string row       = step.StepId?.Value;
            int    unit      = amount.IsLiteralOnly ? amount.Literal : amount.PerUnit;
            int    magnitude = unit < 0 ? -unit : unit;
            int    literal   = amount.Literal < 0 ? -amount.Literal : amount.Literal;

            if (global.DenStartingHp > 0 && literal > global.DenStartingHp)
            {
                sink.Error(EffectStepsSheet, row, column,
                    $"{step.Op} step '{row}' moves {literal.ToString(CultureInfo.InvariantCulture)} in {column}, past the Den's whole {global.DenStartingHp.ToString(CultureInfo.InvariantCulture)} hit points.");
            }
            else if (global.StatQuantum > 1 && magnitude > 0 && magnitude < global.StatQuantum)
            {
                sink.Warning(EffectStepsSheet, row, column,
                    $"{step.Op} step '{row}' moves {magnitude.ToString(CultureInfo.InvariantCulture)} in {column}, below the stat quantum of {global.StatQuantum.ToString(CultureInfo.InvariantCulture)}. Authored in the old domain?");
            }
        }

        enum TargetClass
        {
            CritterOnly,
            CritterOrDen,
            Graveyard,
        }

        /// <summary> The per-op parameter rules, written once so each op reads as a list of what it takes. </summary>
        readonly struct StepChecker
        {
            readonly EffectStepInfo    _step;
            readonly IContentIssueSink _sink;

            public StepChecker(EffectStepInfo step, IContentIssueSink sink)
            {
                _step = step;
                _sink = sink;
            }

            string Row => _step.StepId?.Value;

            void Error(string column, string message) => _sink.Error(EffectStepsSheet, Row, column, $"{_step.Op} step '{Row}': {message}");

            public void RequireNoTarget()
            {
                if (_step.Target != EffectTargetKind.None)
                    Error(nameof(EffectStepInfo.Target), $"acts on the resolving seat and must have no Target, but has {_step.Target}.");
            }

            public void RequireTargetOf(TargetClass targetClass)
            {
                if (_step.Target == EffectTargetKind.None)
                {
                    Error(nameof(EffectStepInfo.Target), "requires a Target.");
                    return;
                }

                bool ok;
                string expected;
                switch (targetClass)
                {
                    case TargetClass.CritterOnly:
                        ok       = EffectVocabulary.TargetsCritters(_step.Target);
                        expected = "a critter target";
                        break;
                    case TargetClass.CritterOrDen:
                        ok       = EffectVocabulary.TargetsCritters(_step.Target) || EffectVocabulary.TargetsDen(_step.Target);
                        expected = "a critter or Den target";
                        break;
                    default:
                        ok       = EffectVocabulary.TargetsGraveyard(_step.Target);
                        expected = "OwnGraveyard or EnemyGraveyard";
                        break;
                }

                if (!ok)
                    Error(nameof(EffectStepInfo.Target), $"needs {expected}, but has {_step.Target}.");
            }

            public void RequireAmount(int minLiteral, bool literalOnly = false)
            {
                if (_step.Amount == null)
                {
                    Error(nameof(EffectStepInfo.Amount), "requires an Amount.");
                    return;
                }
                CheckAmount(nameof(EffectStepInfo.Amount), _step.Amount, minLiteral, literalOnly);
            }

            public void RequireAmount2(int minLiteral, bool literalOnly = false)
            {
                if (_step.Amount2 == null)
                {
                    Error(nameof(EffectStepInfo.Amount2), "requires an Amount2.");
                    return;
                }
                CheckAmount(nameof(EffectStepInfo.Amount2), _step.Amount2, minLiteral, literalOnly);
            }

            public void AllowOptionalAmount(int minLiteral, bool literalOnly = false)
            {
                if (_step.Amount != null)
                    CheckAmount(nameof(EffectStepInfo.Amount), _step.Amount, minLiteral, literalOnly);
            }

            void CheckAmount(string column, EffectAmount amount, int minLiteral, bool literalOnly)
            {
                if (amount.Literal < minLiteral)
                {
                    Error(column, minLiteral == 0
                        ? $"{column} must not be negative, but is {amount.Literal.ToString(CultureInfo.InvariantCulture)}. Only Buff steps take negative amounts."
                        : $"{column} must be at least {minLiteral.ToString(CultureInfo.InvariantCulture)}, but is {amount.Literal.ToString(CultureInfo.InvariantCulture)}.");
                }

                if (literalOnly && !amount.IsLiteralOnly)
                    Error(column, $"{column} must be a plain number, but counts {amount.Counter}.");
            }

            public void RequirePeekKeepFitsLook()
            {
                if (_step.Amount == null || _step.Amount2 == null)
                    return;

                if (_step.Amount2.Literal > _step.Amount.Literal)
                {
                    Error(nameof(EffectStepInfo.Amount2),
                        $"keeps {_step.Amount2.Literal.ToString(CultureInfo.InvariantCulture)} of the {_step.Amount.Literal.ToString(CultureInfo.InvariantCulture)} cards it looks at.");
                }
            }

            public void ForbidAmount()
            {
                if (_step.Amount != null)
                    Error(nameof(EffectStepInfo.Amount), "takes no Amount.");
            }

            public void ForbidAmount2()
            {
                if (_step.Amount2 != null)
                    Error(nameof(EffectStepInfo.Amount2), "takes no Amount2.");
            }

            public void RequireDuration()
            {
                if (_step.Duration == ManaDuration.None)
                    Error(nameof(EffectStepInfo.Duration), "requires a Duration of Turn or Permanent.");
            }

            public void ForbidDuration()
            {
                if (_step.Duration != ManaDuration.None)
                    Error(nameof(EffectStepInfo.Duration), $"takes no Duration, but has {_step.Duration}.");
            }

            public void RequireEngineFlagKeyword()
            {
                if (_step.Keyword == null)
                {
                    Error(nameof(EffectStepInfo.Keyword), "requires a Keyword to grant.");
                    return;
                }

                KeywordInfo keyword = _step.Keyword.MaybeRef;
                if (keyword != null && keyword.Kind != KeywordKind.EngineFlag)
                    Error(nameof(EffectStepInfo.Keyword), $"grants '{keyword.KeywordId}', which is a {keyword.Kind} rather than an engine flag. Trigger labels follow from bindings and cannot be granted.");
            }

            public void ForbidKeyword()
            {
                if (_step.Keyword != null)
                    Error(nameof(EffectStepInfo.Keyword), "takes no Keyword.");
            }

            public void RequireCritterCard()
            {
                if (_step.Card == null)
                {
                    Error(nameof(EffectStepInfo.Card), "requires a Card to summon.");
                    return;
                }

                CardInfo card = _step.Card.MaybeRef;
                if (card != null && card.Type != CardType.Critter)
                    Error(nameof(EffectStepInfo.Card), $"summons '{card.CardId}', which is a {card.Type}. Only critters can be summoned onto a board.");
            }

            public void ForbidCard()
            {
                if (_step.Card != null)
                    Error(nameof(EffectStepInfo.Card), "takes no Card.");
            }

            public void ForbidFilter()
            {
                if (_step.Filter != null)
                    Error(nameof(EffectStepInfo.Filter), "takes no Filter.");
            }

            public void RequireSelector()
            {
                if (_step.Selector == GraveyardSelector.None)
                    Error(nameof(EffectStepInfo.Selector), "requires a Selector, so that the choice is a pure function of public state.");
            }

            public void ForbidSelector()
            {
                if (_step.Selector != GraveyardSelector.None)
                    Error(nameof(EffectStepInfo.Selector), $"takes no Selector, but has {_step.Selector}.");
            }
        }

        #endregion

        #region Step usage in cards and Weathers

        /// <summary>
        /// A step's legality depends on where it is used: a critter-only step cannot be fed a chosen target
        /// that may be a Den, <c>Self</c> only means something on a critter (or on the critter a Weather's
        /// death hook is resolving for), and a Weather has nobody to choose a target.
        /// </summary>
        static void ValidateStepUsage(ContentPool pool, List<StepUsage> usages, IContentIssueSink sink)
        {
            foreach (StepUsage usage in usages)
            {
                EffectStepInfo step = usage.Step;

                if (step.Target == EffectTargetKind.Chosen)
                {
                    if (usage.Weather != null)
                    {
                        sink.Error(WeathersSheet, usage.Row, "Steps",
                            $"Weather '{usage.Row}' uses step '{step.StepId}', which targets Chosen. A Weather has nobody to choose a target.");
                    }
                    else if (EffectVocabulary.IsCritterOnly(step.Op) && EffectVocabulary.ChoiceAdmitsDen(usage.Card.ChooseTarget))
                    {
                        sink.Error(CardsSheet, usage.Row, nameof(CardInfo.ChooseTarget),
                            $"'{usage.Row}' chooses {usage.Card.ChooseTarget}, which may be a Den, but feeds it to {step.Op} step '{step.StepId}', which only accepts critters.");
                    }
                }

                if (step.Target == EffectTargetKind.Self)
                {
                    if (usage.Weather != null)
                    {
                        if (usage.Weather.Trigger != WeatherTrigger.CritterDies)
                        {
                            sink.Error(WeathersSheet, usage.Row, "Steps",
                                $"Weather '{usage.Row}' uses step '{step.StepId}', which targets Self, on a {usage.Weather.Trigger} hook. Self only addresses the dying critter, so only a CritterDies hook has one.");
                        }
                    }
                    else if (usage.Card.Type != CardType.Critter)
                    {
                        sink.Error(CardsSheet, usage.Row, usage.Column,
                            $"'{usage.Row}' is a {usage.Card.Type} and has no board presence, but uses step '{step.StepId}', which targets Self.");
                    }
                }
            }
        }

        #endregion

        #region Cards

        /// <summary>
        /// Every card is shape-legal for its type, declares a chosen target exactly when it uses one, and
        /// prints only keywords that are engine flags.
        /// </summary>
        static void ValidateCards(ContentPool pool, IContentIssueSink sink)
        {
            foreach (CardInfo card in pool.Cards)
            {
                string row = card.CardId?.Value;

                if (card.Cost < 0)
                    sink.Error(CardsSheet, row, nameof(CardInfo.Cost), $"Cost is {card.Cost.ToString(CultureInfo.InvariantCulture)}; a card cannot cost less than nothing.");

                if (card.Type == CardType.Critter)
                {
                    if (card.Attack < 0)
                        sink.Error(CardsSheet, row, nameof(CardInfo.Attack), $"Critter has Attack {card.Attack.ToString(CultureInfo.InvariantCulture)}; a critter cannot deal negative damage.");
                    if (card.Health < 1)
                        sink.Error(CardsSheet, row, nameof(CardInfo.Health), $"Critter has Health {card.Health.ToString(CultureInfo.InvariantCulture)}; a critter that enters play dead is not a card.");
                    if (card.IsSnack)
                        sink.Error(CardsSheet, row, nameof(CardInfo.IsSnack), "IsSnack is a trick subtype; a critter cannot be a Snack.");

                    CheckAgainstTheDomain(pool, sink, row, nameof(CardInfo.Attack), card.Attack, "attack");
                    CheckAgainstTheDomain(pool, sink, row, nameof(CardInfo.Health), card.Health, "health");
                }
                else
                {
                    if (card.Attack != 0 || card.Health != 0)
                        sink.Error(CardsSheet, row, nameof(CardInfo.Attack), "Trick has combat stats; only critters have Attack and Health.");
                    if (card.Keywords != null && card.Keywords.Count > 0)
                        sink.Error(CardsSheet, row, nameof(CardInfo.Keywords), "Trick prints keywords; the engine flags are all board rules, so only critters carry them.");

                    foreach (CardTrigger trigger in EffectVocabulary.AllCardTriggers)
                    {
                        if (trigger != CardTrigger.Hello && card.BindsTrigger(trigger))
                        {
                            sink.Error(CardsSheet, row, trigger.ToString(),
                                $"Trick binds {trigger}. A trick resolves on cast and goes to the graveyard, so it may bind Hello only.");
                        }
                    }

                    if (card.IsSnack && card.ChooseTarget != ChooseTargetKind.None && !EffectVocabulary.ChoiceIsFriendlyOnly(card.ChooseTarget))
                    {
                        sink.Error(CardsSheet, row, nameof(CardInfo.ChooseTarget),
                            $"Snack chooses {card.ChooseTarget}. Snacks are treats: they point at your own side.");
                    }
                }

                bool usesChosen = false;
                foreach (CardTrigger trigger in EffectVocabulary.AllCardTriggers)
                {
                    foreach (MetaRef<EffectStepInfo> stepRef in card.GetSteps(trigger))
                    {
                        if (stepRef?.MaybeRef != null && stepRef.Ref.Target == EffectTargetKind.Chosen)
                            usesChosen = true;
                    }
                }

                if (usesChosen && card.ChooseTarget == ChooseTargetKind.None)
                {
                    sink.Error(CardsSheet, row, nameof(CardInfo.ChooseTarget),
                        "A step targets Chosen but the card declares no ChooseTarget, so the player is never asked to pick one.");
                }
                else if (!usesChosen && card.ChooseTarget != ChooseTargetKind.None)
                {
                    sink.Error(CardsSheet, row, nameof(CardInfo.ChooseTarget),
                        $"Card declares ChooseTarget {card.ChooseTarget} but no step targets Chosen, so the pick would do nothing.");
                }

                if (card.Keywords != null)
                {
                    foreach (MetaRef<KeywordInfo> keywordRef in card.Keywords)
                    {
                        KeywordInfo keyword = keywordRef?.MaybeRef;
                        if (keyword != null && keyword.Kind != KeywordKind.EngineFlag)
                        {
                            sink.Error(CardsSheet, row, nameof(CardInfo.Keywords),
                                $"Card prints '{keyword.KeywordId}', which is a trigger label. The UI derives those labels from the card's bindings.");
                        }
                    }
                }

                if (!card.Collectible)
                {
                    if (card.InStarterCollection)
                    {
                        sink.Error(CardsSheet, row, nameof(CardInfo.InStarterCollection),
                            "Non-collectible card is in the starter collection. Tokens and the second-player bonus card never live in a collection.");
                    }

                    if (card.RankTrack?.MaybeRef != null && !card.RankTrack.Ref.TrackId.Equals(RankTrackId.None))
                    {
                        sink.Error(CardsSheet, row, nameof(CardInfo.RankTrack),
                            $"Non-collectible card uses rank track '{card.RankTrack.Ref.TrackId}'. A card nobody owns has no rank to grow, so it must use the None track.");
                    }
                }
            }
        }

        /// <summary>
        /// One authored stat against the domain it is written in. Health and hit points are counted in
        /// multiples of <see cref="GlobalConfig.StatQuantum"/>, and the two ways to get that wrong pull in
        /// opposite directions: a number above the Den's own hit points is a body or a blow the match cannot
        /// meaningfully contain, and a number below the quantum is almost always one that was authored in the
        /// old, coarse domain and never scaled. The first is an error because it breaks the match; the second
        /// is a warning, because a deliberately tiny number is a legitimate if unusual thing to author.
        /// </summary>
        static void CheckAgainstTheDomain(ContentPool pool, IContentIssueSink sink, string row, string column, int value, string what)
        {
            GlobalConfig global = pool.Global;
            if (global == null)
                return;

            if (global.DenStartingHp > 0 && value > global.DenStartingHp)
            {
                sink.Error(CardsSheet, row, column,
                    $"'{row}' has {what} {value.ToString(CultureInfo.InvariantCulture)}, above the Den's {global.DenStartingHp.ToString(CultureInfo.InvariantCulture)} hit points. Nothing in the match can answer it.");
            }
            else if (global.StatQuantum > 1 && value > 0 && value < global.StatQuantum)
            {
                sink.Warning(CardsSheet, row, column,
                    $"'{row}' has {what} {value.ToString(CultureInfo.InvariantCulture)}, below the stat quantum of {global.StatQuantum.ToString(CultureInfo.InvariantCulture)}. Authored in the old domain?");
            }
        }

        #endregion

        #region Rank tracks

        /// <summary>
        /// No track pushes any card that references it out of legal numbers, and a track that scales an
        /// effect is only used by cards that have an effect it can scale.
        /// </summary>
        static void ValidateRankTracks(ContentPool pool, IContentIssueSink sink)
        {
            foreach (CardInfo card in pool.Cards)
            {
                RankTrackInfo track = card.RankTrack?.MaybeRef;
                if (track == null)
                    continue;

                string row = card.CardId?.Value;

                for (int rank = RankTrackInfo.FirstGrowthRank; rank <= RankTrackInfo.LastGrowthRank; rank++)
                {
                    RankTrackStep deltas = track.GetCumulativeDeltas(rank);

                    int cost = card.Cost + deltas.CostDelta;
                    if (cost < 0)
                    {
                        sink.Error(RankTracksSheet, track.TrackId?.Value, "CostDelta",
                            $"Track takes '{row}' to cost {cost.ToString(CultureInfo.InvariantCulture)} at rank {rank.ToString(CultureInfo.InvariantCulture)}.");
                    }

                    if (card.Type == CardType.Critter)
                    {
                        int attack = card.Attack + deltas.AttackDelta;
                        if (attack < 0)
                        {
                            sink.Error(RankTracksSheet, track.TrackId?.Value, "AttackDelta",
                                $"Track takes '{row}' to {attack.ToString(CultureInfo.InvariantCulture)} attack at rank {rank.ToString(CultureInfo.InvariantCulture)}.");
                        }

                        int health = card.Health + deltas.HealthDelta;
                        if (health < 1)
                        {
                            sink.Error(RankTracksSheet, track.TrackId?.Value, "HealthDelta",
                                $"Track takes '{row}' to {health.ToString(CultureInfo.InvariantCulture)} health at rank {rank.ToString(CultureInfo.InvariantCulture)}.");
                        }
                    }
                }

                if (track.ScalesEffectAmount && !HasScalableHelloAmount(card))
                {
                    sink.Error(CardsSheet, row, nameof(CardInfo.RankTrack),
                        $"'{row}' uses rank track '{track.TrackId}', which scales an effect amount, but its first Hello step has no literal amount to scale.");
                }
            }
        }

        /// <summary> Whether the card's first Hello step carries a plain number a rank track can raise. </summary>
        static bool HasScalableHelloAmount(CardInfo card)
        {
            IReadOnlyList<MetaRef<EffectStepInfo>> hello = card.GetSteps(CardTrigger.Hello);
            if (hello.Count == 0)
                return false;

            EffectStepInfo first = hello[0]?.MaybeRef;
            return first?.Amount != null && first.Amount.IsLiteralOnly;
        }

        #endregion

        #region Weathers

        /// <summary> A Weather's hooks are internally consistent, and the pool is not empty. </summary>
        static void ValidateWeathers(ContentPool pool, IContentIssueSink sink)
        {
            if (pool.Weathers.Count == 0)
                sink.Error(WeathersSheet, null, null, "The Weather pool is empty; every match is played under a Weather.");

            foreach (WeatherInfo weather in pool.Weathers)
            {
                string row = weather.WeatherId?.Value;

                if (weather.HasCostRule && weather.CostDelta == 0)
                    sink.Error(WeathersSheet, row, nameof(WeatherInfo.CostDelta), $"CostScope is {weather.CostScope} but CostDelta is 0, so the rule changes nothing.");
                if (!weather.HasCostRule && weather.CostDelta != 0)
                    sink.Error(WeathersSheet, row, nameof(WeatherInfo.CostScope), $"CostDelta is {weather.CostDelta.ToString(CultureInfo.InvariantCulture)} but no CostScope says what it applies to.");

                bool hasSteps = weather.Steps != null && weather.Steps.Count > 0;
                if (weather.HasTrigger && !hasSteps)
                    sink.Error(WeathersSheet, row, "Steps", $"Trigger is {weather.Trigger} but the Weather binds no steps to it.");
                if (!weather.HasTrigger && hasSteps)
                    sink.Error(WeathersSheet, row, nameof(WeatherInfo.Trigger), "Weather binds steps but has no Trigger to fire them.");

                if (!weather.HasAura && !weather.HasCostRule && !weather.HasTrigger)
                    sink.Error(WeathersSheet, row, null, "Weather has no hooks at all, so the match would be played under nothing.");

                KeywordInfo aura = weather.AuraKeyword?.MaybeRef;
                if (aura != null && aura.Kind != KeywordKind.EngineFlag)
                    sink.Error(WeathersSheet, row, nameof(WeatherInfo.AuraKeyword), $"Aura keyword '{aura.KeywordId}' is a trigger label; an aura can only grant an engine flag.");
            }
        }

        #endregion

        #region Pool

        /// <summary>
        /// The singleton pool can actually build a legal deck: some allowed clan combination has enough
        /// distinct collectible cards, and the starter collection alone does too on day one.
        /// </summary>
        static void ValidatePool(ContentPool pool, IContentIssueSink sink)
        {
            // A nonsense deck size is its own error (ValidateGlobal); there is nothing to measure against here.
            if ((pool.Global?.DeckSize ?? 0) <= 0)
                return;

            CheckBuildable(pool, sink, card => card.Collectible, "the collectible pool", reportShortfalls: true);
            CheckBuildable(pool, sink, card => card.Collectible && card.InStarterCollection, "the starter collection", reportShortfalls: false);
        }

        /// <summary>
        /// Whether some allowed clan combination has enough distinct cards for a deck. Combinations that fall
        /// short are reported as warnings only for the full pool — the starter collection is a subset of it,
        /// so repeating them there would say the same thing twice.
        /// </summary>
        static void CheckBuildable(ContentPool pool, IContentIssueSink sink, Func<CardInfo, bool> include, string what, bool reportShortfalls)
        {
            int deckSize = pool.Global.DeckSize;

            List<ClanInfo> limitedClans = pool.Clans.Where(clan => clan.CountsTowardClanLimit).ToList();

            Dictionary<ClanId, int> perClan = new Dictionary<ClanId, int>();
            foreach (ClanInfo clan in limitedClans)
                perClan[clan.ClanId] = 0;

            int neutralCount = 0;
            foreach (CardInfo card in pool.Cards)
            {
                if (!include(card))
                    continue;

                ClanInfo clan = card.Clan?.MaybeRef;
                if (clan == null)
                    continue;

                if (!clan.CountsTowardClanLimit)
                    neutralCount++;
                else if (perClan.ContainsKey(clan.ClanId))
                    perClan[clan.ClanId]++;
            }

            int best = neutralCount;
            for (int i = 0; i < limitedClans.Count; i++)
            {
                int single = neutralCount + perClan[limitedClans[i].ClanId];
                if (single > best)
                    best = single;

                if (reportShortfalls && single < deckSize)
                {
                    sink.Warning(ClansSheet, limitedClans[i].ClanId?.Value, null,
                        $"A deck of only {limitedClans[i].ClanId} and Wanderers has {single.ToString(CultureInfo.InvariantCulture)} cards to pick from in {what}, short of the {deckSize.ToString(CultureInfo.InvariantCulture)} a legal deck needs. Expected while the pool is small.");
                }

                for (int j = i + 1; j < limitedClans.Count; j++)
                {
                    int pair = neutralCount + perClan[limitedClans[i].ClanId] + perClan[limitedClans[j].ClanId];
                    if (pair > best)
                        best = pair;

                    if (reportShortfalls && pair < deckSize)
                    {
                        sink.Warning(ClansSheet, limitedClans[i].ClanId?.Value, null,
                            $"{limitedClans[i].ClanId} + {limitedClans[j].ClanId} has {pair.ToString(CultureInfo.InvariantCulture)} cards to pick from in {what}, short of the {deckSize.ToString(CultureInfo.InvariantCulture)} a legal deck needs.");
                    }
                }
            }

            if (best < deckSize)
            {
                sink.Error(CardsSheet, null, null,
                    $"No allowed clan combination can build a legal deck out of {what}: the best has {best.ToString(CultureInfo.InvariantCulture)} distinct cards, and a deck needs {deckSize.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        #endregion

        #region Starter decks

        /// <summary>
        /// The starter decks are decks a brand-new account can actually play: legal by the shared validator,
        /// built only from cards the starter grant hands out, and between them covering every clan. They are
        /// checked here rather than only in a test because a broken starter deck is a fresh account that
        /// cannot play at all, and the config build is the last place that can still refuse it.
        /// <para>
        /// Every rule below is an error. A warning would hide among the mono-clan pool warnings, whose count
        /// is pinned precisely so a new one cannot.
        /// </para>
        /// </summary>
        static void ValidateStarterDecks(ContentPool pool, IContentIssueSink sink)
        {
            if (pool.StarterDecks == null || pool.StarterDecks.Count == 0)
            {
                sink.Error(StarterDecksSheet, null, null, "There are no starter decks; Home offers a starter deck to every account.");
                return;
            }

            // A nonsense deck size is its own error (ValidateGlobal), and every deck would report against it.
            if ((pool.Global?.DeckSize ?? 0) <= 0)
                return;

            Dictionary<CardId, CardInfo> cardsById = new Dictionary<CardId, CardInfo>();
            foreach (CardInfo card in pool.Cards)
            {
                if (card.CardId != null)
                    cardsById[card.CardId] = card;
            }

            Dictionary<ClanId, bool> countsTowardLimit = new Dictionary<ClanId, bool>();
            foreach (ClanInfo clan in pool.Clans)
            {
                if (clan.ClanId != null)
                    countsTowardLimit[clan.ClanId] = clan.CountsTowardClanLimit;
            }

            HashSet<ClanId> clansCovered = new HashSet<ClanId>();
            Dictionary<string, StarterDeckId> pairOwners = new Dictionary<string, StarterDeckId>();

            foreach (StarterDeckInfo deck in pool.StarterDecks)
            {
                string row = deck.StarterDeckId?.Value;

                if (string.IsNullOrEmpty(deck.DisplayName))
                    sink.Error(StarterDecksSheet, row, nameof(StarterDeckInfo.DisplayName), "Starter deck has no display name.");
                else if (deck.DisplayName.Length > PlayerDeck.MaxNameLength)
                {
                    sink.Error(StarterDecksSheet, row, nameof(StarterDeckInfo.DisplayName),
                        $"Display name is {deck.DisplayName.Length.ToString(CultureInfo.InvariantCulture)} characters; the picker is laid out for at most {PlayerDeck.MaxNameLength.ToString(CultureInfo.InvariantCulture)}.");
                }

                if (string.IsNullOrEmpty(deck.Description))
                    sink.Error(StarterDecksSheet, row, nameof(StarterDeckInfo.Description), "Starter deck has no description; the picker shows one line about every deck.");

                // The same shared definition of legal the deckbuilder and both entry actions run. A starter
                // deck is not a special kind of deck, so there is no second definition of legal for it.
                List<CardId>         cards    = deck.ToCardIds();
                DeckValidationResult legality = DeckValidator.Validate(cards, cardsById, pool.Global);
                if (!legality.IsValid)
                {
                    sink.Error(StarterDecksSheet, row, nameof(StarterDeckInfo.Cards),
                        $"Starter deck is not a legal deck: {legality}.{BlankPositionHint(cards, legality)}");
                    continue;
                }

                HashSet<ClanId> limitedClans = new HashSet<ClanId>();
                foreach (CardId cardId in cards)
                {
                    CardInfo card = cardsById[cardId];

                    if (!card.InStarterCollection)
                    {
                        sink.Error(StarterDecksSheet, row, nameof(StarterDeckInfo.Cards),
                            $"Starter deck names '{cardId}', which is not in the starter collection. A starter deck a new account cannot play is not a starter deck.");
                    }

                    ClanId clanId = card.Clan?.MaybeRef?.ClanId;
                    if (clanId != null && countsTowardLimit.TryGetValue(clanId, out bool counts) && counts)
                        limitedClans.Add(clanId);
                }

                // Deliberately stricter than DeckValidator, whose lower clan bound is absent on purpose: this
                // rule is about what a starter deck is for, not about what is legal.
                if (limitedClans.Count != pool.Global.MaxClansPerDeck)
                {
                    sink.Error(StarterDecksSheet, row, nameof(StarterDeckInfo.Cards),
                        $"Starter deck draws on {limitedClans.Count.ToString(CultureInfo.InvariantCulture)} clan-limited clans; a starter deck shows off exactly {pool.Global.MaxClansPerDeck.ToString(CultureInfo.InvariantCulture)}.");
                }

                foreach (ClanId clanId in limitedClans)
                    clansCovered.Add(clanId);

                string pairKey = string.Join("+", limitedClans.Select(clan => clan.Value).OrderBy(value => value, StringComparer.Ordinal));
                if (pairOwners.TryGetValue(pairKey, out StarterDeckId other))
                {
                    sink.Error(StarterDecksSheet, row, nameof(StarterDeckInfo.Cards),
                        $"Starter decks '{other}' and '{deck.StarterDeckId}' draw on the same two clans.");
                }
                else
                    pairOwners[pairKey] = deck.StarterDeckId;
            }

            foreach (ClanInfo clan in pool.Clans)
            {
                if (!clan.CountsTowardClanLimit || clan.ClanId == null)
                    continue;

                if (!clansCovered.Contains(clan.ClanId))
                {
                    sink.Error(ClansSheet, clan.ClanId.Value, null,
                        $"Clan '{clan.ClanId}' is in no starter deck, so a new account can never play it without building a deck first.");
                }
            }
        }

        /// <summary>
        /// Where the blanks are, when the deck failed as <c>UnknownCard</c> with no card to name. That is what
        /// an empty <c>Cards[]</c> cell inside a deck block produces — the vertical layout fills a skipped
        /// element with a default one rather than shortening the list — and the author is looking at a
        /// twenty-five-line block, so the position is the only part of the message that helps.
        /// </summary>
        static string BlankPositionHint(List<CardId> cards, DeckValidationResult legality)
        {
            if (legality.Error != DeckValidationError.UnknownCard || legality.OffendingCard != null)
                return "";

            List<string> blanks = new List<string>();
            for (int ndx = 0; ndx < cards.Count; ndx++)
            {
                if (cards[ndx] == null)
                    blanks.Add((ndx + 1).ToString(CultureInfo.InvariantCulture));
            }

            if (blanks.Count == 0)
                return "";

            return blanks.Count == 1
                ? $" Card {blanks[0]} of {cards.Count.ToString(CultureInfo.InvariantCulture)} is blank."
                : $" Cards {string.Join(", ", blanks)} of {cards.Count.ToString(CultureInfo.InvariantCulture)} are blank.";
        }

        #endregion

        #region Global

        /// <summary> The global tunables are mutually consistent and in range. </summary>
        static void ValidateGlobal(ContentPool pool, IContentIssueSink sink)
        {
            GlobalConfig global = pool.Global;
            if (global == null)
            {
                sink.Error(GlobalSheet, null, null, "The Global entry is missing.");
                return;
            }

            RequirePositive(sink, nameof(global.DenStartingHp), global.DenStartingHp);
            RequirePositive(sink, nameof(global.DeckSize), global.DeckSize);
            RequirePositive(sink, nameof(global.MaxClansPerDeck), global.MaxClansPerDeck);
            RequirePositive(sink, nameof(global.MaxBoardCritters), global.MaxBoardCritters);
            RequirePositive(sink, nameof(global.MaxHandSize), global.MaxHandSize);
            RequireNonNegative(sink, nameof(global.OpeningHandFirstPlayer), global.OpeningHandFirstPlayer);
            RequireNonNegative(sink, nameof(global.OpeningHandSecondPlayer), global.OpeningHandSecondPlayer);
            RequireNonNegative(sink, nameof(global.StartingMaxMana), global.StartingMaxMana);
            RequireNonNegative(sink, nameof(global.ManaGainPerTurn), global.ManaGainPerTurn);
            RequireNonNegative(sink, nameof(global.DrawsPerTurn), global.DrawsPerTurn);
            RequireNonNegative(sink, nameof(global.MulligansPerPlayer), global.MulligansPerPlayer);
            RequireNonNegative(sink, nameof(global.TuckeredOutFirstDamage), global.TuckeredOutFirstDamage);
            RequireNonNegative(sink, nameof(global.TuckeredOutIncrement), global.TuckeredOutIncrement);
            RequirePositive(sink, nameof(global.RankMin), global.RankMin);
            RequireNonNegative(sink, nameof(global.InitialLockSlots), global.InitialLockSlots);
            RequirePositive(sink, nameof(global.StatQuantum), global.StatQuantum);
            RequireNonNegative(sink, nameof(global.MatchesPerCardGift), global.MatchesPerCardGift);
            RequirePositive(sink, nameof(global.PowerScoreGapThreshold), global.PowerScoreGapThreshold);
            RequireNonNegative(sink, nameof(global.NewcomerShieldMatches), global.NewcomerShieldMatches);
            RequirePositive(sink, nameof(global.InitialRating), global.InitialRating);
            RequirePositive(sink, nameof(global.RatingKFactor), global.RatingKFactor);

            if (global.OpeningHandFirstPlayer > global.MaxHandSize || global.OpeningHandSecondPlayer > global.MaxHandSize)
            {
                sink.Error(GlobalSheet, nameof(global.MaxHandSize), null,
                    $"An opening hand of {global.MaxOpeningHand.ToString(CultureInfo.InvariantCulture)} does not fit the hand limit of {global.MaxHandSize.ToString(CultureInfo.InvariantCulture)}.");
            }

            // The second seat's hand also has to fit the compensation card, which the mulligan's resolution
            // grants on top of the dealt hand. It is granted rather than drawn, so the rule "a full hand sends
            // the card to the bottom of the deck" does not apply to it — a seat cannot be compensated into
            // its own deck — which makes the hand limit a *content* rule here rather than a runtime one.
            if (global.SecondPlayerBonusCard?.MaybeRef != null && global.OpeningHandSecondPlayer + 1 > global.MaxHandSize)
            {
                sink.Error(GlobalSheet, nameof(global.MaxHandSize), null,
                    $"The second seat's opening hand of {global.OpeningHandSecondPlayer.ToString(CultureInfo.InvariantCulture)} plus its compensation card does not fit the hand limit of {global.MaxHandSize.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (global.DeckSize <= global.MaxOpeningHand)
            {
                sink.Error(GlobalSheet, nameof(global.DeckSize), null,
                    $"A deck of {global.DeckSize.ToString(CultureInfo.InvariantCulture)} does not cover an opening hand of {global.MaxOpeningHand.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (global.RankMin > global.RankMax)
            {
                sink.Error(GlobalSheet, nameof(global.RankMax), null,
                    $"RankMax {global.RankMax.ToString(CultureInfo.InvariantCulture)} is below RankMin {global.RankMin.ToString(CultureInfo.InvariantCulture)}.");
            }

            // A threshold outside the rank range is a knob that lies: below the floor nothing is protected from
            // locking, and above the ceiling no card in the game can ever be locked.
            if (global.MinLockRank < global.RankMin || global.MinLockRank > global.RankMax)
            {
                sink.Error(GlobalSheet, nameof(global.MinLockRank), null,
                    $"MinLockRank {global.MinLockRank.ToString(CultureInfo.InvariantCulture)} is outside the rank range {global.RankMin.ToString(CultureInfo.InvariantCulture)}..{global.RankMax.ToString(CultureInfo.InvariantCulture)}.");
            }

            // A threshold no two decks can straddle is a knob that lies the other way round from MinLockRank:
            // every match would be Even and the asymmetric tiers would be unreachable content. The widest gap
            // the game can produce is a deck of every card at the ceiling against one of every card at the
            // floor (Docs/game-design.md, "Ranks").
            int widestPossibleGap = global.DeckSize * (global.RankMax - global.RankMin);
            if (global.PowerScoreGapThreshold >= widestPossibleGap)
            {
                sink.Error(GlobalSheet, nameof(global.PowerScoreGapThreshold), null,
                    $"PowerScoreGapThreshold {global.PowerScoreGapThreshold.ToString(CultureInfo.InvariantCulture)} is at or above the widest Power Score gap two legal decks can have ({widestPossibleGap.ToString(CultureInfo.InvariantCulture)}), so no match could ever be anything but Even.");
            }

            CardInfo bonus = global.SecondPlayerBonusCard?.MaybeRef;
            if (bonus == null)
                sink.Error(GlobalSheet, nameof(global.SecondPlayerBonusCard), null, "No second-player compensation card is named.");
            else
            {
                if (bonus.Collectible)
                {
                    sink.Error(GlobalSheet, nameof(global.SecondPlayerBonusCard), null,
                        $"'{bonus.CardId}' is collectible. The compensation card is granted at the deal, so it must not be buildable into a deck.");
                }
                if (bonus.Type != CardType.Trick)
                {
                    sink.Error(GlobalSheet, nameof(global.SecondPlayerBonusCard), null,
                        $"'{bonus.CardId}' is a {bonus.Type}. The compensation card is cast from hand, so it must be a trick.");
                }
            }
        }

        static void RequirePositive(IContentIssueSink sink, string member, int value)
        {
            if (value < 1)
                sink.Error(GlobalSheet, member, null, $"{member} is {value.ToString(CultureInfo.InvariantCulture)}; it must be at least 1.");
        }

        static void RequireNonNegative(IContentIssueSink sink, string member, int value)
        {
            if (value < 0)
                sink.Error(GlobalSheet, member, null, $"{member} is {value.ToString(CultureInfo.InvariantCulture)}; it must not be negative.");
        }

        #endregion

        #region Dead rows

        /// <summary> Rows nothing references, and names that collide. Warnings: they are content smells, not errors. </summary>
        static void ValidateDeadRows(ContentPool pool, List<StepUsage> usages, IContentIssueSink sink)
        {
            HashSet<EffectStepId> usedSteps = new HashSet<EffectStepId>();
            foreach (StepUsage usage in usages)
                usedSteps.Add(usage.Step.StepId);

            foreach (EffectStepInfo step in pool.EffectSteps)
            {
                if (!usedSteps.Contains(step.StepId))
                    sink.Warning(EffectStepsSheet, step.StepId?.Value, null, "No card and no Weather uses this step.");
            }

            HashSet<RankTrackId> usedTracks = new HashSet<RankTrackId>();
            foreach (CardInfo card in pool.Cards)
            {
                if (card.RankTrack?.MaybeRef != null)
                    usedTracks.Add(card.RankTrack.Ref.TrackId);
            }

            foreach (RankTrackInfo track in pool.RankTracks)
            {
                if (!usedTracks.Contains(track.TrackId))
                    sink.Warning(RankTracksSheet, track.TrackId?.Value, null, "No card uses this rank track.");
            }

            WarnOnDuplicateNames(sink, CardsSheet, pool.Cards.Select(card => (card.CardId?.Value, card.DisplayName)));
            WarnOnDuplicateNames(sink, ClansSheet, pool.Clans.Select(clan => (clan.ClanId?.Value, clan.DisplayName)));
            WarnOnDuplicateNames(sink, WeathersSheet, pool.Weathers.Select(weather => (weather.WeatherId?.Value, weather.DisplayName)));
        }

        static void WarnOnDuplicateNames(IContentIssueSink sink, string sheet, IEnumerable<(string Row, string DisplayName)> rows)
        {
            Dictionary<string, string> seen = new Dictionary<string, string>();
            foreach ((string row, string displayName) in rows)
            {
                if (string.IsNullOrEmpty(displayName))
                    continue;

                if (seen.TryGetValue(displayName, out string other))
                    sink.Warning(sheet, row, nameof(CardInfo.DisplayName), $"Display name '{displayName}' is also used by '{other}'.");
                else
                    seen.Add(displayName, row);
            }
        }

        #endregion
    }
}
