<!-- Lists the player's recent finished games, newest first, from PlayerModel._matchHistory. -->
<template lang="pug">
MListCard(
  title="Match History"
  :item-list="items"
  :get-item-key="getItemKey"
  :get-search-fields="getSearchFields"
  :empty-list-message="`${playerName} hasn't finished any games yet.`"
  :error="playerError"
  data-testid="player-match-history-card"
  )
  template(#item="{ item: match }")
    MListItem {{ match.matchId }}
      template(#top-right)
        MBadge(
          v-if="match.isWin"
          variant="success"
          ) Win
        MBadge(
          v-else
          variant="neutral"
          ) Place {{ match.position + 1 }}

      template(#bottom-left)
        | {{ match.tricksWon }} tricks · {{ match.humanOpponents }} human opponents
        | · ended #[MDateTime(:instant="DateTime.fromISO(match.endedAt)")]

      template(#bottom-right)
        MBadge(
          v-if="!match.finishedByPlayer"
          variant="warning"
          tooltip="The game ended without the player at the table."
          ) Abandoned
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { computed } from 'vue'

import { getSinglePlayerSubscriptionOptions } from '@metaplay/core'
import { MBadge, MDateTime, MListItem } from '@metaplay/meta-ui-next'
import { MListCard, makeListCardBuilder } from '@metaplay/meta-ui-next/unstable'
import { useSubscription } from '@metaplay/subscriptions'

const props = defineProps<{
  /**
   * Id of the player whose match history to show.
   */
  playerId: string
}>()

const { data: playerData, error: playerError } = useSubscription(() =>
  getSinglePlayerSubscriptionOptions(props.playerId)
)

/**
 * Display name of the player, for the empty-list message.
 */
const playerName = computed(() => model.value?.playerName ?? 'This player')

/**
 * One finished game, as the Admin API serializes MatchHistoryEntry.
 */
interface MatchItem {
  matchId: string
  endedAt: string
  tricksWon: number
  /** Zero-based position in the final standings, 0 for the winner. */
  position: number
  isWin: boolean
  humanOpponents: number
  finishedByPlayer: boolean
}

/**
 * The parts of PlayerModel this card reads, as the Admin API serializes them.
 */
interface PlayerModelJson {
  playerName?: string
  _matchHistory?: MatchItem[] | undefined
}

/**
 * The player's model, typed as PlayerModelJson from the untyped subscription payload.
 */
const model = computed<PlayerModelJson | undefined>(() => playerData.value?.model)

/**
 * The match history, reversed to show the newest game first. The model stores it oldest first.
 */
const items = computed<MatchItem[]>(() => {
  const history: MatchItem[] = model.value?._matchHistory ?? []
  return [...history].reverse()
})

const listCardBuilder = makeListCardBuilder<MatchItem>()

const getItemKey = listCardBuilder.keyGetter((item) => item.matchId)

const getSearchFields = listCardBuilder.searchFieldsGetter((item) => [item.matchId])
</script>
