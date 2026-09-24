using Game.Logic;
using System;
using System.Collections.Generic;

namespace Game.Client.Services;

/// <summary> How a baked card illustration is composed over the shared card frame. </summary>
public enum CardArtTreatment
{
    /// <summary> A clan backdrop with the config emoji standing in for an illustration. </summary>
    Fallback,
    /// <summary> A transparent critter cutout that may extend beyond the clipped art window. </summary>
    CritterCutout,
    /// <summary> A complete illustration clipped inside the art window. </summary>
    FullBleed,
}

/// <summary> Client-baked artwork for one card, plus the clan backdrop every critter sits over. </summary>
public sealed record CardArtSet(
    string BackdropSrc,
    string? IllustrationSrc,
    CardArtTreatment Treatment,
    string CompositionClass,
    bool IsFallback);

/// <summary>
/// Maps the stable gameplay <see cref="CardId"/> to artwork shipped with this client build.
/// <para>
/// Gameplay configuration may add or rebalance cards over the air. An unfamiliar id therefore resolves to
/// a complete clan-coloured fallback instead of requesting an asset that cannot exist until the next client
/// release.
/// </para>
/// </summary>
public static class CardArtCatalog
{
    static readonly IReadOnlyDictionary<string, CardArtSet> CardArt =
        new Dictionary<string, CardArtSet>(StringComparer.Ordinal)
        {
            ["BiscuitHound"]     = Cutout("hearth-knot", "biscuit-hound"),
            ["BoulderBoar"]      = Cutout("rising-rings", "boulder-boar"),
            ["DumpsterBandit"]  = Cutout("returning-crescent", "dumpster-bandit"),
            ["GardenSnail"]      = Cutout("waystar", "garden-snail"),
            ["PebbleCollector"] = Cutout("tidal-eye", "pebble-collector"),
            ["SizzleWhisker"]    = Cutout("split-flame", "sizzle-whisker"),
            ["WarmBiscuit"]      = FullBleed("warm-biscuit"),
            ["EmberKit"] = Cutout("split-flame", "ember-kit"),
            ["Foxfire"] = FullBleed("foxfire"),
            ["FlameDancer"] = Cutout("split-flame", "flame-dancer"),
            ["CinderStorm"] = FullBleed("cinder-storm"),
            ["NineTailMatriarch"] = Cutout("split-flame", "nine-tail-matriarch"),
            ["TideScholar"] = Cutout("tidal-eye", "tide-scholar"),
            ["Slipstream"] = FullBleed("slipstream"),
            ["BubbleDrifter"] = Cutout("tidal-eye", "bubble-drifter"),
            ["Undertow"] = FullBleed("undertow"),
            ["Riptide"] = FullBleed("riptide"),
            ["AcornHoard"] = FullBleed("acorn-hoard"),
            ["MossyYearling"] = Cutout("rising-rings", "mossy-yearling"),
            ["HoneyFeast"] = FullBleed("honey-feast"),
            ["AcornForager"] = Cutout("rising-rings", "acorn-forager"),
            ["OldMossback"] = Cutout("rising-rings", "old-mossback"),
            ["SunbeamRetriever"] = Cutout("hearth-knot", "sunbeam-retriever"),
            ["PackCheer"] = FullBleed("pack-cheer"),
            ["SheepdogShepherd"] = Cutout("hearth-knot", "sheepdog-shepherd"),
            ["DawnShepherd"] = Cutout("hearth-knot", "dawn-shepherd"),
            ["MoonlitAlleycat"] = Cutout("returning-crescent", "moonlit-alleycat"),
            ["SmokeBomb"] = FullBleed("smoke-bomb"),
            ["MoonlightSeance"] = FullBleed("moonlight-seance"),
            ["WhiskerThief"] = Cutout("returning-crescent", "whisker-thief"),
            ["RaccoonRingleader"] = Cutout("returning-crescent", "raccoon-ringleader"),
            ["MeadowMouse"] = Cutout("waystar", "meadow-mouse"),
            ["BusyBeaver"] = Cutout("waystar", "busy-beaver"),
            ["TrailRabbit"] = Cutout("waystar", "trail-rabbit"),
            ["RiverDuck"] = Cutout("waystar", "river-duck"),
            ["PondFrog"] = Cutout("waystar", "pond-frog"),
            ["WiseTortoise"] = Cutout("waystar", "wise-tortoise"),
            ["PricklyHedgehog"] = Cutout("waystar", "prickly-hedgehog"),
            ["StrayGoat"] = Cutout("waystar", "stray-goat"),
            ["HillPony"] = Cutout("waystar", "hill-pony"),
            ["GreyOwl"] = Cutout("waystar", "grey-owl"),
            ["OldBadger"] = Cutout("waystar", "old-badger"),
            ["MooseWanderer"] = Cutout("waystar", "moose-wanderer"),
            ["BerrySnack"] = FullBleed("berry-snack"),
            ["FieldNotes"] = FullBleed("field-notes"),
            ["TheAcorn"] = FullBleed("the-acorn"),
            ["LambToken"] = Cutout("hearth-knot", "lamb-token"),

            ["EmberEaredHare"] = Cutout("split-flame", "ember-eared-hare"),
            ["PaperLanternPrank"] = FullBleed("paper-lantern-prank"),
            ["BonfireBengal"] = Cutout("split-flame", "bonfire-bengal"),
            ["TheHundredTailTale"] = Cutout("split-flame", "the-hundred-tail-tale"),
            ["MapShellTurtle"] = Cutout("tidal-eye", "map-shell-turtle"),
            ["MoonpoolFrog"] = Cutout("tidal-eye", "moonpool-frog"),
            ["SealOfApproval"] = FullBleed("seal-of-approval"),
            ["SpringTide"] = FullBleed("spring-tide"),
            ["PocketShovelMole"] = Cutout("rising-rings", "pocket-shovel-mole"),
            ["BuriedAcorn"] = FullBleed("buried-acorn"),
            ["DeepdelverMole"] = Cutout("rising-rings", "deepdelver-mole"),
            ["HillRaiserMole"] = Cutout("rising-rings", "hill-raiser-mole"),
            ["LongdogLookout"] = Cutout("hearth-knot", "longdog-lookout"),
            ["PicnicBasket"] = FullBleed("picnic-basket"),
            ["RescueStBernard"] = Cutout("hearth-knot", "rescue-st-bernard"),
            ["HearthOfTheWholePack"] = Cutout("hearth-knot", "hearth-of-the-whole-pack"),
            ["KeyholeKitten"] = Cutout("returning-crescent", "keyhole-kitten"),
            ["PossumEncore"] = FullBleed("possum-encore"),
            ["VelvetRopeCat"] = Cutout("returning-crescent", "velvet-rope-cat"),
            ["QueenOfBorrowedThings"] = Cutout("returning-crescent", "queen-of-borrowed-things"),
        };

