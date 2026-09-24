using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// Thrown when the game's config validation finds errors. No archive is written.
    /// <para>
    /// It derives from <see cref="GameConfigBuildFailed"/> so that it carries a structured build report. The
    /// LiveOps Dashboard shows each message against its library and row, and the offline build tool prints the
    /// same report. A plain exception would reach both as unstructured text.
    /// </para>
    /// </summary>
    public class GameConfigValidationFailed : GameConfigBuildFailed
    {
        /// <summary>Every error found, not only the first one.</summary>
        public IReadOnlyList<string> Errors { get; }

        public GameConfigValidationFailed(IReadOnlyList<string> errors, GameConfigValidationResult result)
            : base(new GameConfigBuildReport(Array.Empty<GameConfigBuildMessage>(), new List<GameConfigValidationResult> { result }))
        {
            Errors = errors;
        }

        public override string Message =>
            $"Game config validation failed with {Errors.Count} error(s):{string.Concat(Errors.Select(error => "\n  - " + error))}";
    }

    /// <summary>
    /// The game's config checks, run as part of every config build.
    /// <para>
    /// To check a library item, implement <see cref="IValidatedConfigItem"/> on the item type. Libraries are
    /// discovered by reflection. Checks that span several libraries go in the cross-library methods of this class,
    /// such as <see cref="ValidateGlobal"/>. Errors go to the SDK's <see cref="GameConfigValidationResult"/>, which
    /// the SDK treats as a report, not a failure, so any error also throws <see cref="GameConfigValidationFailed"/>.
    /// </para>
    /// </summary>
    public static class GameConfigValidation
    {
        /// <summary>
        /// Validates the whole shared config. Throws <see cref="GameConfigValidationFailed"/> if any check fails.
        /// </summary>
        public static void Validate(SharedGameConfig config, GameConfigValidationResult result) =>
            Validate(config, result, walkEveryGeneratedName: true);

        /// <summary>
        /// Runs the same checks as <see cref="Validate(SharedGameConfig, GameConfigValidationResult)"/>, with
        /// the check of every generated name made optional.
        /// <para>
        /// That check builds every name the vocabulary can produce (adjectives × nouns × suffixes, at least
        /// <see cref="DisplayNameGenerator.MinCombinationCount"/>) and validates each one, which dominates the
        /// cost. Tests that validate many configs pass false to skip it.
        /// </para>
        /// </summary>
        internal static void Validate(SharedGameConfig config, GameConfigValidationResult result, bool walkEveryGeneratedName)
        {
            ConfigValidationRun run = new ConfigValidationRun(result);

            // Per-item checks for every library whose item type implements IValidatedConfigItem. The SDK's
            // libraries (languages, player segments, offers, offer groups) are validated by SharedGameConfigBase
            // and their items do not implement the interface. InAppProducts is an exception: the SDK checks its
            // own columns, and DemoInAppProductInfo implements the interface to check the game's columns.
            ValidateEveryLibrary(config, run);

            // Cross-library checks.
            ValidatePlayerSegments(config, run);
            ValidateMissionSets(config, run);
            ValidateCosmetics(config, run);
            ValidateGlobal(config, run);
            ValidatePlayerIdentity(config, run, walkEveryGeneratedName);
            ValidateOffers(config, run);
            ValidateOfferGroups(config, run);
            ValidateWeeklyEventTemplates(config, run);
            ValidateEarningRoutes(config, run);
            ValidateBots(config, run);

            if (run.Errors.Count > 0)
                throw new GameConfigValidationFailed(run.Errors, result);
        }

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> if the config is missing entries or values the game
        /// reads. Runs at import, on both the server and the client.
        /// <para>
        /// Every config entry is optional, so an archive built before an entry existed still imports, and the
        /// first feature that reads the entry would then fail on null during a player session. Failing the load
        /// instead names the missing entries and how to rebuild the archive.
        /// </para>
        /// </summary>
        public static void ThrowIfArchiveIsOutdated(SharedGameConfig config)
        {
            List<string> missing = new List<string>();
            GlobalConfig global  = config.Global;

            // The Shop's demo purchases read InAppProducts. Without it the shop shows no demo purchase and
            // reports no error.
            if (config.InAppProducts == null || config.InAppProducts.Count == 0)
                missing.Add("InAppProducts");

            // Offer targeting reads PlayerSegments. Without it no player is in any segment, and nothing reports
            // an error (docs/offers.md, "Player segments").
            if (config.PlayerSegments == null || config.PlayerSegments.Count == 0)
                missing.Add("PlayerSegments");

            // The wardrobe resolves every owned and equipped cosmetic id against Cosmetics. Without it the
            // player's cosmetics do not resolve and the grid is empty, with no error (docs/cosmetics.md).
            if (config.Cosmetics == null || config.Cosmetics.Count == 0)
                missing.Add("Cosmetics");

            // The Shop's Featured slot and catalogue read Offers and OfferGroups. Without them no offer is shown
            // to anyone, with no error (docs/offers.md).
            if (config.Offers == null || config.Offers.Count == 0)
                missing.Add("Offers");
            if (config.OfferGroups == null || config.OfferGroups.Count == 0)
                missing.Add("OfferGroups");

            if (global == null)
            {
                missing.Add("Global");
            }
            else
            {
                // The wallet reads these values. A default GlobalConfig has a null starting wallet and caps of
                // zero, which would give a new player nothing and let them hold nothing.
                if (global.StartingWallet == null)             missing.Add(nameof(GlobalConfig.StartingWallet));
                if (global.MaxCoins <= 0)                      missing.Add(nameof(GlobalConfig.MaxCoins));
                if (global.MaxGems <= 0)                       missing.Add(nameof(GlobalConfig.MaxGems));
                if (global.MaxSpinTokens <= 0)                 missing.Add(nameof(GlobalConfig.MaxSpinTokens));

                // The daily reward reads DailyResetSchedule to decide whether today's claim is open. A default
                // GlobalConfig has none, and no daily reward would ever be available.
                if (global.DailyResetSchedule == null)         missing.Add(nameof(GlobalConfig.DailyResetSchedule));

                if (global.ActiveDailyRewardTable == null)      missing.Add(nameof(GlobalConfig.ActiveDailyRewardTable));
                if (global.ActiveFirstWeekSchedule == null)     missing.Add(nameof(GlobalConfig.ActiveFirstWeekSchedule));
                if (global.ActiveWheelTable == null)            missing.Add(nameof(GlobalConfig.ActiveWheelTable));
                if (global.ActiveDailyMissionSet == null)       missing.Add(nameof(GlobalConfig.ActiveDailyMissionSet));
                if (global.ActiveWeeklyMissionSet == null)      missing.Add(nameof(GlobalConfig.ActiveWeeklyMissionSet));
                if (global.ActiveTournamentRewardTable == null) missing.Add(nameof(GlobalConfig.ActiveTournamentRewardTable));
            }

            // Without a usable PlayerIdentity, DisplayNameGenerator gives every new player a fallback name
            // (DisplayNameGenerator.FallbackPrefix) instead of failing, and nothing reports an error.
            PlayerIdentityConfig identity = config.PlayerIdentity;
            if (identity == null || !identity.CanGenerate)
                missing.Add(nameof(SharedGameConfig.PlayerIdentity));

            // Every table reads BotNames and BotProfiles as it forms. Without profiles every bot plays at full
            // strength. Bot names are drawn without replacement, so the roster needs at least one name per seat.
            // The config build checks the same things in ValidateBots, and this check also covers an archive the
            // config build did not produce (docs/bots.md).
            if (config.BotNames == null || config.BotNames.Count < MatchRules.NumSeats)
                missing.Add(nameof(SharedGameConfig.BotNames));
            if (config.BotProfiles == null || config.BotProfiles.Count == 0)
                missing.Add(nameof(SharedGameConfig.BotProfiles));

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"This game config archive has no {string.Join(", ", missing)}, so it predates the config entries the game reads. "
                    + "Rebuild it with 'dotnet run --project tools/GameConfigGen' and publish the result.");
            }
        }

        /// <summary>
        /// Calls <see cref="IValidatedConfigItem.Validate"/> on every library item that implements the interface.
        /// <para>
        /// Libraries are discovered with the SDK's own <see cref="GameConfigBase.GetConfigEntries"/>, which finds
        /// every <c>[GameConfigEntry]</c> the config repository does, so a new library's checks cannot be left unrun.
        /// Each item is tested on its own, so a library typed to a base class still has its derived items checked.
        /// Errors name the entry by its sheet name (<see cref="GameConfigEntryInfo.EntryName"/>).
        /// </para>
        /// </summary>
        static void ValidateEveryLibrary(SharedGameConfig config, ConfigValidationRun run)
        {
            foreach ((GameConfigEntryInfo entryInfo, IGameConfigEntry entry) in config.GetConfigEntries())
            {
                if (entry is not IGameConfigLibrary library)
                    continue;

                foreach ((object key, IGameConfigData item) in library.EnumerateAll())
                {
                    if (item is IValidatedConfigItem validated)
                        validated.Validate(run.For(entryInfo.EntryName, key));
                }
            }
        }

        /// <summary>
        /// Validates player segments (<c>docs/offers.md</c>, "Player segments"). The SDK already refuses cycles
        /// between segments and ranges whose minimum is above the maximum. These checks cover mistakes the SDK
        /// accepts without error.
        /// <para>
        /// A segment needs a name and a description so an operator can audit who it targets. A segment with no
        /// condition matches every player. A property listed twice is redundant or contradictory, and the SDK
        /// combines both with AND. A balance threshold above the currency's cap matches no player.
        /// </para>
        /// </summary>
        static void ValidatePlayerSegments(SharedGameConfig config, ConfigValidationRun run)
        {
            if (config.PlayerSegments == null)
                return;

            foreach (DefaultPlayerSegmentInfo segment in config.PlayerSegments.Values)
            {
                ConfigItemValidation validation = run.For("PlayerSegments", segment.ConfigKey);

                validation.Require(!string.IsNullOrWhiteSpace(segment.DisplayName), "has no display name for the Dashboard to show", nameof(PlayerSegmentInfoBase.DisplayName));
                validation.Require(!string.IsNullOrWhiteSpace(segment.Description), "has no description saying who is in it", nameof(PlayerSegmentInfoBase.Description));

                if (segment.PlayerCondition is not PlayerSegmentBasicCondition condition)
                {
                    // The sample allows only PlayerSegmentBasicCondition. A custom condition class is written in
                    // code, so it cannot be changed with a config publish, and the Dashboard can only show its
                    // serialized form.
                    validation.Error($"uses a {segment.PlayerCondition.GetType().Name} condition; the sample authors range conditions only", nameof(PlayerSegmentInfoBase.PlayerCondition));
                    continue;
                }

                List<PlayerPropertyRequirement> requirements = condition.PropertyRequirements ?? new List<PlayerPropertyRequirement>();
                int numSegmentReferences = (condition.RequireAnySegment?.Count ?? 0) + (condition.RequireAllSegments?.Count ?? 0);

                if (requirements.Count == 0 && numSegmentReferences == 0)
                {
                    validation.Error("has no conditions, so it matches every player", nameof(PlayerSegmentInfoBase.PlayerCondition));
                    continue;
                }

                // Duplicates are detected by display name rather than by property type, because a parameterized
                // property with different parameters is a different condition. For example, the SDK's
                // playerbase-subset property includes its bucket count and hash modifier in its display name, and
                // an A/B split uses two of them.
                HashSet<string> seen = new HashSet<string>();
                foreach (PlayerPropertyRequirement requirement in requirements)
                {
                    if (requirement?.Id == null)
                    {
                        validation.Error("has a requirement on no property at all", nameof(PlayerSegmentInfoBase.PlayerCondition));
                        continue;
                    }

                    if (!seen.Add(requirement.Id.DisplayName))
                        validation.Error($"conditions on {requirement.Id.DisplayName} twice", nameof(PlayerSegmentInfoBase.PlayerCondition));

                    RequireReachableBalance(validation, config.Global, requirement);
                }
            }
        }

        /// <summary>
        /// Reports a segment requirement whose minimum currency balance is above that currency's cap in
        /// <see cref="GlobalConfig"/>, because no player could match it.
        /// </summary>
        static void RequireReachableBalance(ConfigItemValidation validation, GlobalConfig global, PlayerPropertyRequirement requirement)
        {
            // ValidateGlobal reports a missing Global in the same pass, so the build still fails.
            if (global == null || requirement.Min?.ConstantValue is not long minimum)
                return;

            CurrencyType? currency = requirement.Id switch
            {
                PlayerPropertyCoins      => CurrencyType.Coins,
                PlayerPropertyGems       => CurrencyType.Gems,
                PlayerPropertySpinTokens => CurrencyType.SpinTokens,
                _                        => null,
            };

            if (currency == null)
                return;

            int cap = global.CapOf(currency.Value);
            if (cap > 0 && minimum > cap)
                validation.Error($"wants at least {minimum} {currency}, above the cap of {cap}", nameof(PlayerSegmentInfoBase.PlayerCondition));
        }

        /// <summary>
        /// Validates offer groups: every segment named in <see cref="MetaActivableParams.Segments"/> exists, and
        /// no two groups on the same placement share a priority. The SDK's offer resolution needs a unique
        /// priority per placement, and the SDK's own config checks do not enforce it.
        /// </summary>
        static void ValidateOfferGroups(SharedGameConfig config, ConfigValidationRun run)
        {
            if (config.OfferGroups == null)
                return;

            HashSet<(OfferPlacementId Placement, int Priority)> placementPriorities = new HashSet<(OfferPlacementId, int)>();

            foreach (OfferGroupInfo group in config.OfferGroups.Values)
            {
                ConfigItemValidation validation = run.For("OfferGroups", group.ConfigKey);

                foreach (MetaRef<PlayerSegmentInfoBase> segment in group.ActivableParams?.Segments ?? new List<MetaRef<PlayerSegmentInfoBase>>())
                    RequirePointer(validation, segment, config.PlayerSegments, nameof(MetaActivableParams.Segments));

                if (!placementPriorities.Add((group.Placement, group.Priority)))
                    validation.Error($"shares priority {group.Priority} on placement '{group.Placement}' with another offer group", nameof(MetaOfferGroupInfoBase.Priority));
            }
        }

        /// <summary>
        /// Validates offers: the contents are a valid reward, a wallet-priced offer costs no more than the
        /// currency's cap and has a purchase limit, and a demo-IAP offer can be bought once per player.
        /// </summary>
        static void ValidateOffers(SharedGameConfig config, ConfigValidationRun run)
        {
            if (config.Offers == null || config.Global == null)
                return;

            foreach (OfferInfo offer in config.Offers.Values)
            {
                ConfigItemValidation validation = run.For("Offers", offer.ConfigKey);

                // The offer contents get the same checks as every other reward (RewardBundle.Validate).
                if (offer.Contents == null)
                    validation.Error("grants nothing", nameof(OfferInfo.Contents));
                else
                    offer.Contents.Validate(validation, nameof(OfferInfo.Contents));

                if (offer.HasInGameCurrencyCost)
                {
                    int cap = config.Global.CapOf(offer.PriceCurrency!.Value);
                    if (cap > 0)
                        validation.RequireAtMost(offer.PriceAmount!.Value, cap, nameof(OfferInfo.PriceAmount));

                    // A wallet-priced offer needs a per-player or per-activation purchase limit. An offer with
                    // neither can be bought without limit, which usually means a cell was left blank by mistake.
                    validation.Require(
                        offer.MaxPurchasesPerPlayer.HasValue || offer.MaxPurchasesPerActivation.HasValue,
                        "is a wallet-priced offer with no purchase limit at all, per player or per activation",
                        nameof(MetaOfferInfoBase.MaxPurchasesPerPlayer));
                }
                else
                {
                    // Demo-IAP bundles are large, so the offers design limits each one to a single purchase per
                    // player.
                    validation.Require(offer.MaxPurchasesPerPlayer == 1,
                        "is a demo-IAP offer whose per-player purchase limit is not exactly one", nameof(MetaOfferInfoBase.MaxPurchasesPerPlayer));
                }
            }
        }

        /// <summary>
        /// Validates that the library has at least one weekly-event template and that no template's reward
        /// exceeds a wallet cap in <see cref="GlobalConfig"/>.
        /// <para>
        /// Every template is checked, because the library has no active pointer. Weekly events are created as
        /// LiveOps events, and any template can be picked for the next seeded week (<c>docs/weekly-event.md</c>).
        /// </para>
        /// </summary>
        static void ValidateWeeklyEventTemplates(SharedGameConfig config, ConfigValidationRun run)
        {
            if (config.WeeklyEventTemplates == null || config.Global == null)
                return;

            // Every weekly event is created from a template, so with no templates no weekly event is ever
            // created (docs/weekly-event.md). The client then shows its normal empty state, so only the config
            // build can catch this.
            if (config.WeeklyEventTemplates.Count == 0)
            {
                run.For("WeeklyEventTemplates", "(library)")
                    .Error("holds no templates, so no weekly event can ever be created and every player sees the empty state");
                return;
            }

            foreach (WeeklyEventTemplateInfo template in config.WeeklyEventTemplates.Values)
            {
                foreach ((CurrencyAmount amount, int cap) in OverCap(config.Global, template?.Content?.Reward))
                {
                    run.For("WeeklyEventTemplates", template.ConfigKey)
                        .Error($"pays {amount.Amount} {amount.Currency} against a cap of {cap}", nameof(WeeklyEventTemplateInfo.Content));
                }
            }
        }

        /// <summary>
        /// Validates that every currency used in a price can be earned from the Events hub
        /// (<c>docs/economy.md</c>, "Config build checks"). The shop and the wardrobe send a player who cannot
        /// afford an item to the Events hub to earn the currency.
        /// <para>
        /// The wheel's and the daily cycle's own validation already require them to pay coins, gems and spin
        /// tokens, so this check fails only if those rules change or a currency is added. It makes the shop's
        /// dependency on the Events hub explicit.
        /// </para>
        /// </summary>
        static void ValidateEarningRoutes(SharedGameConfig config, ConfigValidationRun run)
        {
            if (config.Global == null)
                return;

            HashSet<CurrencyType> earnable = EarnableFromTheEventsHub(config);

            foreach ((CurrencyType currency, string library, string itemKey) in FirstPriceRowPerCurrency(config))
            {
                // Report the error against the priced row, because a missing reward source has no single row.
                ConfigItemValidation validation = run.For(library, itemKey);
                validation.Require(
                    earnable.Contains(currency),
                    $"is priced in {currency}, which nothing permanently on the Events hub grants, so a player short of it has nowhere to be sent");
            }
        }

        /// <summary>
        /// Returns the first priced row for each currency used in a price, across wallet-priced offers and
        /// purchasable cosmetics. One row per currency keeps a missing reward source from being reported once
        /// per priced row.
        /// </summary>
        static IEnumerable<(CurrencyType Currency, string Library, string ItemKey)> FirstPriceRowPerCurrency(SharedGameConfig config)
        {
            HashSet<CurrencyType> seen = new HashSet<CurrencyType>();

            if (config.Offers != null)
            {
                foreach (OfferInfo offer in config.Offers.Values)
                {
                    if (offer.HasInGameCurrencyCost && offer.PriceAmount > 0 && seen.Add(offer.PriceCurrency!.Value))
                        yield return (offer.PriceCurrency!.Value, "Offers", offer.OfferId.ToString());
                }
            }

            if (config.Cosmetics != null)
            {
                foreach (CosmeticInfo cosmetic in config.Cosmetics.Values)
                {
                    if (cosmetic.IsPurchasable && cosmetic.Price != null && cosmetic.Price.Amount > 0 && seen.Add(cosmetic.Price.Currency))
                        yield return (cosmetic.Price.Currency, "Cosmetics", cosmetic.Id.ToString());
                }
            }
        }

        /// <summary>
        /// The currencies that every player can always earn from the Events hub: the active daily reward table,
        /// the active wheel table and the active daily and weekly mission sets.
        /// <para>
        /// The first-week event, the weekly event and the tournament are not counted, because they are not
        /// available to every player at all times. The first-week event ends after its last day, a weekly-event
        /// template may not be the one picked for the current week, and the tournament is not on the Events hub.
        /// </para>
        /// </summary>
        static HashSet<CurrencyType> EarnableFromTheEventsHub(SharedGameConfig config)
        {
            HashSet<CurrencyType> earnable = new HashSet<CurrencyType>();

            void AddEarnableCurrencies(RewardBundle reward)
            {
                if (reward?.Amounts == null)
                    return;

                foreach (CurrencyAmount amount in reward.Amounts)
                {
                    if (amount.Amount > 0)
                        earnable.Add(amount.Currency);
                }
            }

            DailyRewardTableInfo dailyRewards = ConfigRefs.Resolve(config.Global.ActiveDailyRewardTable, config.DailyRewards);
            if (dailyRewards?.Steps != null)
            {
                foreach (DailyRewardStepInfo step in dailyRewards.Steps)
                    AddEarnableCurrencies(step?.Reward);
            }

            WheelTableInfo wheel = ConfigRefs.Resolve(config.Global.ActiveWheelTable, config.WheelTables);
            if (wheel?.Sectors != null)
            {
                foreach (WheelSectorInfo sector in wheel.Sectors)
                    AddEarnableCurrencies(sector?.Reward);
            }

            foreach (MetaRef<MissionSetInfo> setReference in new MetaRef<MissionSetInfo>[] { config.Global.ActiveDailyMissionSet, config.Global.ActiveWeeklyMissionSet })
            {
                MissionSetInfo set = ConfigRefs.Resolve(setReference, config.MissionSets);
                if (set?.Missions == null)
                    continue;

                foreach (MetaRef<MissionInfo> missionReference in set.Missions)
                    AddEarnableCurrencies(ConfigRefs.Resolve(missionReference, config.Missions)?.Reward);
            }

            return earnable;
        }

        /// <summary>
        /// Validates the <c>BotProfiles</c> and <c>BotNames</c> libraries as a whole (<c>docs/bots.md</c>).
        /// <para>
        /// These failures produce a table that looks normal at run time, so only the config build can catch
        /// them. With no profiles, every bot seat falls back to the strongest profile. With one profile, every
        /// bot at every table plays the same way.
        /// </para>
        /// </summary>
        static void ValidateBots(SharedGameConfig config, ConfigValidationRun run)
        {
            ConfigItemValidation profilesValidation = run.For("BotProfiles", "(library)");

            // Require at least two profiles so bots at a table can differ in strength.
            if (config.BotProfiles == null || config.BotProfiles.Count == 0)
                profilesValidation.Error("holds no strengths, so every computer player would silently play at full strength");
            else if (config.BotProfiles.Count < 2)
                profilesValidation.Error("holds one strength, so every seat at every table draws the same opponent");

            ConfigItemValidation namesValidation = run.For("BotNames", "(library)");

            // A table draws bot names without replacement, so the roster needs at least one name per seat.
            if (config.BotNames == null)
            {
                namesValidation.Error("is missing, so there is nothing to name a computer player");
                return;
            }

            namesValidation.Require(
                config.BotNames.Count >= MatchRules.NumSeats,
                $"holds {config.BotNames.Count} names for a table of {MatchRules.NumSeats} seats, which a draw without replacement cannot fill",
                "(count)");

            // Two rows with different ids but the same name could both be drawn for one table, which would
            // show the same name twice. Names are compared after DisplayNamePolicy.ToComparisonKey, the same form the
            // name reservation uses.
            Dictionary<string, BotNameInfo> byComparisonKey = new Dictionary<string, BotNameInfo>();
            foreach (BotNameInfo name in config.BotNames.Values)
            {
                if (name?.Name == null)
                    continue;

                string normalized = DisplayNamePolicy.ToComparisonKey(name.Name);
                if (normalized.Length == 0)
                    continue;

                if (byComparisonKey.TryGetValue(normalized, out BotNameInfo first))
                {
                    run.For("BotNames", name.ConfigKey)
                        .Error($"is the same name as '{first.Name}' once case and punctuation are folded away, so a table could seat both", nameof(BotNameInfo.Name));
                }
                else
                {
                    byComparisonKey.Add(normalized, name);
                }
            }
        }

        /// <summary>
        /// Validates each mission set as a whole: every mission has the set's cadence, no two missions have the
        /// same objective and target, and the total reward of a full set is within the ranges the economy is
        /// balanced against (<see cref="MissionSetInfo"/> constants).
        /// </summary>
        static void ValidateMissionSets(SharedGameConfig config, ConfigValidationRun run)
        {
            foreach (MissionSetInfo set in config.MissionSets.Values)
            {
                ConfigItemValidation validation = run.For("MissionSets", set.ConfigKey);

                HashSet<string> objectives = new HashSet<string>();
                int             coins      = 0;
                int             gems       = 0;
                int             tokens     = 0;

                foreach (MetaRef<MissionInfo> reference in set.Missions)
                {
                    if (reference == null)
                        continue;

                    MissionInfo mission = ConfigRefs.Resolve(reference, config.Missions);
                    if (mission == null)
                    {
                        // The SDK's reference check also fails the build, but its message does not name the set
                        // or the member.
                        validation.Error($"names mission '{reference.KeyObject}', which is not in the library", nameof(MissionSetInfo.Missions));
                        continue;
                    }

                    validation.Require(mission.Cadence == set.Cadence, $"holds mission '{mission.Id}', which is a {mission.Cadence} mission", nameof(MissionSetInfo.Missions));

                    // Two missions with the same objective and target count would always complete together.
                    if (!objectives.Add($"{mission.Objective}:{mission.TargetCount}"))
                        validation.Error($"asks for {mission.TargetCount} {mission.Objective} twice", nameof(MissionSetInfo.Missions));

                    if (mission.Reward == null)
                        continue;

                    coins  += mission.Reward.AmountOf(CurrencyType.Coins);
                    gems   += mission.Reward.AmountOf(CurrencyType.Gems);
                    tokens += mission.Reward.AmountOf(CurrencyType.SpinTokens);
                }

                validation.Require(gems == 0, $"pays {gems} gems; missions are a coin and spin-token faucet only", nameof(MissionSetInfo.Missions));

                if (set.Cadence == MissionCadence.Daily)
                {
                    validation.Require(tokens == 0, $"pays {tokens} spin tokens; the daily set is the coin faucet", nameof(MissionSetInfo.Missions));
                    validation.Require(
                        coins >= MissionSetInfo.MinDailySetCoins && coins <= MissionSetInfo.MaxDailySetCoins,
                        $"pays {coins} coins for a full set, outside the {MissionSetInfo.MinDailySetCoins}-{MissionSetInfo.MaxDailySetCoins} the economy is balanced against",
                        nameof(MissionSetInfo.Missions));
                }
                else if (set.Cadence == MissionCadence.Weekly)
                {
                    validation.Require(coins == 0, $"pays {coins} coins; the weekly set is the spin-token faucet", nameof(MissionSetInfo.Missions));
                    validation.RequireAtMost(tokens, MissionSetInfo.MaxWeeklySetTokens, nameof(MissionSetInfo.Missions));
                    validation.RequirePositive(tokens, nameof(MissionSetInfo.Missions));
                }
            }
        }

        /// <summary>
        /// Validates <see cref="GlobalConfig"/>: the wallet caps, the starting wallet against the caps and the
        /// cosmetic prices, the daily-reset schedule, the active pointers, and the rewards of the pointed-to
        /// tables against the caps.
        /// </summary>
        static void ValidateGlobal(SharedGameConfig config, ConfigValidationRun run)
        {
            ConfigItemValidation validation = run.For("Global", "Global");
            GlobalConfig     global     = config.Global;

            if (global == null)
            {
                validation.Error("is missing");
                return;
            }

            // A cap must be positive, or nothing can be granted. It must be at most WalletLimits.MaxSafeCap so
            // that adding a grant to a balance near the cap cannot overflow.
            validation.RequirePositive(global.MaxCoins, nameof(GlobalConfig.MaxCoins));
            validation.RequirePositive(global.MaxGems, nameof(GlobalConfig.MaxGems));
            validation.RequirePositive(global.MaxSpinTokens, nameof(GlobalConfig.MaxSpinTokens));
            validation.RequireAtMost(global.MaxCoins, WalletLimits.MaxSafeCap, nameof(GlobalConfig.MaxCoins));
            validation.RequireAtMost(global.MaxGems, WalletLimits.MaxSafeCap, nameof(GlobalConfig.MaxGems));
            validation.RequireAtMost(global.MaxSpinTokens, WalletLimits.MaxSafeCap, nameof(GlobalConfig.MaxSpinTokens));

            if (global.StartingWallet == null)
            {
                validation.Error("has no starting wallet", nameof(GlobalConfig.StartingWallet));
            }
            else
            {
                global.StartingWallet.Validate(validation, nameof(GlobalConfig.StartingWallet));

                foreach ((CurrencyAmount amount, int cap) in OverCap(global, global.StartingWallet))
                    validation.RequireAtMost(amount.Amount, cap, nameof(GlobalConfig.StartingWallet));

                // A new player must be able to spin the wheel once and buy the cheapest coin-priced cosmetic.
                validation.Require(
                    global.StartingWallet.AmountOf(CurrencyType.SpinTokens) >= 1,
                    "must include a spin token, so a new player can spin the wheel once",
                    nameof(GlobalConfig.StartingWallet));

                int cheapestCoinPrice = CheapestPurchasablePrice(config, CurrencyType.Coins);
                if (cheapestCoinPrice > 0)
                {
                    validation.Require(
                        global.StartingWallet.AmountOf(CurrencyType.Coins) >= cheapestCoinPrice,
                        $"holds {global.StartingWallet.AmountOf(CurrencyType.Coins)} coins, which does not buy the cheapest coin-priced cosmetic at {cheapestCoinPrice}",
                        nameof(GlobalConfig.StartingWallet));
                }

                // A new player must not be able to afford the cheapest gem-priced cosmetic, so that gems remain
                // a currency the player has to earn (docs/economy.md).
                int cheapestGemPrice = CheapestPurchasablePrice(config, CurrencyType.Gems);
                if (cheapestGemPrice > 0)
                {
                    validation.Require(
                        global.StartingWallet.AmountOf(CurrencyType.Gems) < cheapestGemPrice,
                        $"holds {global.StartingWallet.AmountOf(CurrencyType.Gems)} gems, which already buys the cheapest gem-priced cosmetic at {cheapestGemPrice}",
                        nameof(GlobalConfig.StartingWallet));
                }
            }

            // The player-local day boundary for daily rewards. DailyResetScheduleRules keeps it on the same
            // local-midnight boundary as the PlayerCalendar day that missions use.
            DailyResetScheduleRules.Validate(validation, global.DailyResetSchedule, nameof(GlobalConfig.DailyResetSchedule));

            DailyRewardTableInfo dailyRewards = RequirePointer(validation, global.ActiveDailyRewardTable, config.DailyRewards, nameof(GlobalConfig.ActiveDailyRewardTable));

            // No step of the active daily reward table may grant more of a currency than its cap.
            if (dailyRewards?.Steps != null)
            {
                foreach (DailyRewardStepInfo step in dailyRewards.Steps)
                {
                    foreach ((CurrencyAmount amount, int cap) in OverCap(global, step?.Reward))
                        validation.Error($"points at '{dailyRewards.Id}', whose step {step.Step} grants {amount.Amount} {amount.Currency} against a cap of {cap}", nameof(GlobalConfig.ActiveDailyRewardTable));
                }
            }

            RequirePointer(validation, global.ActiveFirstWeekSchedule, config.FirstWeekSchedules, nameof(GlobalConfig.ActiveFirstWeekSchedule));

            // No day of any first-week schedule may grant more of a currency than its cap. Every schedule is
            // checked, not only the active one, because a player keeps the schedule they started on for their
            // whole week. Lowering a cap could otherwise make a day of an inactive schedule unclaimable for a
            // player who is still on it. The error names the cap field, because the schedule may be one that no
            // pointer names.
            if (config.FirstWeekSchedules != null)
            {
                foreach ((FirstWeekScheduleId scheduleId, FirstWeekScheduleInfo schedule) in config.FirstWeekSchedules)
                {
                    if (schedule?.Days == null)
                        continue;

                    foreach (FirstWeekDayInfo day in schedule.Days)
                    {
                        foreach ((CurrencyAmount amount, int cap) in OverCap(global, day?.Reward))
                            validation.Error($"is {cap}, and the published schedule '{scheduleId}' grants {amount.Amount} {amount.Currency} on day {day.Day}", GlobalConfig.CapMemberOf(amount.Currency));
                    }
                }
            }

            WheelTableInfo wheel = RequirePointer(validation, global.ActiveWheelTable, config.WheelTables, nameof(GlobalConfig.ActiveWheelTable));

            // No sector of the active wheel table may grant more of a currency than its cap. The wheel refuses
            // to spin while any sector would overflow the player's wallet, so one oversized sector would disable
            // the wheel for a player near a cap.
            if (wheel?.Sectors != null)
            {
                foreach (WheelSectorInfo sector in wheel.Sectors)
                {
                    foreach ((CurrencyAmount amount, int cap) in OverCap(global, sector?.Reward))
                        validation.Error($"points at '{wheel.Id}', whose sector {sector.Sector} pays {amount.Amount} {amount.Currency} against a cap of {cap}", nameof(GlobalConfig.ActiveWheelTable));
                }

                // The starting wallet must pay SpinWheelPolicy.SpinCost. The starting-wallet check above requires
                // one spin token, and this check keeps it correct if the spin cost changes.
                CurrencyAmount price = SpinWheelPolicy.SpinCost;
                if (global.StartingWallet != null)
                {
                    validation.Require(
                        global.StartingWallet.AmountOf(price.Currency) >= price.Amount,
                        $"points at a wheel spun for {price}, which the starting wallet cannot pay",
                        nameof(GlobalConfig.ActiveWheelTable));
                }
            }

            RequirePointer(validation, global.ActiveTournamentRewardTable, config.TournamentRewards, nameof(GlobalConfig.ActiveTournamentRewardTable));

            MissionSetInfo dailySet  = RequirePointer(validation, global.ActiveDailyMissionSet, config.MissionSets, nameof(GlobalConfig.ActiveDailyMissionSet));
            MissionSetInfo weeklySet = RequirePointer(validation, global.ActiveWeeklyMissionSet, config.MissionSets, nameof(GlobalConfig.ActiveWeeklyMissionSet));

            if (dailySet != null)
                validation.Require(dailySet.Cadence == MissionCadence.Daily, $"points at '{dailySet.Id}', which is a {dailySet.Cadence} set", nameof(GlobalConfig.ActiveDailyMissionSet));
            if (weeklySet != null)
                validation.Require(weeklySet.Cadence == MissionCadence.Weekly, $"points at '{weeklySet.Id}', which is a {weeklySet.Cadence} set", nameof(GlobalConfig.ActiveWeeklyMissionSet));
        }

        /// <summary>
        /// Validates the generated-name vocabulary: its size, the spelling of each word, and optionally that
        /// every name it can produce passes <see cref="DisplayNamePolicy.Validate"/>.
        /// <para>
        /// The word lists are published as config, so without this check a bad word would first be found when
        /// the server gives a new player a name that the rename rules refuse.
        /// </para>
        /// </summary>
        static void ValidatePlayerIdentity(SharedGameConfig config, ConfigValidationRun run, bool walkEveryGeneratedName)
        {
            ConfigItemValidation     validation = run.For("PlayerIdentity", "PlayerIdentity");
            PlayerIdentityConfig identity   = config.PlayerIdentity;

            if (identity == null)
            {
                validation.Error("is missing");
                return;
            }

            validation.RequireNotEmpty(identity.Adjectives, nameof(PlayerIdentityConfig.Adjectives));
            validation.RequireNotEmpty(identity.Nouns, nameof(PlayerIdentityConfig.Nouns));
            validation.RequirePositive(identity.SuffixCount, nameof(PlayerIdentityConfig.SuffixCount));
            validation.RequireAtMost(identity.SuffixCount, 100, nameof(PlayerIdentityConfig.SuffixCount));
            validation.RequirePositive(identity.GeneratorVersion, nameof(PlayerIdentityConfig.GeneratorVersion));

            ValidateNameWords(validation, identity.Adjectives, nameof(PlayerIdentityConfig.Adjectives));
            ValidateNameWords(validation, identity.Nouns, nameof(PlayerIdentityConfig.Nouns));

            if (!identity.CanGenerate)
                return;

            validation.Require(
                identity.CombinationCount >= DisplayNameGenerator.MinCombinationCount,
                $"produces {identity.CombinationCount} names, which is under the {DisplayNameGenerator.MinCombinationCount} a player needs not to keep meeting their own name",
                nameof(PlayerIdentityConfig.Adjectives));

            // The check of every generated name below is the expensive part. Tests can skip it with
            // walkEveryGeneratedName.
            if (!walkEveryGeneratedName)
                return;

            // Validate every generated name with the rename rules, including the reserved bot names, so a bot
            // name that the generator can also produce fails the build. One bad word causes many failing names,
            // so the error reports a count and a few examples instead of one error per name.
            BotNameRoster reservedBotNames = BotConfig.ReservedNames(config);

            int          refusedCount = 0;
            List<string> examples     = new List<string>();
            foreach (string candidate in DisplayNameGenerator.AllCombinations(identity))
            {
                DisplayNameRefusal refusal = DisplayNamePolicy.Validate(candidate, reservedBotNames);
                if (refusal == DisplayNameRefusal.None)
                    continue;

                refusedCount++;
                if (examples.Count < 5)
                    examples.Add($"'{candidate}' ({refusal})");
            }

            validation.Require(
                refusedCount == 0,
                $"can generate {refusedCount} names the server would refuse, for example {string.Join(", ", examples)}",
                nameof(PlayerIdentityConfig.Adjectives));
        }

        /// <summary>
        /// Validates one word list: no blank words, no case-insensitive duplicates, and letters only. A space or
        /// hyphen in a word would put punctuation into generated names.
        /// </summary>
        static void ValidateNameWords(ConfigItemValidation validation, List<string> words, string memberHint)
        {
            if (words == null)
                return;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string word in words)
            {
                if (string.IsNullOrWhiteSpace(word))
                {
                    validation.Error("holds a blank word", memberHint);
                    continue;
                }

                foreach (char ch in word)
                {
                    if (!char.IsLetter(ch))
                    {
                        validation.Error($"holds '{word}', which is not letters only", memberHint);
                        break;
                    }
                }

                if (!seen.Add(word))
                    validation.Error($"holds '{word}' more than once", memberHint);
            }
        }

        /// <summary>
        /// Resolves an active pointer and reports an error if it is null or names no item in
        /// <paramref name="library"/>. Works whether or not the config's <see cref="MetaRef{TItem}"/>s have been
        /// resolved, so the checks also work on a config built in a test.
        /// </summary>
        static TInfo RequirePointer<TKey, TInfo>(ConfigItemValidation validation, MetaRef<TInfo> reference, IGameConfigLibrary<TKey, TInfo> library, string memberHint)
            where TInfo : class, IGameConfigData<TKey>
        {
            if (reference == null)
            {
                validation.Error("points at nothing", memberHint);
                return null;
            }

            TInfo item = ConfigRefs.Resolve(reference, library);
            if (item == null)
                validation.Error($"points at '{reference.KeyObject}', which is not in the library", memberHint);
            return item;
        }

        /// <summary>
        /// The amounts of <paramref name="reward"/> that exceed their currency's cap in <paramref name="global"/>,
        /// each with that cap. A cap of zero means no cap. A null reward yields nothing.
        /// </summary>
        static IEnumerable<(CurrencyAmount Amount, int Cap)> OverCap(GlobalConfig global, RewardBundle reward)
        {
            if (reward?.Amounts == null)
                yield break;

            foreach (CurrencyAmount amount in reward.Amounts)
            {
                int cap = global.CapOf(amount.Currency);
                if (cap > 0 && amount.Amount > cap)
                    yield return (amount, cap);
            }
        }

        /// <summary>
        /// The lowest positive <paramref name="currency"/> price among purchasable cosmetics, or zero if none is
        /// priced in that currency.
        /// </summary>
        static int CheapestPurchasablePrice(SharedGameConfig config, CurrencyType currency)
        {
            if (config.Cosmetics == null)
                return 0;

            int cheapest = 0;
            foreach (CosmeticInfo cosmetic in config.Cosmetics.Values)
            {
                if (!cosmetic.IsPurchasable || cosmetic.Price == null || cosmetic.Price.Currency != currency || cosmetic.Price.Amount <= 0)
                    continue;
                if (cheapest == 0 || cosmetic.Price.Amount < cheapest)
                    cheapest = cosmetic.Price.Amount;
            }
            return cheapest;
        }

        /// <summary>
        /// Validates the cosmetic catalogue as a whole (<c>docs/cosmetics.md</c>).
        /// <para>
        /// Every rendered slot must have at least one purchasable item, or its tab in the cosmetics grid is
        /// empty. Every default cosmetic must exist and must not be purchasable. A cosmetic awarded by a
        /// tournament placement must not be purchasable, so it can only be earned. The <c>MetaRef</c> check
        /// already guarantees that the awarded cosmetic exists.
        /// </para>
        /// </summary>
        static void ValidateCosmetics(SharedGameConfig config, ConfigValidationRun run)
        {
            if (config.Cosmetics == null)
                return;

            ConfigItemValidation validation = run.For("Cosmetics", "Cosmetics");

            foreach (CosmeticKind slot in CosmeticStyles.ShippedSlots)
            {
                bool anyForSale = false;
                foreach (CosmeticInfo cosmetic in config.Cosmetics.Values)
                {
                    if (cosmetic.Kind == slot && cosmetic.IsPurchasable)
                    {
                        anyForSale = true;
                        break;
                    }
                }

                validation.Require(anyForSale, $"has nothing for sale in the {slot} slot, so that tab of the grid sells nothing", "IsPurchasable");
            }

            // Every player starts owning and wearing the CosmeticDefaults items. A missing one would leave new
            // players with an empty slot, which the client hides by drawing the default look. Selling one would
            // charge players for an item they already own.
            foreach (CosmeticId id in CosmeticDefaults.All)
            {
                CosmeticInfo starting = config.Cosmetics.GetValueOrDefault(id);

                validation.Require(starting != null, $"does not carry '{id}', which every player starts owning and wearing", "Id");

                if (starting != null)
                    validation.Require(!starting.IsPurchasable, $"sells '{id}', which every player already owns", "IsPurchasable");
            }

            if (config.TournamentRewards == null)
                return;

            foreach (TournamentRewardTableInfo table in config.TournamentRewards.Values)
            {
                if (table?.Placements == null)
                    continue;

                foreach (TournamentPlacementInfo band in table.Placements)
                {
                    if (band?.Cosmetic == null)
                        continue;

                    // The reference is resolved in an imported config and unresolved in a config built in a test,
                    // and ConfigRefs.Resolve handles both.
                    CosmeticInfo awarded = ConfigRefs.Resolve(band.Cosmetic, config.Cosmetics);

                    if (awarded == null)
                        continue;

                    validation.Require(
                        !awarded.IsPurchasable,
                        $"awards '{awarded.Id}' for placing in '{table.Id}', and the shop sells the same item",
                        nameof(TournamentPlacementInfo.Cosmetic));
                }
            }
        }
    }
}
