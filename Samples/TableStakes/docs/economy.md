# Economy

[Documentation index](../README.md#documentation) · Read first: [`player.md`](player.md), [`game-config.md`](game-config.md)

This doc covers the three currencies, the player's wallet, the one method that changes a balance, where currency
comes from and goes, and the checks that keep the economy playable. Prices and reward amounts are in game config.

## Currencies

`CurrencyType` in [`SharedCode/GameConfigs/Currencies.cs`](../SharedCode/GameConfigs/Currencies.cs) has three values:

- **Coins**, the everyday earned currency and the main cosmetic price.
- **Gems**, the premium currency, for premium cosmetics and some offers.
- **Spin tokens**, which buy spins of the wheel and nothing else.

Currency identities are code, because every stored wallet and every feature has to agree on them. Amounts are config,
written as `CurrencyAmount` (one currency) and `RewardBundle` (one or more currencies, each at most once).

Matches have no entry fee and grant no currency.

## The wallet

[`PlayerWalletModel`](../SharedCode/Economy/PlayerWalletModel.cs) holds the three balances. It is checksummed, because
every writer runs at the same timeline position on client and server.

The wallet is immutable. A change is computed as a complete new wallet and applied by one assignment, so a
half-applied transaction cannot exist.

Each balance has a cap in `Global` config. A grant that would exceed a cap is refused whole. It is never clamped.
The one exception is an in-app purchase grant (`WalletTransaction.PaidGrant`). The SDK records a validated purchase
whatever the wallet does, so refusing it would take the player's money and grant nothing. A paid grant may leave a
balance above its cap, and ordinary grants of that currency are then refused until the player spends below it.

### Transactions

A [`WalletTransaction`](../SharedCode/Economy/WalletTransaction.cs) is a request. It names the feature that moves the
balance, the reason, the config id it is about (never player data), and the amounts spent and granted. A transaction
grants, spends, or exchanges one for the other in one step, as the spin wheel and wallet-priced offers do. Features
and reasons are closed enums, so the reasons an analyst filters on are a fixed list.

`PlayerWalletModel.Settle` is a pure function that computes the result of a transaction and writes nothing. Spends
settle before grants, so a purchase is affordable on the balance the player holds, not on the balance its reward
would give. A transaction is refused whole if it is malformed, the player cannot afford it, or a grant would pass a
cap that applies to it.

## ApplyWallet

`PlayerModel.ApplyWallet` is the only method that changes a balance. Every model action runs twice on each side, a
dry run and the real run. A feature calls `ApplyWallet` on both passes, before writing its own state, and returns
early if the wallet refuses. On the real run it also emits its own analytics event with the same correlation id
([`analytics.md`](analytics.md#correlation-id)).

`ApplyWallet` takes the transaction rather than a settlement computed earlier, so it always settles against the
wallet as it is at that moment. On a committed success it assigns the new wallet and writes the currency rows. The
wallet does not deduplicate. Each feature decides whether its own claim already happened.

`PlayerModel.PreviewWallet` settles without applying. Screens use it to show a shortfall, and the server uses it to
refuse a claim before enqueuing an action that would fail.

### Where ApplyWallet may be called

The wallet is checksummed, so `ApplyWallet` may run only in a client action, a synchronized server action, or an SDK
path that runs on the player timeline, such as an offer purchase or a purchase claim. It must not run in an
unsynchronized server action ([`player.md`](player.md#action-base-classes)). The match-completion context has no
wallet, so a match-completion observer cannot grant.

## Where currency comes from and goes

Every source and sink after account creation is a call to `ApplyWallet`:

- **Sources:** daily rewards, the first-week event, missions, the weekly event, the tournament, the spin wheel's
  prizes, offers and in-app purchases.
- **Sinks:** cosmetics, the spin wheel's token cost and wallet-priced offers.

A new source or sink is another caller of `ApplyWallet`, with a new feature or reason value if needed. Each feature's
doc describes its own grants.

The starting wallet comes from `Global` config. Account creation settles it against an empty wallet with the normal
caps and rules (`PlayerWalletModel.Starting`), so changing it affects new players only. Its analytics rows are
written on the account's first login ([`player.md`](player.md)).

Segments and offer conditions read the balances through player properties, so they see the same balance the player
sees ([`offers.md`](offers.md)).

## Config build checks

Two economy decisions span several libraries, so the config build checks them in
[`GameConfigValidation`](../SharedCode/GameConfigs/GameConfigValidation.cs) rather than in any one item.

The first session can use both everyday sinks, a spin and a coin-priced cosmetic, while gems stay the currency a new
player cannot spend yet. A starting wallet that bought a gem cosmetic outright would make the premium band a second
coin band.

Every price must be earnable on the Events hub, because the insufficient-funds screens send the player there. Only
features that every player has at every moment count. The first-week event, the weekly event and the tournament do
not.

## Analytics

The wallet writes one `economy_transaction` row per currency moved on a committed settlement. A source and a sink of
the same currency are never netted into one row. A spend the player could not afford writes `economy_spend_rejected`.
A cap or malformed refusal is a defect and writes nothing. The wallet does not write the feature's own event. The
calling action emits it with the same correlation id. Event rules are in [`analytics.md`](analytics.md).