    /// <summary> Resolve artwork for a config card. Null and unknown identities receive the neutral fallback. </summary>
    public static CardArtSet Resolve(CardInfo? card)
        => Resolve(card?.CardId, card?.Clan?.MaybeRef?.ClanId?.Value, card?.Type ?? CardType.Critter);

    /// <summary> Resolve artwork without needing a resolved config reference, which also keeps the policy testable. </summary>
    public static CardArtSet Resolve(CardId? cardId, string? clanId, CardType cardType)
    {
        if (cardId != null && CardArt.TryGetValue(cardId.Value, out CardArtSet? art))
            return art;

        return new CardArtSet(
            BackdropSrc(clanId),
            null,
            CardArtTreatment.Fallback,
            cardType == CardType.Trick ? "card-art-fallback-trick" : "card-art-fallback-critter",
            true);
    }

    static CardArtSet Cutout(string backdrop, string illustration)
        => new(
            $"art/cards/background-{backdrop}.webp",
            $"art/cards/{illustration}.webp",
            CardArtTreatment.CritterCutout,
            $"card-art-{illustration}",
            false);

    static CardArtSet FullBleed(string illustration)
        => new(
            $"art/cards/{illustration}.webp",
            null,
            CardArtTreatment.FullBleed,
            $"card-art-{illustration}",
            false);

    static string BackdropSrc(string? clanId) => $"art/cards/background-{ClanSymbolSlug(clanId)}.webp";

    /// <summary> The clan identity is its symbol, not its current animal-themed working name. </summary>
    public static string ClanSymbolSlug(string? clanId) => clanId switch
    {
        "Kitsune"   => "split-flame",
        "Tidepool" or "Tidecallers" => "tidal-eye",
        "Mossback" or "Mosskin"     => "rising-rings",
        "Sunny" or "Sunhearts"      => "hearth-knot",
        "Moonlight" or "Moonhands"  => "returning-crescent",
        _            => "waystar",
    };
}
