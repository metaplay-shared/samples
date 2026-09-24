# Cosmetics

[Documentation index](../README.md#documentation) · Read first: [`player.md`](player.md), [`economy.md`](economy.md), [`game-config.md`](game-config.md)

A player has three cosmetic slots: an avatar, a frame around it, and an effect on their name. Cosmetics are bought
with coins or gems in the Cosmetics screen, or earned from the seasonal tournament, and other players see them in the
tournament standings and at the table. This doc covers the catalogue, the player's wardrobe, and how equipped
cosmetics reach other players. Cosmetics are implemented by the game on `PlayerModel`.

## Catalogue

The `Cosmetics` config entry holds the catalogue as `CosmeticInfo` items
([`SharedCode/GameConfigs/Cosmetics.cs`](../SharedCode/GameConfigs/Cosmetics.cs)), authored in
`GameConfigSource/Cosmetics.csv`. Each item has a slot, a style, and either a price in coins or gems or a note on how
it is earned.

The catalogue is append-only, because ownership is permanent and a removed item would orphan every wardrobe that
holds it ([`game-config.md`](game-config.md#editing-rules)). The config build keeps the catalogue consistent with the
rest of the design, for example a tournament prize is never also sold in the shop.

### Styles

A style is one rendering rule the client implements. `CosmeticStyles` gives each style its slot and the token the
client draws with: a CSS class for a frame or a name effect, and a glyph for an avatar.

Each item names its style explicitly, and the client never derives a class from an item id. An id-derived class that
matches no CSS rule renders as nothing, and no browser reports it. With an explicit style, an item's id and its look
can differ. `CosmeticsPolicy.StyleTokenOf` in
[`WebClient/Meta/CosmeticsPolicy.cs`](../WebClient/Meta/CosmeticsPolicy.cs) is the one place the client translates an
item into a token.

A name effect styles the name, and the name itself is always rendered as text.

## Wardrobe

[`PlayerCosmeticsState`](../SharedCode/Cosmetics/PlayerCosmeticsState.cs) holds every item the player owns, the item
equipped in each slot, and the items granted without the player asking. `PlayerModel` is its only writer. The
wardrobe is the single answer to whether a player owns a cosmetic. The tournament keeps its own record of what each
season paid, but nothing reads that record for ownership.

The wardrobe is checksummed, so every writer is a client action or a synchronized server action
([`player.md`](player.md#action-base-classes)). Cosmetics earned before the wardrobe existed were copied into it by a
schema migration ([`player.md`](player.md#adding-state-to-existing-accounts)).

**Buying** is a client action, because it has no server secret: the price, the slot and whether the item is for sale
come from config that both copies hold, never from the action. It pays through the wallet
([`economy.md`](economy.md)), and the bought item is equipped at once.

**Equipping** takes the slot from the item's kind, so an action cannot put an item in the wrong slot. It fires the
public identity hook, so the tournament standings pick up the change ([`player.md`](player.md#public-identity)).

**Tournament grants.** A placement reward's cosmetic is added to the wardrobe unworn and marked unacknowledged. It
is acknowledged when the player opens the Cosmetics screen. Until then the Profile tab shows a badge.

### The starting three

[`CosmeticDefaults`](../SharedCode/Cosmetics/CosmeticDefaults.cs) names three catalogue items that make up the default
look: a spade avatar, a plain frame and plain name text. They are never purchasable. `PlayerModel.GrantDefaultCosmetics`
gives a new player all three and equips each in its slot, beside the starting wallet.

**Every slot therefore holds an item the player owns** from the moment the account exists. The wardrobe is an
inventory as well as a shop: a slot changes by equipping something else, and the starting look is one of the things
the player can equip. The three are drawn exactly as an empty slot is drawn, so a model from before they existed looks
the same.

They cannot be bought, and the bot draws skip them for the same reason they skip every item that is not for sale
([`bots.md`](bots.md#cosmetics)). A schema migration grants them to accounts created before they existed and fills
any empty slot. A slot that holds a purchase is left alone, because a migration never changes what somebody wears.

### No unequip

There is no unequip action and no unequip control. Every slot always holds something the player owns, so there is
nothing to empty a slot to. To change what is worn, the player equips another item, including one of the starting
three.

## How other players see cosmetics

`PlayerModel.BuildPublicIdentity` puts the three equipped items in `PlayerPublicIdentity`
([`player.md`](player.md#public-identity)), and every surface that draws somebody else reads that one type. Each
surface turns the items into style tokens through `CosmeticsPolicy` and draws them with the same `PlayerAvatar` and
`PlayerName` components, so an item looks the same wherever it is worn. An empty slot draws the client's default.

Computer players, at a table and in the tournament standings, are dressed from the catalogue too, but never in a
frame, so the champion frame stays something only a season win produces ([`bots.md`](bots.md#cosmetics)).

### In the standings

The tournament carries the identity as its division avatar, and equipping refreshes it
([`player.md`](player.md#public-identity)).

### At the table

A match seat carries the same identity, pinned for the whole game ([`match.md`](match.md#seat-identity)), so the
seat plaque and the results board draw the same picture.
