using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Game-wide tunables: the numbers the match, the deck rules and the collection are built on. Values come
    /// from <c>Docs/game-design.md</c>; the config build checks them against each other (an opening hand that fits
    /// the hand limit, a deck that covers the opening draw, and so on).
    /// <para>
    /// Engine pacing is deliberately absent. Timings are host-supplied parameters, never config, so that unit
    /// tests and the self-play harness can zero them without touching content.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class GlobalConfig : GameConfigKeyValue<GlobalConfig>
    {
        /// <summary> Starting and maximum Den health. Reducing the enemy Den to zero wins the match. </summary>
        [MetaMember(1)]  public int DenStartingHp = 125;

        /// <summary> Cards in a legal deck. Exactly this many, singleton. </summary>
        [MetaMember(2)]  public int DeckSize = 25;

        /// <summary> How many clan-limited clans one deck may draw from. Wanderers never count. </summary>
        [MetaMember(3)]  public int MaxClansPerDeck = 2;

        /// <summary> Board width per side. A full board refuses critter plays, and effect summons onto it fizzle. </summary>
        [MetaMember(4)]  public int MaxBoardCritters = 6;

        /// <summary> Hand limit. An overflowing draw goes to the bottom of the deck, never burned. </summary>
        [MetaMember(5)]  public int MaxHandSize = 9;

        [MetaMember(6)]  public int OpeningHandFirstPlayer = 3;

        /// <summary> The second player's larger opening hand, on top of which they get the bonus card. </summary>
        [MetaMember(7)]  public int OpeningHandSecondPlayer = 4;

        /// <summary>
        /// The second-player compensation card, granted when the mulligan resolves and never present in a
        /// deck.
        /// </summary>
        [MetaMember(8)]  public MetaRef<CardInfo> SecondPlayerBonusCard;

        /// <summary> Maximum mana before the first turn start. Both seats gain their first mana on their own first turn. </summary>
        [MetaMember(9)]  public int StartingMaxMana = 0;

        /// <summary> Maximum mana gained at each of a seat's own turn starts. There is no cap. </summary>
        [MetaMember(10)] public int ManaGainPerTurn = 1;

        [MetaMember(11)] public int DrawsPerTurn = 1;

        /// <summary> How many times a player may replace part of their opening hand. </summary>
        [MetaMember(12)] public int MulligansPerPlayer = 1;

        /// <summary> Den damage on the first refused draw. Refusals include a draw into a full hand. </summary>
        [MetaMember(13)] public int TuckeredOutFirstDamage = 5;

        /// <summary> How much each further refused draw adds to the Tuckered Out damage. </summary>
        [MetaMember(14)] public int TuckeredOutIncrement = 5;

        /// <summary> The rank floor. A card is never removed from a collection; it bottoms out here. </summary>
        [MetaMember(15)] public int RankMin = 1;

        /// <summary> The rank ceiling. A pick of a card already held here gains nothing at all. </summary>
        [MetaMember(16)] public int RankMax = 5;

        /// <summary> Lock slots a new account starts with. More are earned through account progression. </summary>
        [MetaMember(17)] public int InitialLockSlots = 2;

        /// <summary>
        /// The lowest rank a card may be locked at. A card at the rank floor is already protected by the floor
        /// and has no growth to freeze, so a lock on one is a free denial play: it takes the card off the
        /// winner's Heist menu at no cost to the owner, and heist-acquisition is how the economy spreads cards
        /// in exactly the era every card is at the floor (<c>Docs/game-design.md</c>, "Locks"). At 2 every lock
        /// costs something real.
        /// </summary>
        [MetaMember(18)] public int MinLockRank = 2;

        /// <summary>
        /// One point of the old, coarse stat domain, in the units the game now counts health and hit points
        /// in. Every hand-authored body, damage and heal is a multiple of it; rank tracks can use finer
        /// increments to give differently sized bodies proportionate growth. It
        /// is not an engine rule — nothing divides by it — but a content one: the validator uses it to tell a
        /// number authored in the old domain from a deliberately fine-grained one.
        /// </summary>
        [MetaMember(19)] public int StatQuantum = 5;

        /// <summary>
        /// The Power Score gap — the two decks' scores subtracted — above which a match is no longer Even and
        /// the stakes tier goes asymmetric (<c>Docs/game-design.md</c>, "Ranks"). Design-fixed and explicitly
        /// untuned. The gap is in ranks, which the 5× stat domain deliberately did not scale, so a 25-card
        /// deck still scores 25-125 and this number means what game-design.md says it means.
        /// </summary>
        [MetaMember(20)] public int PowerScoreGapThreshold = 15;

        /// <summary>
        /// A player's first N ranked matches — bot-fallback matches included — run at the newcomer shield
        /// tier: no rank moves in either direction. A shield on either side shields the match, because what
        /// one player stands to win is what the other stands to lose.
        /// </summary>
        [MetaMember(21)] public int NewcomerShieldMatches = 2;

        /// <summary>
        /// The rating a new account is seeded at. Matchmaking's pairing axis — it is a number the queue orders
        /// on, never a ladder position.
        /// </summary>
        [MetaMember(22)] public int InitialRating = 1000;

        /// <summary>
        /// The fixed K-factor of the rating update: the most one match can move a rating, before the expected
        /// score scales it. The whole formula can be replaced; the queue only ever reads
        /// <c>PlayerRecord.Rating</c> as an opaque ordering.
        /// </summary>
        [MetaMember(23)] public int RatingKFactor = 32;

        /// <summary> Completed matches per new-card gift. Zero disables the track. </summary>
        [MetaMember(24)] public int MatchesPerCardGift = 3;
        /// <summary> Whether practice matches also advance the new-card gift track. </summary>
        [MetaMember(25)] public bool CardGiftIncludesPractice = true;

        /// <summary> The larger of the two opening hands, which the deck must be able to cover. </summary>
        public int MaxOpeningHand => OpeningHandFirstPlayer > OpeningHandSecondPlayer ? OpeningHandFirstPlayer : OpeningHandSecondPlayer;
    }
}
