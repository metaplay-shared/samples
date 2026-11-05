<template lang="pug">
div
  //- MListCard is our own kitchen-sink UI component for displaying lists with all kinds of convenient utilities like searching, filtering and sorting.
  //- Tip: you can look inside the component properties to see all the available options as we add them over time!
  MListCard(
    title="Producers"
    subtitle="Every producer in the game, and how far this player has got with each one."
    :item-list="allProducers"
    :get-item-key="getItemKey"
    :get-search-fields="getSearchFields"
    :filters="filters"
    :sort-options="sortOptions"
    :presets="presets"
    empty-list-message="This game has no producers configured."
    :error="gameDataError ?? playerDataError"
    data-testid="player-producers-card"
    )
    template(#default="{ entries }")
      MItemGrid(
        :entries="entries"
        :min-item-width-rem="10"
        )
        template(#item="{ item }")
          div(class="tw-flex tw-items-start tw-justify-between tw-gap-1")
            span(class="tw-truncate tw-font-semibold") {{ item.name }}
            MBadge(v-if="item.category === 'Event'") Event
          div(class="tw-mb-2 tw-text-sm tw-text-neutral-500") {{ item.kind }}
          div(class="tw-flex tw-items-center tw-justify-between tw-gap-1")
            MBadge(
              v-if="item.level > 0"
              variant="primary"
              ) Level {{ item.level }}
            MBadge(
              v-else
              variant="neutral"
              ) Locked
            MTextButton(
              v-if="item.level === 0"
              permission="api.players.unlock_producer"
              :disabled-tooltip="unlockDisabledTooltip(item)"
              @click="onProducerUnlockClick(item)"
              :data-testid="`unlock-button-${sentenceCaseToKebabCase(item.id)}`"
              ) Unlock
            MTextButton(
              v-else
              permission="api.players.unlock_producer"
              :disabled-tooltip="'This producer is already unlocked.'"
              :data-testid="`unlock-button-${sentenceCaseToKebabCase(item.id)}`"
              ) Unlocked

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
  MActionModal,
  MBadge,
  MTextButton,
  useNotifications,
  MItemGrid,
  MListCard,
  makeListCardBuilder,
  type PresetConfigs,
} from '@metaplay/meta-ui-next'
import { sentenceCaseToKebabCase } from '@metaplay/meta-utilities'
import { useSubscription } from '@metaplay/subscriptions'

const props = defineProps<{
  /**
   * Id of the player whose producers we want to show.
   */
  playerId: string
}>()

/**
 * A producer as shown in the list: what the game config knows about it, plus how far this player has got with it.
 */
interface ProducerListItem {
  /**
   * Id of the producer, for example 'Elixir'.
   */
  id: string
  /**
   * Position of the producer in the game config, which is the game's own progression order.
   */
  index: number
  /**
   * Display name of the producer.
   */
  name: string
  /**
   * Display name of the producer's kind, for example 'Website'.
   */
  kind: string
  /**
   * 'Normal' producers can be unlocked by an admin from this card, 'Event' ones only by a LiveOps event.
   */
  category: string
  /**
   * The player's level for this producer.
   */
  level: number
}

// Subscribe to the data we need to render this component.
// Protip: subscriptions cache and refresh their data automatically. Much better than individual HTTP requests!
const { data: gameData, error: gameDataError } = useSubscription(() =>
  getGameDataByLibrarySubscriptionOptions(['Producers', 'ProducerKinds'])
)
const {
  data: playerData,
  error: playerDataError,
  refresh: playerRefresh,
} = useSubscription(() => getSinglePlayerSubscriptionOptions(props.playerId))

const unlockProducerModal = useTemplateRef('unlockProducerModal')

/**
 * Binds the item type once so that the filters, sort options and getters below don't have to annotate it again.
 */
const listCardBuilder = makeListCardBuilder<ProducerListItem>()

/**
 * Producer ids are unique, so they double as the list card's item keys.
 */
const getItemKey = listCardBuilder.keyGetter((item) => item.id)

/**
 * Fields that the list card's search box matches against.
 * Protip: add custom search fields that are relevant in your game!
 */
const getSearchFields = listCardBuilder.searchFieldsGetter((item) => [item.name, item.kind])

/**
 * Filters to be passed to the list card.
 * Protip: add custom filters that are relevant in your game!
 */
const filters = {
  status: listCardBuilder.filters.customOptions('Status', {
    unlocked: { label: 'Unlocked', filterFn: (item) => item.level > 0 },
    locked: { label: 'Locked', filterFn: (item) => item.level === 0 },
  }),
  // Matches the ProducerKinds display names, which is what `item.kind` holds.
  kind: listCardBuilder.filters.stringOptions('Kind', (item) => item.kind, {
    language: { label: 'Programming language', value: 'Programming Language' },
    framework: { label: 'Web framework', value: 'Web Framework' },
    tool: { label: 'Tool', value: 'Tool' },
  }),
  // Locked producers count as 0. No max, as producers have no configured max level.
  level: listCardBuilder.filters.numericRange('Level', (item) => item.level ?? 0, 0),
}

/**
 * Sort options to be passed to the list card. Locked producers sort as level 0.
 * Protip: add custom sort options that are relevant in your game!
 */
const sortOptions = {
  index: listCardBuilder.sort('Game progression', 'Earliest first', 'Latest first', (item) => item.index ?? 0),
  name: listCardBuilder.sort('Name', 'A-Z', 'Z-A', (item) => item.name),
  level: listCardBuilder.sort('Level', 'Lowest first', 'Highest first', (item) => item.level ?? 0, true),
}

/**
 * One-click views of the list, shown in the card's preset dropdown. The `all` key renames the card's built-in
 * "All items" preset, and stays the default because it is first.
 * Protip: add presets for the views your team looks at most!
 */
const presets: PresetConfigs<typeof filters, typeof sortOptions> = {
  all: { displayName: 'All producers' },
  languages: {
    displayName: 'Programming languages',
    filters: { kind: { type: 'options', activeOptions: ['language'] } },
  },
  frameworks: {
    displayName: 'Web frameworks',
    filters: { kind: { type: 'options', activeOptions: ['framework'] } },
  },
  tools: {
    displayName: 'Tools',
    filters: { kind: { type: 'options', activeOptions: ['tool'] } },
  },
}

/**
 * Custom computed property to list every producer in the game, with this player's level for the unlocked ones.
 */
const allProducers = computed((): ProducerListItem[] | undefined => {
  if (gameData.value && playerData.value) {
    // eslint-disable-next-line @typescript-eslint/no-unsafe-type-assertion -- We know this is an object.
    const availableProducers = gameData.value.gameConfig.Producers as Record<
      string,
      { name: string; kind: string; category: string }
    >
    // eslint-disable-next-line @typescript-eslint/no-unsafe-type-assertion -- We know this is an object.
    const producerKinds = gameData.value.gameConfig.ProducerKinds as Record<string, { name: string }>

    return Object.keys(availableProducers).map((id, index) => ({
      id,
      index,
      name: availableProducers[id].name,
      kind: producerKinds[availableProducers[id].kind].name,
      category: availableProducers[id].category,
      level: playerData.value.model.producers[id]?.level ?? 0,
    }))
  } else {
    return undefined
  }
})

const selectedProducer = ref<string | null>(null)
const gameServerApi = useGameServerApi()

const { showSuccessNotification } = useNotifications()

/**
 * Tooltip text explaining why a producer cannot be unlocked from the dashboard.
 */
function unlockDisabledTooltip(item: ProducerListItem): string | undefined {
  if (item.category === 'Event') {
    return 'Event producers are unlocked by a special producer LiveOps event, not from the dashboard.'
  }
  return undefined
}

function onProducerUnlockClick(item: ProducerListItem): void {
  unlockProducerModal.value?.open()
  selectedProducer.value = item.id
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
