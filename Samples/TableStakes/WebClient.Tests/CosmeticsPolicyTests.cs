using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Model;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="CosmeticsPolicy"/> (tile state, preview, refusal text), the standings rows built by
/// <see cref="StepRailBuilder.StandingsOf"/>, and the check that every <see cref="CosmeticStyle"/> has a rule that draws it.
/// These run without a browser or a server (<c>docs/cosmetics.md</c>).
/// </summary>
[TestFixture]
public class CosmeticsPolicyTests
{
    private static CosmeticItem Item(CosmeticOwnership ownership, CosmeticSlot slot = CosmeticSlot.Frame, string styleToken = "frame-silver") =>
        new CosmeticItem("frame.silver", slot, "Silver Frame", "Where everybody starts.", ownership, CurrencyKind.Coins, 500, "", styleToken);

    #region Tile states

    /// <summary>
    /// An owned and equipped item reads as Equipped, not Owned. An owned item reads as Owned even when it is
    /// purchasable, because ownership is permanent. An unowned item for sale reads from the balance. An item that is
    /// not for sale reads as Locked when it names an unlock requirement, and as Unavailable when it names none. That
    /// is the state of a retired catalogue entry: it stays in the catalogue so existing owners keep it, but nobody can
    /// obtain it.
    /// </summary>
    [TestCase(true,  true,  false, true,  "",                        CosmeticOwnership.Equipped)]
    [TestCase(true,  false, true,  true,  "",                        CosmeticOwnership.Owned)]
    [TestCase(true,  false, true,  false, "",                        CosmeticOwnership.Owned)]
    [TestCase(false, false, true,  true,  "",                        CosmeticOwnership.Affordable)]
    [TestCase(false, false, true,  false, "",                        CosmeticOwnership.Unaffordable)]
    [TestCase(false, false, false, true,  "Win a tournament season", CosmeticOwnership.Locked)]
    [TestCase(false, false, false, true,  "",                        CosmeticOwnership.Unavailable)]
    public void EachItemReadsAsItsTileState(bool isOwned, bool isEquipped, bool isPurchasable, bool canAfford, string unlockRequirement, CosmeticOwnership expected)
    {
        Assert.That(CosmeticsPolicy.OwnershipOf(isOwned: isOwned, isEquipped: isEquipped, isPurchasable: isPurchasable, canAfford: canAfford, unlockRequirement: unlockRequirement),
            Is.EqualTo(expected));
    }

    #endregion

    #region The preview

    private static readonly IdentityView Worn = new IdentityView(
        DisplayName: "Avery", AvatarToken: "", FrameToken: "frame-sapphire", NameEffectToken: "name-prism", Level: 1,
        CompetitionName: "Seasonal Tournament", CompetitionRank: 4);

    /// <summary>
    /// The preview replaces only the item's slot and keeps the rest of the worn identity, so it shows the player as
    /// the standings would. The slot takes the item's style token, not its catalogue id. The two can differ
    /// (<c>frame.brass</c> uses <c>frame-bronze</c>), and a catalogue id in this field would produce a CSS class that
    /// matches no rule, which renders as no frame without any error.
    /// </summary>
    [Test]
    public void ThePreviewSwapsOneSlotAndKeepsTheRest()
    {
        IdentityView preview = CosmeticsPolicy.Preview(Worn, Item(CosmeticOwnership.Affordable, CosmeticSlot.Frame, "frame-emerald"));

        Assert.That(preview.FrameToken, Is.EqualTo("frame-emerald"));
        Assert.That(preview.NameEffectToken, Is.EqualTo("name-prism"));
        Assert.That(preview.DisplayName, Is.EqualTo("Avery"));
        Assert.That(preview.CompetitionRank, Is.EqualTo(4));
    }

    [Test]
    public void PreviewingANameEffectLeavesTheFrameAlone()
    {
        IdentityView preview = CosmeticsPolicy.Preview(Worn, Item(CosmeticOwnership.Affordable, CosmeticSlot.NameEffect, "name-amber"));

        Assert.That(preview.NameEffectToken, Is.EqualTo("name-amber"));
        Assert.That(preview.FrameToken, Is.EqualTo("frame-sapphire"));
    }

