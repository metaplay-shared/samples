<template lang="pug">
MCard(
  title="Newcomer shield"
  :is-loading="!playerData || !gameData"
  data-testid="sticky-paws-shield-card"
  )
  template(#title)
    span(data-testid="card-title") Newcomer shield
    MBadge(
      :variant="stateVariant"
      shape="pill"
      class="tw-relative tw-bottom-[1px]"
      data-testid="shield-state-badge"
      ) {{ stateLabel }}

  //- Every number on this card comes from the Global config library, so with the library missing there is
  //- nothing to say and a plausible-looking default would be worse than an empty card.
  MErrorCallout(
    v-if="gameData && !globalConfig"
    :error="configUnavailable"
    data-testid="shield-config-unavailable"
    )

  MList(
    v-else
    show-border
    )
    MListItem
      | Ranked matches played
      template(#top-right)
        span(data-testid="shield-ranked-matches") {{ rankedMatchesPlayed }} of {{ shieldMatches }}
      template(#bottom-left) The shield covers an account's first {{ shieldMatches }} ranked matches.

    MListItem
      | Waiver
      template(#top-right)
        span(data-testid="shield-waiver") {{ waived ? 'On' : 'Off' }}
      template(#bottom-left) Lifts this account's own shield. The ranked count is left alone.

    MListItem
      | At a table
      template(#badge)
        MBadge(
          v-if="atATable"
          variant="warning"
          ) Seated
      template(#top-right)
        span(data-testid="shield-current-match") {{ atATable ? currentMatch : 'No' }}
      template(#bottom-left) The match this account is pointing at, and the id to search the logs by.

  //- The one fact the SDK's generated modal cannot state, and the reason an operator concludes the button is
  //- broken without it. Shown only while the shield is still deciding something.
  MCallout(
    v-if="state === 'shielded'"
    title="A shield on either seat shields the match"
    variant="warning"
    class="tw-mt-3"
    data-testid="shield-both-seats-callout"
    )
    | Seating two humans at an unshielded table means waiving both accounts. Waiving one and seeing nothing
    | change is the feature working, not a broken control.

  template(#buttons)
    MActionModalButton(
      :trigger-button-label="waived ? 'Restore shield' : 'Waive shield'"
      :modal-title="waived ? 'Restore newcomer shield' : 'Waive newcomer shield'"
      :action="toggleWaiver"
      :ok-button-label="waived ? 'Restore' : 'Waive'"
      :trigger-button-disabled-tooltip="toggleDisabledTooltip"
      :permission="togglePermission"
      variant="warning"
      data-testid="shield-toggle"
      )
      p(v-if="waived")
        | Hands this account its newcomer shield back, so its remaining
        | #[MBadge {{ remainingMatches }}] shielded ranked
        | #[span {{ remainingMatches === 1 ? 'match is' : 'matches are' }}] tiered as a newcomer's again.
      p(v-else)
        | Lifts this account's own newcomer shield, so its ranked matches are tiered by Power Score from the
        | next queue entry onwards. The opponent's shield, if they still have one, shields the match anyway.
      p(class="tw-mb-0 tw-text-neutral-500")
        | The same lever as the #[span(class="tw-italic") Waive newcomer shield] action in Admin Actions; this
        | one already knows which way to move it.
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { getGameDataByLibrarySubscriptionOptions, getSinglePlayerSubscriptionOptions } from '@metaplay/core'
import { useGameServerApi } from '@metaplay/game-server-api'
import {
  MActionModalButton,
  MBadge,
  MCallout,
  MCard,
  MErrorCallout,
  MList,
  MListItem,
  useNotifications,
} from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { type ShieldState, shieldState, shieldStateLabel } from './shieldState'

const props = defineProps<{
  /**
   * Id of the player whose newcomer shield this card shows. Injected by the `Players/Details/Tab0` placement.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const { showSuccessNotification } = useNotifications()

const { data: playerData, refresh: playerRefresh } = useSubscription(() =>
  getSinglePlayerSubscriptionOptions(props.playerId)
)
const { data: gameData } = useSubscription(() => getGameDataByLibrarySubscriptionOptions(['Global']))

/**
 * The action's own C# type name, read off the player's dashboard-action metadata rather than hard-coded, so a
 * namespace change cannot silently break the POST below.
 */
const shieldAction = computed(() => {
  const actions = playerData.value?.playerSpecializedActions
  if (!Array.isArray(actions)) return undefined
  return actions.find((action: { typeName?: string }) =>
    (action.typeName ?? '').endsWith('.PlayerSetNewcomerShieldWaived')
  )
})

/** The permission the server will check, taken from the same metadata as the type name. */
const togglePermission = computed<string>(() => shieldAction.value?.permission ?? 'api.players.disruptive')

const rankedMatchesPlayed = computed<number>(() => playerData.value?.model?.record?.rankedMatchesPlayed ?? 0)

/**
 * The `Global` config library.
 *
 * Note the PascalCase on its members: `GlobalConfig`'s are C# *fields*, which the admin API serializes under
 * their declared names, unlike the player model's properties and unlike the `CardInfo` rows the other cards
 * read. **Nothing here falls back to a number.** A plausible default is the worst possible failure for this
 * card — `NewcomerShieldMatches` silently reading a constant is exactly the class of bug this wave's headline
 * spec error was — so a missing library renders as an error instead.
 */
const globalConfig = computed(() => gameData.value?.gameConfig?.Global)

const configUnavailable = new Error(
  'The Global game-config library did not load, so this card has no threshold to compare against. ' +
    'Nothing here is safe to show without it.'
)

const shieldMatches = computed<number>(() => Number(globalConfig.value?.NewcomerShieldMatches))

const waived = computed<boolean>(() => playerData.value?.model?.newcomerShieldWaived === true)

/** `EntityId.None` serializes as the string `'None'`, not as null or an empty string. */
const currentMatch = computed<string>(() => playerData.value?.model?.currentMatch ?? 'None')
const atATable = computed<boolean>(() => currentMatch.value !== 'None' && currentMatch.value !== '')

const state = computed<ShieldState>(() => shieldState(rankedMatchesPlayed.value, shieldMatches.value, waived.value))
const stateLabel = computed<string>(() => shieldStateLabel(state.value))
const stateVariant = computed<'success' | 'warning' | 'neutral'>(() => {
  if (state.value === 'playedOut') return 'neutral'
  if (state.value === 'waived') return 'warning'
  return 'success'
})

/** How many shielded ranked matches the account has left, floored at zero. */
const remainingMatches = computed<number>(() => Math.max(0, shieldMatches.value - rankedMatchesPlayed.value))

/**
 * Why the toggle is not offered, or `undefined` when it is.
 *
 * The `playedOut` case is the one worth stating: past the threshold the waiver decides nothing, so offering
 * the lever with copy claiming an effect is exactly the confusion the "Played out" badge exists to prevent —
 * an operator presses, sees no change, and concludes the waiver is broken.
 */
const toggleDisabledTooltip = computed<string | undefined>(() => {
  if (!shieldAction.value) return 'This server reports no newcomer-shield dashboard action.'
  if (state.value === 'playedOut')
    return "This account's shield has played out, so the waiver decides nothing either way."
  return undefined
})

/**
 * Post the opposite of the account's current value through the SDK's own dashboard-action endpoint, so the
 * permission check and the audit-log entry are the ones the `[PlayerDashboardAction]` attribute already
 * arranges. No game-specific admin API controller is involved.
 *
 * The POST resolves once the action is *enqueued*, not once it has run, so the model is polled before
 * anything is claimed on screen — otherwise a session whose timeline flush lags the POST gets a toast
 * announcing a change the card is still showing the old value for.
 */
async function toggleWaiver(): Promise<void> {
  const typeName = shieldAction.value?.typeName
  if (typeName === undefined) throw new Error('This server reports no newcomer-shield dashboard action.')

  const nextValue = !waived.value
  await gameServerApi.post(`/dashboardActions/${props.playerId}/execute`, {
    $type: typeName,
    waived: nextValue,
  })

  const landed = await pollUntilWaiverIs(nextValue)
  playerRefresh()

  if (landed) showSuccessNotification(nextValue ? 'Newcomer shield waived.' : 'Newcomer shield restored.')
  else showSuccessNotification('Waiver queued; the account has not applied it yet.')
}

/**
 * Whether the waiver reached the model within a couple of seconds of the enqueue.
 * @param expected The value the action was posted with.
 * @returns True once the model agrees, false if it still does not after the last attempt.
 */
async function pollUntilWaiverIs(expected: boolean): Promise<boolean> {
  for (let attempt = 0; attempt < 10; attempt++) {
    const response = await gameServerApi.get(`/players/${props.playerId}`)
    if (response.data?.model?.newcomerShieldWaived === expected) return true
    await new Promise<void>((resolve) => {
      setTimeout(resolve, 200)
    })
  }
  return false
}
</script>
