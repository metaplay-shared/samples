namespace WebClient.Meta;

/// <summary>
/// An immutable view of all meta state at one point in time.
/// <para>
/// The Home card choice and the Events hub order are both computed from one snapshot. Reading live services one
/// property at a time could mix values from before and after an action, for example suggesting a daily reward
/// claim above a card that already shows it claimed.
/// </para>
/// </summary>
public sealed record MetaSnapshot(
    IdentityView    Identity,
    WalletView      Wallet,
    DailyRewardView DailyReward,
    FirstWeekView   FirstWeek,
    MissionsView    Missions,
    SpinWheelView   SpinWheel,
    WeeklyEventView WeeklyEvent,
    TournamentView  Tournament,
    ShopView        Shop,
    ProfileView     Profile)
{
    /// <summary>The features on the Events hub, in <see cref="MetaFeature"/> order.</summary>
    public IReadOnlyList<IFeatureView> EventsFeatures => new IFeatureView[]
    {
        FirstWeek, DailyReward, Missions, SpinWheel, WeeklyEvent,
    };

    /// <summary>
    /// The features on the Compete hub. The seasonal tournament is the only one
    /// (<c>docs/seasonal-tournament.md</c>).
    /// </summary>
    public IReadOnlyList<IFeatureView> CompeteFeatures => new IFeatureView[] { Tournament };

    /// <summary>
    /// The view for a feature, or null for <see cref="MetaFeature.Play"/>, which has no meta state. It returns null
    /// instead of an empty view so that a caller asking for Play's state fails visibly.
    /// </summary>
    public IFeatureView? ViewOf(MetaFeature feature) => feature switch
    {
        MetaFeature.FirstWeekEvent => FirstWeek,
        MetaFeature.DailyReward    => DailyReward,
        MetaFeature.Missions       => Missions,
        MetaFeature.SpinWheel      => SpinWheel,
        MetaFeature.WeeklyEvent    => WeeklyEvent,
        MetaFeature.Tournament     => Tournament,
        MetaFeature.Shop           => Shop,
        MetaFeature.Cosmetics      => Cosmetics,
        MetaFeature.Profile        => Profile,
        _                          => null,
    };

    public CosmeticsView Cosmetics => Profile.Cosmetics;
}

/// <summary>
/// What the player has already seen on this client. It is stored on the client and is not game state.
/// <para>
/// <see cref="BadgePolicy"/> takes it as a parameter instead of reading browser storage, so the badge rules can be
/// tested outside a browser.
/// </para>
/// </summary>
public sealed record SeenState(IReadOnlyCollection<string> SeenOfferIds, bool CosmeticAcknowledged)
{
    public static readonly SeenState Nothing = new SeenState(Array.Empty<string>(), false);
}