    /// <summary>An avatar is also previewed by its style token, not its catalogue id.</summary>
    [Test]
    public void ThePreviewUsesTheStyleTokenForAnAvatarToo()
    {
        CosmeticItem crown = new CosmeticItem(
            "avatar.crown", CosmeticSlot.Avatar, "Crown", "", CosmeticOwnership.Owned, CurrencyKind.Coins, 2_500, "", "avatar-crown");

        Assert.That(CosmeticsPolicy.Preview(Worn, crown).AvatarToken, Is.EqualTo("avatar-crown"));
    }

    #endregion

    #region Standings rows carry the seat's cosmetics

    private static readonly EntityId Me  = EntityId.Create(EntityKindCore.Player, 7);
    private static readonly EntityId You = EntityId.Create(EntityKindCore.Player, 8);

    /// <summary>
    /// Stands in for the catalogue's id-to-token lookup. It maps a few ids to tokens that differ from the id and
    /// returns an empty string for every other id.
    /// </summary>
    private static string FakeToken(CosmeticId? id) => id?.Value switch
    {
        "frame.brass"  => "frame-bronze",
        "name.shimmer" => "name-prism",
        "avatar.queen" => "avatar-heart",
        _              => "",
    };

    /// <summary>
    /// A seated player wearing <paramref name="avatar"/>, <paramref name="frame"/> and
    /// <paramref name="nameEffect"/> as catalogue ids, as a real seat carries them. The
    /// <see cref="PlayerPublicIdentity"/> constructor is internal and visible to this assembly through
    /// <c>SharedCode/AssemblyInfo.cs</c>.
    /// </summary>
    private static TournamentEntrant Seat(int number, EntityId player, string? frame, string? nameEffect, string? avatar = null) =>
        new TournamentEntrant(
            number,
            isBot: false,
            identity: new PlayerPublicIdentity(
                player,
                $"Player {number}",
                avatarId: avatar == null ? null : CosmeticId.FromString(avatar),
                frameId: frame == null ? null : CosmeticId.FromString(frame),
                nameEffectId: nameEffect == null ? null : CosmeticId.FromString(nameEffect)),
            wins: 3,
            scoredMatches: 5,
            lastWinAt: MetaTime.Epoch);

    private static TournamentEntrant Bot(int number) =>
        new TournamentEntrant(number, isBot: true, PlayerPublicIdentity.ForBot(EntityId.Create(EntityKindCore.Player, (ulong)(100 + number)), $"Bot {number}", avatarId: null),
            wins: 1, scoredMatches: 5, lastWinAt: MetaTime.Epoch);

    /// <summary>
    /// A seat's identity carries the player's cosmetics as catalogue ids, and a standings row must carry style
    /// tokens. Without the translation every row renders with no frame, including the player's own.
    /// <para>
    /// This calls the production row builder, <see cref="StepRailBuilder.StandingsOf"/>. Fixture rows are written with
    /// tokens already in place, so they would pass even if the translation were removed.
    /// </para>
    /// </summary>
    [Test]
    public void AStandingsRowWearsTheStyleTokenOfWhatItsSeatOwns()
    {
        IReadOnlyList<StandingRow> rows = StepRailBuilder.StandingsOf(
            new[] { Seat(1, Me, "frame.brass", "name.shimmer", "avatar.queen") }, Me, FakeToken);

        Assert.That(rows[0].FrameToken, Is.EqualTo("frame-bronze"), "the row lost the frame between the seat and the screen");
        Assert.That(rows[0].NameEffectToken, Is.EqualTo("name-prism"));
        Assert.That(rows[0].AvatarToken, Is.EqualTo("avatar-heart"), "the row lost the avatar between the seat and the screen");
        Assert.That(rows[0].IsSelf, Is.True);
    }

    /// <summary>Another player's row also carries that player's style tokens, so other players see what they bought.</summary>
    [Test]
    public void AnotherPlayersRowWearsTheirsToo()
    {
        IReadOnlyList<StandingRow> rows = StepRailBuilder.StandingsOf(
            new[] { Seat(1, You, "frame.brass", null) }, Me, FakeToken);

        Assert.That(rows[0].FrameToken, Is.EqualTo("frame-bronze"));
        Assert.That(rows[0].IsSelf, Is.False);
    }

