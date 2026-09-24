namespace WebClient.Meta;

/// <summary>
/// A player's public identity as other players see it: display name, worn cosmetics and competitive standing.
/// <para>
/// The cosmetics preview and the standings both render this record, so a cosmetic looks the same in both places
/// (docs/player.md, "Public identity").
/// </para>
/// </summary>
public sealed record IdentityView(
    string DisplayName,

    /// <summary>
    /// The avatar's style token, or empty for no avatar, which shows the default spade. It holds the style token,
    /// not the catalogue id (see <see cref="FrameToken"/>).
    /// </summary>
    string AvatarToken,

    /// <summary>
    /// The frame's style token, or empty for no frame.
    /// <para>
    /// Components build CSS classes from this value. It must be the style token, not the catalogue id: the two are
    /// different strings, and a class built from the catalogue id matches no CSS rule
    /// (see <see cref="CosmeticsPolicy.StyleTokenOf"/>).
    /// </para>
    /// </summary>
    string FrameToken,

    /// <inheritdoc cref="FrameToken"/>
    string NameEffectToken,

    int    Level,

    /// <summary>
    /// The name of the competition for <see cref="CompetitionRank"/>, such as the seasonal tournament, or empty.
    /// </summary>
    string CompetitionName,

    /// <summary>The player's rank in the competition, starting from 1, or zero if the player is not ranked.</summary>
    int    CompetitionRank)
{
    /// <summary>
    /// The competition name and rank as one line of text.
    /// <para>
    /// A rank without a competition name shows <see cref="StandingBadge"/> instead of "Not in a competition", so
    /// that this property and <see cref="StandingBadge"/> agree.
    /// </para>
    /// </summary>
    public string StandingLine =>
        CompetitionRank > 0 && CompetitionName.Length > 0 ? $"{CompetitionName} · #{CompetitionRank}"
        : CompetitionRank > 0                             ? StandingBadge
        : CompetitionName.Length > 0                      ? CompetitionName
        :                                                   "Not in a competition";

    /// <summary>The rank alone, for places with little room, such as a standings row.</summary>
    public string StandingBadge => CompetitionRank > 0 ? $"#{CompetitionRank}" : "Unranked";
}

/// <summary>Which cosmetic slot an item goes in.</summary>
public enum CosmeticSlot
{
    Avatar,
    Frame,
    NameEffect,
}

/// <summary>
/// A cosmetic's ownership state, which decides what the player can do with it.
/// <see cref="CosmeticsPolicy.OwnershipOf"/> chooses it.
/// </summary>
public enum CosmeticOwnership
{
    Equipped,
    Owned,

    /// <summary>For sale, and the player can afford it.</summary>
    Affordable,

    /// <summary>For sale, and the player cannot afford it.</summary>
    Unaffordable,

    /// <summary>Not for sale. Unlocked by an achievement, which the card names.</summary>
    Locked,

    /// <summary>Not obtainable by this player.</summary>
    Unavailable,
}

/// <summary>One cosmetic.</summary>
public sealed record CosmeticItem(
    /// <summary>The catalogue id. The purchase action uses it, and tests use it to find the tile.</summary>
    string            Id,

    CosmeticSlot      Slot,
    string            Name,
    string            Flavour,
    CosmeticOwnership Ownership,
    CurrencyKind      PriceCurrency,
    long              PriceAmount,
    string            UnlockRequirement,

    /// <summary>
    /// The style token used in CSS classes. It differs from <see cref="Id"/> (see <see cref="IdentityView.FrameToken"/>).
    /// </summary>
    string            StyleToken = "")
{
    /// <summary>How much <paramref name="wallet"/> is short of the price, or zero.</summary>
    public long ShortfallAgainst(WalletView wallet) => Math.Max(0, PriceAmount - wallet.AmountOf(PriceCurrency));
}

/// <summary>The cosmetics catalogue and the selected slot.</summary>
public sealed record CosmeticsView(
    ActivityState                 State,
    IReadOnlyList<CosmeticItem>   Items,
    CosmeticSlot                  SelectedSlot,
    bool                          HasUnacknowledgedAcquisition) : IFeatureView
{
    public MetaFeature Feature          => MetaFeature.Cosmetics;
    public TimeSpan?   UntilDeadline    => null;
    public double?     Progress         => null;
    public TimeSpan    EndingSoonWithin => TimeSpan.Zero;

    public IEnumerable<CosmeticItem> InSlot(CosmeticSlot slot) => Items.Where(i => i.Slot == slot);

    /// <summary>
    /// The slots that have at least one item, in order of first appearance. The grid shows a tab for each.
    /// <para>
    /// A slot with no items gets no tab. Config validation also rejects a catalogue with an empty slot
    /// (<c>docs/cosmetics.md</c>).
    /// </para>
    /// </summary>
    public IReadOnlyList<CosmeticSlot> Slots => Items.Select(i => i.Slot).Distinct().ToArray();

    public CosmeticItem? EquippedIn(CosmeticSlot slot) =>
        Items.FirstOrDefault(i => i.Slot == slot && i.Ownership == CosmeticOwnership.Equipped);
}

/// <summary>
/// The Profile screen: identity, game statistics and the cosmetics catalogue.
/// <para>
/// The display name and the statistics are read from the player model once a session exists, not from fixtures.
/// </para>
/// </summary>
public sealed record ProfileView(
    ActivityState State,
    IdentityView  Identity,
    int           GamesPlayed,
    int           GamesWon,
    int           TricksWon,
    CosmeticsView Cosmetics) : IFeatureView
{
    public MetaFeature Feature          => MetaFeature.Profile;
    public TimeSpan?   UntilDeadline    => null;
    public double?     Progress         => null;
    public TimeSpan    EndingSoonWithin => TimeSpan.Zero;

    /// <summary>
    /// The win rate as a rounded whole percent, or null if the player has finished no games, so that a new player
    /// is not shown 0% (docs/player.md).
    /// </summary>
    public int? WinRatePercent => GamesPlayed <= 0 ? (int?)null : (int)Math.Round(100.0 * GamesWon / GamesPlayed);

    /// <summary>The win rate as text: a percentage, or a dash if <see cref="WinRatePercent"/> is null.</summary>
    public string WinRateText => WinRatePercent.HasValue ? $"{WinRatePercent.Value}%" : "—";
}
