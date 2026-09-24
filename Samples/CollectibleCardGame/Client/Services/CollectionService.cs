using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Client.Services;

/// <summary>
/// The client half of the account's collection: the card catalogue as this player sees it, the decks they have
/// saved, their lock slots, and the actions that change any of it. One client service per subsystem, each
/// implementing the observer handle for the part of the model it owns — this one is
/// <see cref="ICollectionChangeObserver"/>.
/// <para>
/// Every mutating method dry-runs its action against the local model first and returns the refusal reason
/// instead of sending an action the server would refuse. The prediction is the action's own validation, so
/// the client and the server cannot disagree about what is legal.
/// </para>
/// </summary>
public class CollectionService : ICollectionChangeObserver
{
    readonly MetaplayClientService _client;

    /// <summary>
    /// Raised when the collection, the decks or the lock slots changed. Screens that only draw the collection
    /// subscribe here rather than to the coarser connection-wide state event.
    /// </summary>
    public event Action? OnCollectionChanged;

    public CollectionService(MetaplayClientService client)
    {
        _client = client;
    }

    PlayerModel? Player => _client.PlayerModel;

    /// <summary> The game config the session is running against, or null before one has started. </summary>
    public SharedGameConfig? GameConfig => Player?.GameConfig;

    /// <summary> Whether there is a player model to read. Every accessor below returns an empty answer until then. </summary>
    public bool HasPlayer => Player != null;

    public int CardGiftInterval => GameConfig?.Global.MatchesPerCardGift ?? 0;
    public int CardGiftProgress => Player?.CardGiftProgress ?? 0;
    public bool CardGiftIncludesPractice => GameConfig?.Global.CardGiftIncludesPractice ?? false;
    public CardInfo? UnseenCardGift => Player?.UnseenCardGift is CardId id ? GameConfig?.Cards.TryGetValue(id, out CardInfo card) == true ? card : null : null;
    public bool HasUnownedCards => Player != null && CardGiftPolicy.Candidates(Player).Count > 0;
    public void ClaimReadyCardGift()
    {
        if (Player != null && CardGiftPolicy.IsReady(Player))
            _client.ExecuteAction(new PlayerClaimCardGift());
    }
    public void DismissCardGift()
    {
        if (Player?.UnseenCardGift is CardId id)
            _client.ExecuteAction(new PlayerDismissCardGift(id));
    }

    #region The catalogue

    // The catalogue is fixed for the life of a config, and the screens read it several times per render, so
    // it is built once per config rather than per access. Identity of the config object is the cache key: a
    // config update hands out a different instance and the lists rebuild.
    SharedGameConfig? _catalogueConfig;
    IReadOnlyList<CardInfo>        _allCards     = Array.Empty<CardInfo>();
    IReadOnlyList<ClanInfo>        _allClans     = Array.Empty<ClanInfo>();
    IReadOnlyList<StarterDeckInfo> _starterDecks = Array.Empty<StarterDeckInfo>();

    void EnsureCatalogue()
    {
        SharedGameConfig? config = GameConfig;
        if (ReferenceEquals(config, _catalogueConfig))
            return;

        _catalogueConfig = config;
        _allCards = config == null
            ? Array.Empty<CardInfo>()
            : config.Cards.Values.Where(card => card.Collectible).ToList();
        _allClans = config == null
            ? Array.Empty<ClanInfo>()
            : config.Clans.Values.ToList();
        _starterDecks = config == null
            ? Array.Empty<StarterDeckInfo>()
            : config.StarterDecks.Values.ToList();
    }

    /// <summary>
    /// Every collectible card, in catalogue order. Unowned cards are included: an unowned card is a card worth
    /// heisting, so the browser shows what is out there rather than hiding it.
    /// <para>
    /// The same instance for as long as the config is, so a screen may compare it by reference to know whether
    /// anything it derived from it needs rebuilding.
    /// </para>
    /// </summary>
    public IReadOnlyList<CardInfo> AllCards
    {
        get
        {
            EnsureCatalogue();
            return _allCards;
        }
    }

    /// <summary> Every clan, for the filter row. </summary>
    public IReadOnlyList<ClanInfo> AllClans
    {
        get
        {
            EnsureCatalogue();
            return _allClans;
        }
    }

    public CardInfo? GetCard(CardId cardId)
    {
        SharedGameConfig? config = GameConfig;
        if (config == null || cardId == null)
            return null;

        return config.Cards.TryGetValue(cardId, out CardInfo card) ? card : null;
    }