    /// <summary>
    /// A seat with nothing equipped, and a bot, get empty strings. An empty modifier adds no CSS class, so the
    /// avatar draws its defaults.
    /// </summary>
    [Test]
    public void ASeatWearingNothingCarriesNoToken()
    {
        IReadOnlyList<StandingRow> rows = StepRailBuilder.StandingsOf(
            new[] { Seat(1, Me, null, null), Bot(2) }, Me, FakeToken);

        Assert.That(rows[0].FrameToken, Is.Empty);
        Assert.That(rows[0].NameEffectToken, Is.Empty);
        Assert.That(rows[1].FrameToken, Is.Empty, "a bot owns nothing");
        Assert.That(rows[1].IsBot, Is.True);
    }

    /// <summary>
    /// An id that is missing from the catalogue maps to an empty string instead of reaching a class attribute. A
    /// load-time check is meant to prevent this case, and this test pins the fallback if it happens anyway.
    /// </summary>
    [Test]
    public void AnIdThatDoesNotResolveDrawsNothing()
    {
        IReadOnlyList<StandingRow> rows = StepRailBuilder.StandingsOf(
            new[] { Seat(1, Me, "frame.withdrawn", null) }, Me, FakeToken);

        Assert.That(rows[0].FrameToken, Is.Empty);
    }

