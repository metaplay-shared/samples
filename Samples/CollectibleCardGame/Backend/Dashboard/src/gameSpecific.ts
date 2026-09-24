// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import type { App } from 'vue'

// Import the integration API.
import { OverviewListItem, setGameSpecificInitialization } from '@metaplay/core'

/**
 * How many keys an object-shaped model member has, for the overview list items below. The player model
 * arrives untyped from the admin API, and `Object.keys` refuses an `any`.
 * @param value A model member the admin API serialized as a JSON object, or anything else.
 * @returns The member's key count, or zero for anything that is not an object.
 */
function countKeys(value: unknown): number {
  if (value === null || typeof value !== 'object') return 0
  return Object.keys(value).length
}

/**
 * Sticky Paws' whole dashboard customization. This is the one entry point the SDK offers: `src/main.ts`
 * registers `GameSpecificPlugin` after the SDK's own `CorePlugin` and carries a "do not edit" banner, and it
 * and every root-level file in this project are copies of the SDK's `MetaplaySDK/Frontend/DefaultDashboard`
 * template. So all customization lives here and under `src/`.
 * @param app The Vue app instance.
 */
export function GameSpecificPlugin(_app: App): void {
  setGameSpecificInitialization(async (initializationApi) => {
    // Three cards, all in Tab0 — whose default display name is already "Game State". Placed `before` so the
    // sample's own cards sit above the SDK's Inbox and Broadcast-history cards, both empty in this sample.
    initializationApi.addUiComponent(
      'Players/Details/Tab0',
      {
        uniqueId: 'NewcomerShieldCard',
        vueComponent: async () => await import('./NewcomerShieldCard.vue'),
        width: 'full',
      },
      { position: 'before' }
    )
    initializationApi.addUiComponent(
      'Players/Details/Tab0',
      {
        uniqueId: 'CollectionCard',
        vueComponent: async () => await import('./CollectionCard.vue'),
        width: 'full',
      },
      { position: 'after', targetId: 'NewcomerShieldCard' }
    )
    initializationApi.addUiComponent(
      'Players/Details/Tab0',
      {
        uniqueId: 'DecksCard',
        vueComponent: async () => await import('./DecksCard.vue'),
        width: 'full',
      },
      { position: 'after', targetId: 'CollectionCard' }
    )

    // Everything an operator wants at a glance, above the tabs and without opening a card. This is the
    // highest value per line of code in the whole customization.
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asNumber('Rating', (player) => player.model.record.rating),
      { position: 'after', targetId: 'Joined' }
    )
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asString(
        'Ranked W/L/D',
        (player) =>
          `${player.model.record.rankedWins} / ${player.model.record.rankedLosses} / ${player.model.record.rankedDraws}`
      ),
      { position: 'after', targetId: 'Rating' }
    )
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asNumber('Ranked matches', (player) => player.model.record.rankedMatchesPlayed),
      { position: 'after', targetId: 'Ranked W/L/D' }
    )
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asNumber('Unranked matches', (player) => player.model.record.unrankedMatchesPlayed),
      { position: 'after', targetId: 'Ranked matches' }
    )
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asNumber('Collection size', (player) => countKeys(player.model.collection)),
      { position: 'after', targetId: 'Unranked matches' }
    )
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asNumber('Saved decks', (player) => countKeys(player.model.decks)),
      { position: 'after', targetId: 'Collection size' }
    )
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asString(
        'Lock slots used',
        (player) => `${String(countKeys(player.model.lockSlots))} / ${String(player.model.lockSlotCount ?? 0)}`
      ),
      { position: 'after', targetId: 'Saved decks' }
    )

    // The closest thing the account has to a match history, and a model-size hazard worth watching: the set
    // grows one entry per delivered ranked result and is never pruned. The entries themselves are already in
    // the raw model dump on the Technical tab; a list of four hundred entity ids would help nobody.
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asNumber('Match results applied', (player) => (player.model.appliedMatchResults ?? []).length),
      { position: 'after', targetId: 'Lock slots used' }
    )

    // One of the two answers to "why can't this account queue". `EntityId.None` serializes as the string
    // 'None', so that is what an idle account has to be read for.
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asString(
        'At a table',
        (player) => {
          const currentMatch = String(player.model.currentMatch ?? 'None')
          return currentMatch === 'None' ? '—' : currentMatch
        },
        undefined,
        true
      ),
      { position: 'after', targetId: 'Match results applied' }
    )

    // Card ids are opaque strings everywhere the dashboard renders one — the raw model dump, the generated
    // forms, the audit log. One decorator makes every one of them read as the card's name at once.
    const gameData = await initializationApi.getGameDataByLibrary(['Cards'])
    initializationApi.addStringIdDecorator('Game.Logic.CardId', (stringId: string): string => {
      const displayName = gameData.gameConfig.Cards?.[stringId]?.displayName
      return displayName ? `${String(displayName)} (${stringId})` : stringId
    })

    // Deliberately **no** `addPlayerResources`: this game has no currency. Rating and the ranked record are
    // the account-level progression it has, and they are the list items above.
  })
}