    public bool Owns(CardId cardId) => cardId != null && Player?.Collection.ContainsKey(cardId) == true;

    /// <summary> This account's rank for the card, or 0 when it does not own one. </summary>
    public int RankOf(CardId cardId)
    {
        PlayerModel? player = Player;
        if (player == null || cardId == null)
            return 0;

        return player.Collection.TryGetValue(cardId, out int rank) ? rank : 0;
    }

    public int OwnedCount => Player?.Collection.Count ?? 0;


    public PlayerRecord? Record => Player?.Record;

    #endregion

    #region Locks

    public int LockSlotCount => Player?.LockSlotCount ?? 0;

    /// <summary> The card locked in the slot, or null when the slot is empty. </summary>
    public CardId? LockedIn(int slotIndex)
    {
        PlayerModel? player = Player;
        if (player == null)
            return null;

        return player.LockSlots.TryGetValue(slotIndex, out CardId cardId) ? cardId : null;
    }

    /// <summary> Which slot holds the card, or -1 when it is not locked. </summary>
    int SlotOf(CardId cardId)
    {
        PlayerModel? player = Player;
        if (player == null || cardId == null)
            return -1;

        foreach ((int slot, CardId locked) in player.LockSlots)
        {
            if (locked == cardId)
                return slot;
        }

        return -1;
    }

    public bool IsLocked(CardId cardId) => Player?.IsLocked(cardId) == true;

    /// <summary>
    /// How many slots currently hold a card. The map is sparse and only ever keyed by a real slot index, so
    /// its count is the answer — every screen that shows "N of M locked" reads this one.
    /// </summary>
    public int LockedCount => Player?.LockSlots.Count ?? 0;

    /// <summary> The lowest empty slot, or -1 when every slot is taken. </summary>
    public int FirstFreeSlot()
    {
        PlayerModel? player = Player;
        if (player == null)
            return -1;

        for (int slot = 0; slot < player.LockSlotCount; slot++)
        {
            if (!player.LockSlots.ContainsKey(slot))
                return slot;
        }

        return -1;
    }

    /// <summary>
    /// The lowest rank a card may be locked at, from game config. A fresh account is entirely below it, which
    /// is the point: at the rank floor a lock freezes nothing and only denies a winner their Heist pick.
    /// </summary>
    public int MinLockRank => GameConfig?.Global.MinLockRank ?? 1;

    /// <summary> Whether this card's own rank clears <see cref="MinLockRank"/>. The same rule the action runs. </summary>
    public bool CanLock(CardId cardId) => cardId != null && RankOf(cardId) >= MinLockRank;

    /// <summary>
    /// Lock a card into a slot. Locking, unlocking and moving a lock are all the same action, so a card that is
    /// already locked elsewhere simply moves.
    /// </summary>
    public string? SetLockSlot(int slotIndex, CardId? cardId) => Execute(new PlayerSetLockSlot(slotIndex, cardId));

    /// <summary>
    /// Set a card's rank. <b>Development-only</b>: nothing in the game moves a rank yet, so this is the only
    /// way a screen can reach a rule that turns on one. Its affordance is query-string gated.
    /// </summary>
    public string? DevSetCardRank(CardId cardId, int rank) => Execute(new PlayerDevSetCardRank(cardId, rank));

    /// <summary> Clear whichever slot holds the card. A card that is not locked is left alone. </summary>
    public string? Unlock(CardId cardId)
    {
        int slot = SlotOf(cardId);
        return slot < 0 ? null : SetLockSlot(slot, null);
    }

    #endregion

    #region Decks

    /// <summary>
    /// The saved decks, in the order they were created. That is also id order and needs no sort: ids come
    /// from a counter that only ever goes up, and the map keeps insertion order, so a new deck is appended
    /// and an overwrite stays where it was. Handed out as the model's own map rather than a copy, since the
    /// deck list reads it several times per render.
    /// </summary>
    public MetaDictionary<int, PlayerDeck> Decks => Player?.Decks ?? EmptyDecks;

    static readonly MetaDictionary<int, PlayerDeck> EmptyDecks = new MetaDictionary<int, PlayerDeck>();

    public int DeckCount => Player?.Decks.Count ?? 0;

    /// <summary>
    /// The config-authored starter decks, in sheet order. Empty before a session exists. Cached on config
    /// identity like the catalogue, because the picker reads the list on every render.
    /// </summary>
    public IReadOnlyList<StarterDeckInfo> StarterDecks
    {
        get
        {
            EnsureCatalogue();
            return _starterDecks;
        }
    }

