<!-- Lists the cosmetics the player owns and marks the equipped ones. Looks up each owned id in the Cosmetics
     game config library for its display name and kind. -->
<template lang="pug">
MListCard(
  title="Cosmetics"
  :item-list="items"
  :get-item-key="getItemKey"
  :get-search-fields="getSearchFields"
  :filters="filters"
  :empty-list-message="`${playerName} doesn't own any cosmetics yet.`"
  :error="cosmeticsError ?? playerError"
  data-testid="player-cosmetics-card"
  )
  template(#item="{ item: cosmetic }")
    MListItem {{ cosmetic.displayName }}
      template(#badge)
        MBadge(variant="primary") {{ cosmetic.kind }}

      template(#top-right)
        MBadge(
          v-if="cosmetic.isEquipped"
          variant="success"
          ) Equipped
        MBadge(
          v-else-if="cosmetic.isUnacknowledged"
          variant="warning"
          tooltip="Granted but not yet seen in the game."
          ) New

      template(#bottom-left) {{ cosmetic.id }}

      template(#bottom-right)
        span(v-if="cosmetic.price") {{ cosmetic.price }}
        span(
          v-else
          class="tw-italic tw-text-neutral-400"
          ) Not purchasable
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { getGameDataByLibrarySubscriptionOptions, getSinglePlayerSubscriptionOptions } from '@metaplay/core'
import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { MListCard, makeListCardBuilder } from '@metaplay/meta-ui-next/unstable'
import { useSubscription } from '@metaplay/subscriptions'

const props = defineProps<{
  /**
   * Id of the player whose wardrobe to show.
   */
  playerId: string
}>()

const { data: playerData, error: playerError } = useSubscription(() =>
  getSinglePlayerSubscriptionOptions(props.playerId)
)

/**
 * The Cosmetics library of the active game config, for display names and kinds.
 */
const { data: cosmeticsConfig, error: cosmeticsError } = useSubscription(() =>
  getGameDataByLibrarySubscriptionOptions(['Cosmetics'])
)

const playerName = computed(() => model.value?.playerName ?? 'This player')

interface CosmeticItem {
  id: string
  displayName: string
  kind: string
  isEquipped: boolean
  isUnacknowledged: boolean
  price: string | null
}

/**
 * One entry of the Cosmetics game-config library, as the Admin API serializes it.
 */
interface CosmeticCatalogueEntry {
  displayName?: string | undefined
  kind?: string | undefined
  price?: { amount?: number | undefined; currency?: string | undefined } | null | undefined
}

/**
 * The parts of PlayerModel this card reads, as the Admin API serializes them.
 */
interface PlayerModelJson {
  playerName?: string
  _cosmetics?:
    | {
        owned?: string[] | undefined
        unacknowledged?: string[] | undefined
        _equippedAvatar?: string | null | undefined
        _equippedFrame?: string | null | undefined
        _equippedNameEffect?: string | null | undefined
      }
    | undefined
}

/**
 * The player's model, typed as PlayerModelJson from the untyped subscription payload.
 */
const model = computed<PlayerModelJson | undefined>(() => playerData.value?.model)

/**
 * The Cosmetics library of the active game config, keyed by cosmetic id.
 */
const catalogue = computed<Record<string, CosmeticCatalogueEntry | undefined>>(
  () => cosmeticsConfig.value?.gameConfig.Cosmetics ?? {}
)

/**
 * The owned cosmetics, each combined with its Cosmetics library entry. An owned id that is missing from the
 * game config is still listed, with the id as its display name.
 */
const items = computed<CosmeticItem[]>(() => {
  const cosmetics = model.value?._cosmetics
  const owned: string[] = cosmetics?.owned ?? []
  const unacknowledged: string[] = cosmetics?.unacknowledged ?? []
  const equipped = [cosmetics?._equippedAvatar, cosmetics?._equippedFrame, cosmetics?._equippedNameEffect].filter(
    (id: string | null | undefined): id is string => id !== null && id !== undefined
  )

  return owned.map((id: string) => {
    const catalogueEntry = catalogue.value[id]
    const price = catalogueEntry?.price
    return {
      id,
      displayName: catalogueEntry?.displayName ?? id,
      kind: catalogueEntry?.kind ?? 'Unknown',
      isEquipped: equipped.includes(id),
      isUnacknowledged: unacknowledged.includes(id),
      price: price ? `${price.amount ?? '?'} ${price.currency ?? '?'}` : null,
    }
  })
})

const listCardBuilder = makeListCardBuilder<CosmeticItem>()

const getItemKey = listCardBuilder.keyGetter((item) => item.id)

const getSearchFields = listCardBuilder.searchFieldsGetter((item) => [item.id, item.displayName])

const filters = {
  equipped: listCardBuilder.filters.customOptions('Equipped', {
    equipped: { label: 'Yes', filterFn: (item) => item.isEquipped },
    notEquipped: { label: 'No', filterFn: (item) => !item.isEquipped },
  }),
}
</script>
