<template lang="pug">
MCard(
  :is-loading="!playerData || !gameData"
  data-testid="sticky-paws-collection-card"
  )
  template(#title)
    span(data-testid="card-title") Collection
    MBadge(
      variant="neutral"
      shape="pill"
      class="tw-relative tw-bottom-[1px]"
      data-testid="collection-summary-badge"
      ) {{ ownedCount }}/{{ collectibleCount }} owned · {{ ranksAboveFloor }} above rank {{ rankMin }}

  template(#subtitle)
    | Every collectible card, with the rank this account holds it at. Ranks above the floor are the Heist's
    | only visible trace: nothing else moves a rank after the starter grant.

  //- Every number this card compares against comes from the Global library, so with it missing there is
  //- nothing to say. A fallback constant would be worse than useless here: RankMin, RankMax and MinLockRank
  //- happen to be 1, 5 and 2, so a defaulted read renders a card that looks entirely correct and is not.
  MErrorCallout(
    v-if="gameData && !globalConfig"
    :error="configUnavailable"
    data-testid="collection-config-unavailable"
    )

  MHorizontalDictionary(
    v-if="globalConfig"
    :items="lockSlotItems"
    class="tw-mb-3"
    )

  MList(
    v-if="globalConfig"
    show-border
    striped
    )
    MListItem(
      v-for="row in rows"
      :key="row.cardId"
      condensed
      :data-testid="`collection-row-${row.cardId}`"
      )
      | {{ row.displayName }}
      template(#badge)
        MBadge(
          v-if="row.locked"
          variant="primary"
          ) 🔒 Locked
        MBadge(
          v-if="!row.owned"
          variant="danger"
          ) Not owned
      template(#top-right)
        span(:data-testid="`collection-rank-${row.cardId}`") {{ row.owned ? `Rank ${row.rank} / ${rankMax}` : '—' }}
      template(#bottom-left) {{ row.clan }} · {{ row.cost }} mana · {{ row.cardId }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { getGameDataByLibrarySubscriptionOptions, getSinglePlayerSubscriptionOptions } from '@metaplay/core'
import { MBadge, MCard, MErrorCallout, MHorizontalDictionary, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

const props = defineProps<{
  /**
   * Id of the player whose collection this card shows. Injected by the `Players/Details/Tab0` placement.
   */
  playerId: string
}>()

const { data: playerData } = useSubscription(() => getSinglePlayerSubscriptionOptions(props.playerId))
const { data: gameData } = useSubscription(() => getGameDataByLibrarySubscriptionOptions(['Cards', 'Global']))

/** One rendered row: a config card with whatever the account holds joined onto it. */
interface CollectionRow {
  cardId: string
  displayName: string
  clan: string
  cost: number
  owned: boolean
  rank: number
  locked: boolean
}

/** `PlayerModel.Collection`, a card id to rank map. Absent key means the account does not own the card. */
const collection = computed<Record<string, number>>(() => playerData.value?.model?.collection ?? {})

/**
 * `PlayerModel.LockSlots`, keyed by **slot index** rather than by card — an absent key is an empty slot, so
 * nothing needs a sentinel card id. "Is this card locked" is therefore a walk over the values, which is what
 * `PlayerModel.IsLocked` does in C#.
 */
const lockSlots = computed<Record<string, string>>(() => playerData.value?.model?.lockSlots ?? {})
const lockedCardIds = computed<Set<string>>(() => new Set(Object.values(lockSlots.value)))

/**
 * The `Global` config library.
 *
 * `GlobalConfig`'s members are C# fields, so the admin API serializes them under their declared PascalCase
 * names — unlike the `CardInfo` rows, whose members are properties and arrive camelCased. **None of the three
 * reads below falls back to a number**, deliberately: `RankMin`, `RankMax` and `MinLockRank` are 1, 5 and 2 in
 * this build, so a defaulted read would render a card that looks entirely correct and is not — the exact
 * silent failure a casing regression produces.
 */
const globalConfig = computed(() => gameData.value?.gameConfig?.Global)

const configUnavailable = new Error(
  'The Global game-config library did not load, so there is no rank floor or ceiling to read ranks against. ' +
    'Nothing here is safe to show without it.'
)

const rankMin = computed<number>(() => Number(globalConfig.value?.RankMin))
const rankMax = computed<number>(() => Number(globalConfig.value?.RankMax))
const minLockRank = computed<number>(() => Number(globalConfig.value?.MinLockRank))

/**
 * The rows, built by walking the **config** and joining the account's ranks onto it, so a card the account
 * does not own renders as "Not owned" rather than being silently absent. Non-collectible cards (the tokens the
 * rules mint) are not part of anybody's collection and are left out.
 *
 * Sort: locked first, then rank descending, then clan, then cost, then the card id — that last tie-break
 * being `CardInfo.CompareCanonical`, the game's own canonical order.
 */
const rows = computed<CollectionRow[]>(() => {
  const cards = gameData.value?.gameConfig?.Cards
  if (!cards) return []

  const built: CollectionRow[] = []
  for (const cardId of Object.keys(cards)) {
    const card = cards[cardId]
    if (card?.collectible !== true) continue

    const rank = collection.value[cardId]
    built.push({
      cardId,
      displayName: String(card.displayName ?? cardId),
      clan: String(card.clan ?? '—'),
      cost: Number(card.cost ?? 0),
      owned: typeof rank === 'number',
      rank: typeof rank === 'number' ? rank : 0,
      locked: lockedCardIds.value.has(cardId),
    })
  }

  return built.sort((a, b) => {
    if (a.locked !== b.locked) return a.locked ? -1 : 1
    if (a.rank !== b.rank) return b.rank - a.rank
    if (a.clan !== b.clan) return a.clan < b.clan ? -1 : 1
    if (a.cost !== b.cost) return a.cost - b.cost
    return a.cardId < b.cardId ? -1 : 1
  })
})

const collectibleCount = computed<number>(() => rows.value.length)
const ownedCount = computed<number>(() => rows.value.filter((row) => row.owned).length)

/** The Heist's only visible trace: a card above `Global.RankMin` was moved there by something. */
const ranksAboveFloor = computed<number>(() => rows.value.filter((row) => row.owned && row.rank > rankMin.value).length)

/**
 * The lock slots, as an operator reads them: how many the account has, what is in them, and the rank a card
 * must reach before it can be locked at all — below that floor there is nothing to freeze, because the floor
 * already protects the card.
 */
const lockSlotItems = computed<Array<{ key: string; value: unknown }>>(() => {
  const slotCount = Number(playerData.value?.model?.lockSlotCount ?? 0)
  const items: Array<{ key: string; value: unknown }> = [
    { key: 'Lock slots used', value: `${String(Object.keys(lockSlots.value).length)} / ${String(slotCount)}` },
    { key: 'Lockable from', value: `Rank ${String(minLockRank.value)}` },
  ]

  const cards = gameData.value?.gameConfig?.Cards
  for (const slot of Object.keys(lockSlots.value).sort((a, b) => Number(a) - Number(b))) {
    const cardId = lockSlots.value[slot]
    const displayName = cards?.[cardId]?.displayName
    items.push({ key: `Slot ${slot}`, value: displayName ? String(displayName) : cardId })
  }

  return items
})
</script>