    /// <summary>
    /// The deck this account last entered a match with, or null. It may name a deck that has since been
    /// deleted — or a starter deck the config no longer carries, which is the same thing — so a reader picks
    /// with <see cref="PracticeDeckPick.Choose"/> rather than using it directly.
    /// </summary>
    public DeckChoice? LastPlayedDeck => Player?.LastPlayedDeck;

    public PlayerDeck? GetDeck(int deckId)
    {
        PlayerModel? player = Player;
        if (player == null)
            return null;

        return player.Decks.TryGetValue(deckId, out PlayerDeck deck) ? deck : null;
    }

    public bool CanSaveMoreDecks => DeckCount < PlayerModel.MaxSavedDecks;

    /// <summary> The shared legality check, ownership included — the same call the save action runs. </summary>
    public DeckValidationResult ValidateDeck(IReadOnlyList<CardId> cards)
    {
        PlayerModel? player = Player;
        if (player == null)
            return DeckValidationResult.Failed(DeckValidationError.WrongSize);

        return DeckValidator.ValidateForPlayer(cards, player.GameConfig, player.Collection);
    }

    /// <summary> The shared Power Score, live as the player edits. </summary>
    public int PowerScore(IReadOnlyList<CardId> cards)
        => Player == null ? 0 : DeckValidator.ComputePowerScore(cards, Player.Collection);

    /// <summary>
    /// Save a deck, creating one when <paramref name="deckId"/> is null. Returns the refusal reason, or null
    /// when the action was sent.
    /// </summary>
    public string? SaveDeck(int? deckId, string name, IReadOnlyList<CardId> cards) => Execute(new PlayerSaveDeck(deckId, name, cards));

    public string? RenameDeck(int deckId, string name) => Execute(new PlayerRenameDeck(deckId, name));

    public string? DeleteDeck(int deckId) => Execute(new PlayerDeleteDeck(deckId));

    #endregion

    #region Sending

    /// <summary>
    /// Dry-run the action against the local model and send it only if it succeeds. Returns the refusal as
    /// player copy, or null when the action was sent.
    /// </summary>
    string? Execute(PlayerAction action)
    {
        MetaActionResult? result = _client.DryExecuteAction(action);
        if (result == null)
            return "Not connected.";

        if (!result.IsSuccess)
            return RefusalText(action, result);

        _client.ExecuteAction(action);
        return null;
    }

    /// <summary> The player copy for a refusal from one of the actions above. </summary>
    string RefusalText(PlayerAction action, MetaActionResult result)
    {
        if (result == ActionResults.InvalidLockSlot)      return "That lock slot does not exist.";
        if (result == ActionResults.CardNotOwned)         return "You do not own that card.";
        if (result == ActionResults.CardRankTooLowToLock) return $"Only rank {MinLockRank} and above can be locked.";
        if (result == ActionResults.CardIsLocked)         return "A locked card cannot change rank. Unlock it first.";
        if (result == ActionResults.InvalidCardRank)      return "That rank is out of range.";
        if (result == ActionResults.UnknownDeck)          return "That deck no longer exists.";
        if (result == ActionResults.TooManySavedDecks)    return $"You already have {PlayerModel.MaxSavedDecks} decks saved.";

        if (result == ActionResults.InvalidDeckName)
        {
            string? name = action switch
            {
                PlayerSaveDeck save     => save.Name,
                PlayerRenameDeck rename => rename.Name,
                _                       => null,
            };
            return string.IsNullOrWhiteSpace(name)
                ? "Give the deck a name."
                : $"Deck names are at most {PlayerDeck.MaxNameLength} characters.";
        }

        // Every other save refusal is a deck-legality one, and the validator names the card it tripped on.
        if (action is PlayerSaveDeck saved && Player != null)
        {
            DeckValidationResult legality = ValidateDeck(saved.Cards);
            if (!legality.IsValid)
                return DeckLegalityText.Describe(legality, Player.GameConfig);
        }

        return "That was refused.";
    }

    #endregion

    /// <summary>
    /// The model changed under a committed action. The observer hub dispatches here rather than to the root
    /// handle, so screens that draw the collection re-render and everything else is left alone.
    /// </summary>
    void ICollectionChangeObserver.GenericPropertyChanged(string propertyName) => OnCollectionChanged?.Invoke();
}
