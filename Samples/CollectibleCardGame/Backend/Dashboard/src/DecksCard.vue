<template lang="pug">
MCard(
  :is-loading="!playerData || !gameData"
  data-testid="sticky-paws-decks-card"
  )
  template(#title)
    span(data-testid="card-title") Saved decks
    MBadge(
      variant="neutral"
      shape="pill"
      class="tw-relative tw-bottom-[1px]"
      data-testid="decks-summary-badge"
      ) {{ decks.length }} saved

  template(#subtitle)
    | Power Score is what the ranked queue bands on and what sets the stakes tier, so a pairing question is a
    | deck question. A deck is legal when it is saved and is never re-validated, so a card count that is not
    | {{ deckSize }} is visibly wrong — no legality verdict is derived here.

  //- The card count against Global.DeckSize is the whole of what this card verdicts, so with the library
  //- missing there is nothing to compare against — and DeckSize falling back to 0 would put a red
  //- "25 of 0 cards" badge on every legal deck and print "a card count that is not 0 is visibly wrong".
  MErrorCallout(
    v-if="gameData && !globalConfig"
    :error="configUnavailable"
    data-testid="decks-config-unavailable"
    )

  MList(
    v-if="globalConfig"
    show-border
    )
    MListItem(data-testid="decks-last-played")
      | Last played
      template(#top-right)
        span(data-testid="decks-last-played-value") {{ lastPlayedLabel }}
      template(#bottom-left)
        | The deck this account was last seated with, and what Home defaults its picker to. Either one of
        | these saved decks or a config-authored starter deck.

  MList(
    v-if="globalConfig && decks.length > 0"
    show-border
    striped
    class="tw-mt-3"
    )
    MListItem(
      v-for="deck in decks"
      :key="deck.deckId"
      :data-testid="`deck-row-${deck.deckId}`"
      )
      | {{ deck.name }}
      template(#badge)
        MBadge(
          v-if="deck.isLastPlayed"
          variant="primary"
          data-testid="deck-last-played-badge"
          ) Last played
        MBadge(
          v-if="deck.cardCount !== deckSize"
          variant="danger"
          ) {{ deck.cardCount }} of {{ deckSize }} cards
      template(#top-right)
        span(:data-testid="`deck-power-score-${deck.deckId}`") Power Score {{ deck.powerScore }}
      template(#bottom-left) {{ deck.cardCount }} cards · deck {{ deck.deckId }}

  MCallout(
    v-else-if="globalConfig"
    title="No saved decks"
    variant="neutral"
    class="tw-mt-3"
    data-testid="decks-empty-callout"
    )
    | This account has never saved a deck. A fresh account plays a starter deck until it builds one, which is
    | why the last-played row above handles both kinds.
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { getGameDataByLibrarySubscriptionOptions, getSinglePlayerSubscriptionOptions } from '@metaplay/core'
import { MBadge, MCallout, MCard, MErrorCallout, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { type DeckChoiceJson, lastPlayedDeckLabel } from './lastPlayedDeck'
import { computePowerScore } from './powerScore'

const props = defineProps<{
  /**
   * Id of the player whose decks this card shows. Injected by the `Players/Details/Tab0` placement.
   */
  playerId: string
}>()

const { data: playerData } = useSubscription(() => getSinglePlayerSubscriptionOptions(props.playerId))
const { data: gameData } = useSubscription(() =>
  getGameDataByLibrarySubscriptionOptions(['Cards', 'StarterDecks', 'Global'])
)

/** One rendered deck row. */
interface DeckRow {
  deckId: string
  name: string
  cardCount: number
  powerScore: number
  isLastPlayed: boolean
}

/**
 * The `Global` config library. `GlobalConfig`'s members are C# fields, so PascalCase on the wire — and
 * `DeckSize` deliberately does **not** fall back to a number: a defaulted `0` would badge every legal deck
 * red and make the subtitle read "a card count that is not 0 is visibly wrong", which no test on a fresh
 * account would catch. The subtitle interpolates the real value, so the e2e case asserts on it.
 */
const globalConfig = computed(() => gameData.value?.gameConfig?.Global)

const configUnavailable = new Error(
  'The Global game-config library did not load, so there is no deck size to compare these decks against. ' +
    'Nothing here is safe to show without it.'
)

const deckSize = computed<number>(() => Number(globalConfig.value?.DeckSize))

const collection = computed<Record<string, number>>(() => playerData.value?.model?.collection ?? {})

/**
 * `PlayerModel.LastPlayedDeck`, a `DeckChoice`: exactly one of `savedDeckId` and `starterDeckId` is set, and
 * the whole thing is null on an account that has never played. The admin API serializes the type's computed
 * `isSaved` / `isStarter` too, so there is no need to re-derive them here.
 */
const lastPlayedDeck = computed<DeckChoiceJson | undefined>(() => playerData.value?.model?.lastPlayedDeck ?? undefined)

/** `PlayerModel.Decks`, keyed by the server-assigned deck id. */
const decks = computed<DeckRow[]>(() => {
  const saved: Record<string, { name?: string; cards?: string[] } | undefined> = playerData.value?.model?.decks ?? {}

  const lastPlayedSavedId = lastPlayedDeck.value?.isSaved === true ? lastPlayedDeck.value.savedDeckId : undefined

  return Object.keys(saved)
    .sort((a, b) => Number(a) - Number(b))
    .map((deckId) => {
      const deck = saved[deckId]
      const cards: string[] = Array.isArray(deck?.cards) ? deck.cards : []
      return {
        deckId,
        name: deck?.name ?? '(unnamed)',
        cardCount: cards.length,
        powerScore: computePowerScore(cards, collection.value),
        isLastPlayed: lastPlayedSavedId !== undefined && Number(deckId) === lastPlayedSavedId,
      }
    })
})

/** The `StarterDecks` library's display names, keyed by starter deck id. */
const starterDeckNames = computed<Record<string, string>>(() => {
  const library = gameData.value?.gameConfig?.StarterDecks
  if (!library) return {}

  const names: Record<string, string> = {}
  for (const starterDeckId of Object.keys(library)) {
    const displayName = library[starterDeckId]?.displayName
    if (displayName !== undefined) names[starterDeckId] = String(displayName)
  }

  return names
})

/** This account's saved deck names, keyed by deck id, for the last-played row to resolve against. */
const savedDeckNames = computed<Record<string, string>>(() => {
  const names: Record<string, string> = {}
  for (const deck of decks.value) names[deck.deckId] = deck.name
  return names
})

/** What the last-played row says. Every branch is pinned in `tests/unit/lastPlayedDeck.test.ts`. */
const lastPlayedLabel = computed<string>(() =>
  lastPlayedDeckLabel(lastPlayedDeck.value, savedDeckNames.value, starterDeckNames.value)
)
</script>
