<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  //- meta-list-card is our own kitchen-sink UI component for displaying lists with all kinds of convenient utilities like filtering and sorting.
  //- Tip: you can look inside the component properties to see all the available options as we add them over time!
  MetaListCard(
    title="Producers"
    :item-list="allProducers"
    :search-fields="searchFields"
    :filter-sets="filterSets"
    :sort-options="sortOptions"
    :page-size="20"
    list-layout="flex"
    data-testid="player-producers-card"
    )
    //- meta-list-card doesn't know how to neatly display producers, so here we make a small box with the relevant text fields.
    //- The 'listLayout="flex"' -option causes these elements to float and wrap like left aligned text on different screen sizes.
    template(#item-card="slotProps")
      //- Chaining a lot of CSS classes to make a box with some nice default visuals and a conditional style.
      div(
        class="tw-mr-2 tw-h-full tw-rounded tw-border tw-border-neutral-300 tw-py-3 tw-text-center"
        :class="[slotProps.item.level > 0 ? 'tw-bg-neutral-100' : 'tw-bg-neutral-600 tw-text-neutral-100']"
        style="min-width: 7rem"
        )
        //- Simple header
        div(class="tw-mb-1 tw-font-bold") {{ slotProps.item.info }}

        //- Conditionally show either levels, an unlock button (for unlockable producers), or a lock icon (for special producers).
        div(
          v-if="slotProps.item.level > 0"
          class="tw-text-sm"
          ) Level {{ slotProps.item.level }}
        div(
          v-else-if="gameData?.gameConfig.Producers?.[slotProps.item.info].category === 'Normal'"
          class="tw-mt-2 tw-px-2"
          )
          MButton(
            permission="api.players.unlock_producer"
            size="small"
            @click="onProducerUnlockClick(slotProps.item)"
            :data-testid="`unlock-button-${sentenceCaseToKebabCase(slotProps.item.info)}`"
            ) Unlock
        div(
          v-else
          class="tw-flex tw-items-center tw-justify-center"
          )
          svg(
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 640 640"
            class="tw-mt-1.5 tw-size-6"
            )
            // Font Awesome Free v7.1.0 by @fontawesome - https://fontawesome.com License - https://fontawesome.com/license/free Copyright 2026 Fonticons, Inc.
            path(
              d="M256 160L256 224L384 224L384 160C384 124.7 355.3 96 320 96C284.7 96 256 124.7 256 160zM192 224L192 160C192 89.3 249.3 32 320 32C390.7 32 448 89.3 448 160L448 224C483.3 224 512 252.7 512 288L512 512C512 547.3 483.3 576 448 576L192 576C156.7 576 128 547.3 128 512L128 288C128 252.7 156.7 224 192 224z"
              )

  MActionModal(
    ref="unlockProducerModal"
    title="Unlock producer"
    :action="unlockProducer"
    ok-button-label="Unlock"
    data-testid="unlock-producer"
    )
    p Are you sure you want to unlock #[MBadge {{ selectedProducer }}] for #[MBadge {{ playerData?.model?.playerName }}]? This action cannot be undone.
</template>

<script lang="ts" setup>
import { computed, ref, useTemplateRef } from 'vue'

import { getGameDataByLibrarySubscriptionOptions, getSinglePlayerSubscriptionOptions } from '@metaplay/core'
import { useGameServerApi } from '@metaplay/game-server-api'
import {
  MetaListSortDirection,
  MetaListSortOption,
  MetaListFilterSet,
  MetaListFilterOption,
  MetaListCard,
} from '@metaplay/meta-ui'
import { MActionModal, MBadge, MButton, useNotifications } from '@metaplay/meta-ui-next'
import { sentenceCaseToKebabCase } from '@metaplay/meta-utilities'
import { useSubscription } from '@metaplay/subscriptions'

const props = defineProps<{
  /**
   * Id of the player whose producers we want to show.
   */
  playerId: string
}>()

// Subscribe to the data we need to render this component.
// Protip: subscriptions cache and refresh their data automatically. Much better than individual HTTP requests!
const { data: gameData } = useSubscription(() => getGameDataByLibrarySubscriptionOptions(['Producers']))
const { data: playerData, refresh: playerRefresh } = useSubscription(() =>
  getSinglePlayerSubscriptionOptions(props.playerId)
)

const unlockProducerModal = useTemplateRef('unlockProducerModal')

/**
 * Search fields array to be passed to the meta-list-card component.
 * Protip: add custom search fields that are relevant in your game!
 */
const searchFields = ['info']

/**
 * Filter sets array to be passed to the meta-list-card component.
 * Protip: add custom filter sets that are relevant in your game!
 */
const filterSets = [
  new MetaListFilterSet('unlocked', [
    new MetaListFilterOption('Locked', (x: any) => x.level === 0),
    new MetaListFilterOption('Unlocked', (x: any) => x.level > 0),
  ]),
]

/**
 * Sort options array to be passed to the meta-list-card component.
 * Protip: add custom sort options that are relevant in your game!
 */
const sortOptions = [
  MetaListSortOption.asUnsorted(),
  new MetaListSortOption('Level', 'level', MetaListSortDirection.Ascending),
  new MetaListSortOption('Level', 'level', MetaListSortDirection.Descending),
]

/**
 * Custom computed property to look up all the possible producers from game configs and add info about the one the player has already unlocked so it's easier to render a nice looking list.
 */
const allProducers = computed(() => {
  if (gameData.value && playerData.value) {
    // eslint-disable-next-line @typescript-eslint/no-unsafe-type-assertion -- We know this is an object.
    const availableProducers = gameData.value.gameConfig.Producers as object

    return Object.keys(availableProducers).map((id) => {
      if (id in playerData.value.model.producers) {
        return playerData.value.model.producers[id]
      } else {
        return {
          info: id,
          level: 0,
        }
      }
    })
  } else {
    return undefined
  }
})

const selectedProducer = ref<string | null>(null)
const gameServerApi = useGameServerApi()

const { showSuccessNotification } = useNotifications()

function onProducerUnlockClick(item: any): void {
  unlockProducerModal.value?.open()
  selectedProducer.value = item.info
}

/**
 * Unlock a producer by sending a request to the server.
 */
async function unlockProducer(): Promise<void> {
  await gameServerApi.post(`/players/${props.playerId}/unlockProducer/${selectedProducer.value}`)
  showSuccessNotification(`Unlocked ${selectedProducer.value} for player ${props.playerId}.`)
  playerRefresh()
}
</script>