    /// <summary>A row's rank is its 1-based position in the seat list passed in.</summary>
    [Test]
    public void RowsAreRankedInTheOrderTheyArrive()
    {
        IReadOnlyList<StandingRow> rows = StepRailBuilder.StandingsOf(
            new[] { Bot(1), Seat(2, Me, "frame.brass", null), Bot(3) }, Me, FakeToken);

        Assert.That(rows.Select(r => r.Rank), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(rows[1].IsSelf, Is.True);
    }

    #endregion

    #region CosmeticsPolicy.ViewOf

    /// <summary>
    /// <see cref="CosmeticsPolicy.ViewOf"/> converts a public identity for every surface that shows a player. It
    /// carries no competition name or rank, because callers such as the table have no standing to show.
    /// </summary>
    [Test]
    public void ViewOfCarriesTheNameAndNoStandingAtAll()
    {
        IdentityView view = CosmeticsPolicy.ViewOf(
            config: null,
            new PlayerPublicIdentity(Me, "Avery", avatarId: null, frameId: null, nameEffectId: null));

        Assert.That(view.DisplayName, Is.EqualTo("Avery"));
        Assert.That(view.CompetitionRank, Is.Zero);
        Assert.That(view.CompetitionName, Is.Empty);
    }

    /// <summary>
    /// With no config to resolve them, equipped ids convert to empty strings, which draw the defaults. Passing the
    /// raw id through instead would produce a CSS class that matches no rule.
    /// </summary>
    [Test]
    public void ViewOfCrossesAnUnresolvableIdToWearingNothing()
    {
        IdentityView view = CosmeticsPolicy.ViewOf(
            config: null,
            new PlayerPublicIdentity(Me, "Avery",
                avatarId:     CosmeticId.FromString("avatar.queen"),
                frameId:      CosmeticId.FromString("frame.brass"),
                nameEffectId: CosmeticId.FromString("name.shimmer")));

        Assert.That(view.AvatarToken, Is.Empty);
        Assert.That(view.FrameToken, Is.Empty);
        Assert.That(view.NameEffectToken, Is.Empty);
    }

    /// <summary>A null identity converts to an empty view instead of throwing.</summary>
    [Test]
    public void ViewOfAnAbsentProjectionDrawsNothingAtAll()
    {
        IdentityView view = CosmeticsPolicy.ViewOf(config: null, identity: null);

        Assert.That(view.DisplayName, Is.Empty);
        Assert.That(view.FrameToken, Is.Empty);
    }

    #endregion

    #region Refusal text

    /// <summary>The item a refused purchase was working from, priced in <paramref name="currency"/>.</summary>
    private static CosmeticItem Priced(CurrencyKind currency, long price) =>
        new CosmeticItem("frame.emerald", CosmeticSlot.Frame, "Emerald Frame", "", CosmeticOwnership.Affordable, currency, price, "", "frame-emerald");

    private static readonly WalletView WalletOf750 = new WalletView(750, 750, 0);

    /// <summary>
    /// Each refusal result has its own sentence instead of one generic line. A result with no sentence of its own,
    /// such as a system error, gets the generic line.
    /// </summary>
    [TestCaseSource(nameof(RefusalSentences))]
    public void EachRefusalHasItsOwnSentence(MetaActionResult result, string expected)
    {
        Assert.That(CosmeticsPolicy.RefusalMessage(result, Priced(CurrencyKind.Coins, 500), WalletOf750), Is.EqualTo(expected));
    }

    private static IEnumerable<TestCaseData> RefusalSentences()
    {
        yield return new TestCaseData(ActionResults.NoSuchCosmetic, "That item isn't in the catalogue any more. Nothing was spent.")
            .SetArgDisplayNames(nameof(ActionResults.NoSuchCosmetic));
        yield return new TestCaseData(ActionResults.CosmeticNotPurchasable, "This one isn't for sale — it's earned, not bought.")
            .SetArgDisplayNames(nameof(ActionResults.CosmeticNotPurchasable));
        yield return new TestCaseData(ActionResults.CosmeticAlreadyOwned, "You already own this one.")
            .SetArgDisplayNames(nameof(ActionResults.CosmeticAlreadyOwned));
        yield return new TestCaseData(ActionResults.CosmeticNotOwned, "You don't own that one yet.")
            .SetArgDisplayNames(nameof(ActionResults.CosmeticNotOwned));
        yield return new TestCaseData(ActionResults.CosmeticAlreadyEquipped, "You're already wearing it.")
            .SetArgDisplayNames(nameof(ActionResults.CosmeticAlreadyEquipped));
        yield return new TestCaseData(MetaActionResult.UnknownError, "That did not go through. Please try again.")
            .SetArgDisplayNames(nameof(MetaActionResult.UnknownError));
    }

    /// <summary>The shortfall is the item's price minus the wallet balance the tap read, in the price's currency. A
    /// refused purchase spends nothing, so that balance is still current.</summary>
    [TestCase(CurrencyKind.Coins, "Not enough Coins — you need 150 more Coins.")]
    [TestCase(CurrencyKind.Gems,  "Not enough Gems — you need 150 more Gems.")]
    public void AShortfallNamesTheCurrencyAndTheGap(CurrencyKind currency, string expected)
    {
        Assert.That(CosmeticsPolicy.RefusalMessage(ActionResults.InsufficientFunds, Priced(currency, 900), WalletOf750),
            Is.EqualTo(expected));
    }

    [TestCase(901)]
    [TestCase(900)]
    public void AShortfallOfOneOrZeroIsNotSignedNegative(long price)
    {
        string refusal = CosmeticsPolicy.RefusalMessage(ActionResults.InsufficientFunds, Priced(CurrencyKind.Coins, price), WalletOf750)!;

        Assert.That(refusal, Does.Not.Contain("-"), "a shortfall is never drawn negative");
    }

    /// <summary>A success produces no refusal text.</summary>
    [Test]
    public void SuccessDrawsNothing()
    {
        Assert.That(CosmeticsPolicy.RefusalMessage(MetaActionResult.Success, Priced(CurrencyKind.Coins, 500), WalletOf750),
            Is.Null);
    }

    /// <summary>A null result, which means no session sent the action, produces no refusal text. Fixture scenarios,
    /// offline mode and render tests have no session.</summary>
    [Test]
    public void NoSessionDrawsNothing()
    {
        Assert.That(CosmeticsPolicy.RefusalMessage(null, Priced(CurrencyKind.Coins, 500), WalletOf750),
            Is.Null);
    }

    #endregion

    #region Styles have rules to draw them with

    /// <summary>
    /// Every <see cref="CosmeticStyle"/> has a rule that draws it. For a frame or name effect the rule is a CSS
    /// class in meta-shell.css, and for an avatar it is a case in AvatarIcon's switch, keyed by the full token. A
    /// missing rule produces no error: the browser ignores a class with no rule, and AvatarIcon draws its default
    /// glyph for an unknown token.
    /// </summary>
    [Test]
    public void EveryStyleHasARuleInTheStylesheet()
    {
        string css        = CssSource.MetaCss();
        string avatarIcon = File.ReadAllText(AvatarIconPath());

        foreach (CosmeticStyle style in Enum.GetValues<CosmeticStyle>())
        {
            if (style == CosmeticStyle.None)
                continue;

            CosmeticKind slot = CosmeticStyles.SlotOf(style);

            if (slot == CosmeticKind.Avatar)
            {
                string arm = $"\"{CosmeticStyles.TokenOf(style)}\"";
                Assert.That(Regex.IsMatch(avatarIcon, Regex.Escape(arm) + @"\s*=>"), Is.True,
                    $"{style} has no '{arm} =>' arm in AvatarIcon.razor, so anything wearing it draws the default spade");
                continue;
            }

            string block = slot == CosmeticKind.Frame ? "m-frame" : "m-name";
            string rule  = $".{block}--{CosmeticStyles.TokenOf(style)}";

            Assert.That(Regex.IsMatch(css, Regex.Escape(rule) + @"\s*[,{]"), Is.True,
                $"{style} has no '{rule}' rule in meta-shell.css, so anything wearing it renders as nothing");
        }
    }

    /// <summary>
    /// Every frame and name effect rule in meta-shell.css belongs to a <see cref="CosmeticStyle"/>. A rule without one is
    /// unused, because no item can use it.
    /// </summary>
    [Test]
    public void EveryCosmeticRuleInTheStylesheetIsReachable()
    {
        string css = CssSource.MetaCss();

        HashSet<string> tokens = Enum.GetValues<CosmeticStyle>()
            .Where(style => style != CosmeticStyle.None)
            .Select(CosmeticStyles.TokenOf)
            .ToHashSet();

        foreach (Match match in Regex.Matches(css, @"\.m-(?:frame|name)--([a-z0-9-]+)"))
        {
            Assert.That(tokens, Does.Contain(match.Groups[1].Value),
                $"meta-shell.css has a '.{match.Value.TrimStart('.')}' rule that no CosmeticStyle names");
        }
    }

    /// <summary>
    /// Inside a shop tile, the plain name effect must set an explicit colour. <c>.m-name--name-plain</c> inherits
    /// its colour, which lets a plain name turn gilt on the viewer's own standings row. A shop tile's colour is
    /// gold, so without an explicit colour the "no effect" tile would look like the amber effect's tile.
    /// </summary>
    [Test]
    public void ThePlainNameEffectIsPinnedInsideAShopTile()
    {
        string css = CssSource.MetaCss();

        Match rule = Regex.Match(css, @"\.m-cosmetic__effect\.m-name--name-plain\s*\{([^}]*)\}");

        Assert.That(rule.Success, Is.True,
            "meta-shell.css does not pin '.m-cosmetic__effect.m-name--name-plain', so the plain tile inherits the tile's gold");

        Match colour = Regex.Match(rule.Groups[1].Value, @"color\s*:\s*([^;]+)");

        Assert.That(colour.Success, Is.True, "the plain tile's rule sets no colour at all");
        Assert.That(colour.Groups[1].Value.Trim(), Is.Not.EqualTo("inherit"),
            "the plain tile's rule inherits, which is the gold it exists to stop");
    }

    /// <summary>
    /// Every frame style has a case in AvatarFrame's switch, keyed by the full token. A missing case draws the
    /// default hairline frame without any error.
    /// </summary>
    [Test]
    public void EveryFrameStyleHasASilhouette()
    {
        string avatarFrame = File.ReadAllText(AvatarFramePath());

        foreach (CosmeticStyle style in Enum.GetValues<CosmeticStyle>())
        {
            if (CosmeticStyles.SlotOf(style) != CosmeticKind.Frame)
                continue;

            string arm = $"\"{CosmeticStyles.TokenOf(style)}\"";
            Assert.That(Regex.IsMatch(avatarFrame, Regex.Escape(arm) + @"\s*=>"), Is.True,
                $"{style} has no '{arm} =>' arm in AvatarFrame.razor, so anything wearing it draws the default hairline");
        }
    }

    /// <summary>
    /// Every case in AvatarFrame's switch belongs to a frame style. A case without one is unused, because no item
    /// can use it.
    /// </summary>
    [Test]
    public void EverySilhouetteIsAFrameStyle()
    {
        string avatarFrame = File.ReadAllText(AvatarFramePath());

        HashSet<string> tokens = Enum.GetValues<CosmeticStyle>()
            .Where(style => CosmeticStyles.SlotOf(style) == CosmeticKind.Frame)
            .Select(CosmeticStyles.TokenOf)
            .ToHashSet();

        foreach (Match match in Regex.Matches(avatarFrame, @"""(frame-[a-z0-9-]+)""\s*=>"))
        {
            Assert.That(tokens, Does.Contain(match.Groups[1].Value),
                $"AvatarFrame.razor has a '{match.Groups[1].Value}' silhouette that no CosmeticStyle names");
        }
    }

    private static string AvatarIconPath() => CssSource.RepoPath("WebClient/Components/Meta/AvatarIcon.razor");

    private static string AvatarFramePath() => CssSource.RepoPath("WebClient/Components/Meta/AvatarFrame.razor");

    /// <summary>
    /// Every name effect <c>@keyframes</c> block animates only <c>opacity</c> and <c>transform</c>, which the
    /// compositor can animate without repainting. A looping <c>background-position</c> or <c>text-shadow</c> makes
    /// the main thread repaint every frame while the screen is open. The browser test
    /// <c>NothingInTheShellLoopsForeverOnAPropertyThatHasToBeRepainted</c> checks this for the lobby, which shows no
    /// name effects, so this test checks the stylesheet instead.
    /// </summary>
    [Test]
    public void IdleNameEffectLoopsMoveOnlyWhatTheCompositorCanMove()
    {
        string css = CssSource.MetaCss();

        List<string> found = Regex.Matches(css, @"@keyframes (m-name-[a-z-]+)")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.That(found, Is.SupersetOf(new[] { "m-name-prism", "m-name-bloom", "m-name-drip" }),
            "an effect lost its keyframes");

        foreach (string animation in found)
        {
            string block = CssSource.BlockFollowing(css, css.IndexOf("@keyframes " + animation, StringComparison.Ordinal));

            Assert.That(block, Is.Not.Empty, $"the {animation} keyframes are gone from meta-shell.css");

            List<string> properties = Regex.Matches(block, @"([a-z-]+)\s*:")
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();

            Assert.That(properties, Is.SubsetOf(new[] { "opacity", "transform" }),
                $"{animation} animates a property the compositor cannot move, so the loop repaints every frame it runs");
        }
    }

    /// <summary>
    /// Every animated name effect stops under reduced motion. The shell's reduced-motion rule covers only elements
    /// inside <c>.m-shell</c>, so meta-shell.css has a separate block for the name effects. The test names each animated
    /// effect so that one missing from the block fails by name.
    /// <see cref="IdleNameEffectLoopsMoveOnlyWhatTheCompositorCanMove"/> checks that their keyframes exist.
    /// </summary>
    [Test]
    public void AnimatedNameEffectsStopUnderReducedMotion()
    {
        string css = CssSource.MetaCss();

        List<string> blocks = CssSource.ReducedMotionBlocks(css);
        string reduced = string.Join("\n", blocks);

        Assert.That(blocks, Is.Not.Empty, "meta-shell.css carries no prefers-reduced-motion block at all");
        Assert.That(reduced, Does.Contain(".m-name--name-prism").And.Contain(".m-name--name-bloom"),
            "an animated name effect is not stopped under reduced motion");
        Assert.That(reduced, Does.Contain(".m-name--name-frost"),
            "the frost's snow is not hidden under reduced motion");
        Assert.That(reduced, Does.Contain("animation: none"), "the premium effects are halted by something other than animation: none");
        Assert.That(reduced, Does.Contain("display: none"), "the frost's snow is hidden by something other than display: none");
    }

    #endregion
}
