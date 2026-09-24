# Offers and in-app purchases

[Documentation index](../README.md#documentation) · Read first: [`economy.md`](economy.md), [`game-config.md`](game-config.md)

The Shop sells offers built on the Metaplay SDK's
[MetaOffers](https://docs.metaplay.io/feature-cookbooks/in-game-offers/getting-started-with-in-game-offers). Some are
paid from the wallet. Others are in-app purchases on the SDK's Development purchase platform, which lets a demo
client buy without a real store. Player segments decide which offers a player is shown. This doc covers the offer
types, offer groups and placements, segments and targeting, and the purchase flow.

Offers, offer groups and in-app products are the `Offers`, `OfferGroups` and `InAppProducts` config entries, authored
in the `GameConfigSource/` sheets of the same names. The game's types are in
[`SharedCode/Offers/`](../SharedCode/Offers/) and [`SharedCode/Purchases/`](../SharedCode/Purchases/).

## Offers

### One offer type for both price kinds

[`OfferInfo`](../SharedCode/Offers/Offers.cs) holds both price kinds in one class: a row with a wallet price is sold
for currency, and a row that names an in-app product is sold as an in-app purchase. It is one class because the SDK's
config repository generator refuses an abstract library item type, so a mixed catalogue cannot use two subclasses.

### Wallet-priced offers

The client buys one with the SDK action `PlayerPurchaseInGameCurrencyMetaOffer`. `OfferInfo` implements the SDK's
in-game currency hooks so that the price and the contents settle in one wallet exchange
([`economy.md`](economy.md)).

The SDK pays the price and then consumes the offer's rewards as a separate step. That second step cannot know whether
a grant would pass a currency cap once the price is already spent. So the exchange grants the contents, and the
offer's reward type for wallet offers grants nothing.

The SDK also records the purchase after the payment hook whatever the wallet did. So the affordability hook settles
the whole exchange, not only the price, and a purchase whose grant would pass a cap is refused as `CannotAfford`
before anything is recorded. Otherwise a one-time offer would sell out with nothing paid or granted.

The Shop asks the same settlement before it draws the card, so that refusal is never something a player meets after
tapping Buy. A card draws one of three states: the purchase would settle and it offers Buy, the price is beyond the
balance and it states the shortfall, or the contents would overflow a balance and it names the one to spend down.
`OfferInfo.OverflowingCurrency` answers the third, the way the wheel's own full-wallet state does
([`spin-wheel.md`](spin-wheel.md)). "Not enough" would be the wrong sentence for a player who holds the price.

### In-app purchase offers

An in-app purchase offer carries a `DemoOfferReward`, which grants the offer's contents through the wallet when the
SDK claims a validated purchase. The product it names has dynamic content, so the product gets its contents from the
offer that is being bought.

The grant is a paid grant, which a balance cap does not refuse ([`economy.md`](economy.md)): by the time it runs, the
player has paid and the SDK has recorded the purchase. The Shop still checks the contents against the caps with
`OfferInfo.OverflowingCurrency` and names the full balance instead of offering Buy, so a player goes over a cap only
when the balance grows between the tap and the claim.

### Precursors

An offer can name a precursor offer. It then becomes available only after the precursor was purchased and the
precursor's activation ended. The SDK evaluates this from its own per-player offer state.

## Offer groups and placements

[`OfferPlacementIds`](../SharedCode/Offers/OfferPlacementIds.cs) names the Shop's two placements. **Featured** shows one
offer, and **Catalogue** lists the others ([Resolving the featured offer](#resolving-the-featured-offer)).

Offer groups are the only SDK activables the game uses. `OfferGroupsModel` on `PlayerModel` must be bound to the same
info type as the config library, because the SDK's activable set casts each info object to its own info type.

A segment-targeted group is authored as **transient**, with a finite lifetime. A transient group's activation follows
its conditions, so it ends when the player leaves the segment. Inside one open activation, the SDK can toggle a
transient group's active state without checking placement availability again. The finite lifetime ends the
activation, so qualifying again starts a new activation, which does check the placement. The SDK's offer group source
item cannot set the transient flag, so `OfferGroupSourceItem` reads it from an `IsTransient` column.

### Resolving the featured offer

`MetaStateService.ResolveFeatured` in
[`WebClient/Services/MetaStateService.cs`](../WebClient/Services/MetaStateService.cs) walks the featured groups in
the SDK's own priority order and picks the first active one. This keeps the featured slot to one offer when two
transient groups report active at once, and lets a targeted group outrank an untargeted fallback. The catalogue shows
a status for each of its offers, including sold-out and locked ones, and leaves out the one the featured slot shows.
The client refreshes the offers when the Shop opens.

The config build checks offers and groups against this design (`GameConfigValidation`). For example, each in-app
purchase offer sells once per player, and two groups on one placement cannot share a priority.

## Segment targeting

An offer or an offer group can name a player segment. The player turns personalized offers off and on from the Shop,
and the shipped targeting segments all require that setting, so with it off only untargeted groups activate. The
config build does not enforce this: a new targeting segment honors the opt-out only if it requires the setting too.

The client does not evaluate segment conditions. It reads the offer and group state the SDK has resolved.

## Player segments

Segments use the SDK's
[player segments](https://docs.metaplay.io/feature-cookbooks/player-segments/implementing-player-segments). They are
rows in the `PlayerSegments` config entry, each a set of requirements on typed player properties, combined with AND.
The game adds no segment type or columns of its own. Segment ids are also constants in
[`PlayerSegmentIds`](../SharedCode/Player/PlayerSegments.cs), so code and tests do not repeat the strings.

Segments can overlap, and none ranks against another. Offer group priority decides which offer a player in two
segments sees. No segment matches every player: the untargeted groups reach a player in no segment, and the config
build refuses a segment with no conditions, which would look like targeting while matching everyone. Segments are
conditions over `PlayerModel`, so the client and the server evaluate them from the same state.

Segment conditions use only the SDK's property requirements. The game defines no `PlayerCondition` subclass of its
own, because a boundary written in code cannot be retuned over the air, and the LiveOps Dashboard can show an operator
only what a condition serializes to. The config build refuses a custom condition, so the decision is enforced.

A server or client refuses to load an archive with no segments ([`game-config.md`](game-config.md#load-time)). Such an
archive would put every player in no segment, which looks like personalized offers being switched off.

### Player properties

Each property is a `TypedPlayerPropertyId<T>` subclass that reads `PlayerModel`, kept beside the feature whose state it
reads. A sheet names a property by its name, resolved by `ParsePlayerPropertyId` in
[`GameConfigParsers.cs`](../SharedCode/GameConfigs/GameConfigParsers.cs), so a new property needs a line there before a
sheet can use it.

- **Account age** counts elapsed 24-hour periods, not local calendar days. A calendar count depends on the player's
  UTC offset, which each login refreshes.
- **Validated purchases** counts the fixed-content products `PlayerModel.OnClaimedInAppProduct` has granted. Purchases
  of dynamic-content products, the ones wrapped by offers, are not counted. It does not read the SDK's full purchase
  history, because that member is server-only and a condition over it would evaluate differently on the client and
  the server.
- **Games played, games won and has customized name** read `PlayerModel.TargetingFacts`, a checksummed copy of the
  record and the rename state. The originals are written by unsynchronized server actions and are excluded from the
  checksum ([`player.md`](player.md#which-members-are-excluded-from-the-checksum)). A client action such as the
  Shop's offer refresh writes checksummed offer state from the segments, so a segment that read the originals could
  resolve differently on the client and the server. The server copies the originals with the synchronized
  `PlayerTargetingFactsSynced` after each action that moves them, so both sides see the new value at the same
  timeline position.
- The display name is not a property. Free-form player text is not used as a targeting or analytics dimension.

## In-app products

[`DemoInAppProductInfo`](../SharedCode/Purchases/DemoPurchases.cs) exists only for the Development platform: it has
no real store ids, and its development id is derived from the product id. Every product is consumable, so the SDK
refuses a second use of the same transaction.

A product with fixed contents is granted once per player by `PlayerModel.OnClaimedInAppProduct`, which records a
`ResolvedWalletBundle` on the purchase so the LiveOps Dashboard shows what was granted. A dynamic-content product
grants nothing there, because its offer grants the contents.

The Development platform accepts a purchase only when `Environment:EnableDevelopmentFeatures` is on. Its receipt
proves only that the receipt is well formed, not that a payment happened. In offline mode, the SDK's offline server
accepts every purchase without validating it.

## Purchase flow

The client code is in [`WebClient/Services/MetaplayClientService.cs`](../WebClient/Services/MetaplayClientService.cs).
The client first prepares the purchase, which makes the offer the product's pending dynamic content. Once the server
confirms that, the client writes a demo receipt with [`DemoPurchaseReceipt`](../SharedCode/Purchases/DemoPurchases.cs)
and reports the purchase. Validation is entirely the SDK's: the platform checks the receipt, and a transaction id
already used anywhere in the database is refused. On success the client claims the purchase, which grants the offer's
contents and records the purchase, so the offer sells out at its limit. No game action grants in-app purchase
contents without a validated receipt.

### Pending purchase sweep

The SDK does not deliver a verdict again. Without help, a purchase validated after the client closed would never be
granted and would keep its pending slot. So on session start, `FinishPendingInAppPurchases` claims every validated
purchase and clears every reused receipt. It leaves purchases still awaiting validation to the server.
