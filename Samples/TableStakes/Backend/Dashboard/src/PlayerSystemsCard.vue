<!-- Shows the current state of the player's meta systems as label and value rows, one group per system.
     Record totals are in the overview card's resources, the tournament uses the SDK's built-in card, and the
     match history and cosmetics lists have their own cards. -->
<template lang="pug">
MCard(
  title="Meta Systems"
  :error="playerError"
  data-testid="player-systems-card"
  )
  div(
    v-if="model"
    class="tw-flex tw-flex-col tw-gap-4"
    )
    div(
      v-for="group in groups"
      :key="group.title"
      )
      div(class="tw-mb-1 tw-font-bold tw-leading-6") {{ group.title }}
      div(class="tw-grid tw-grid-cols-[auto_1fr] tw-gap-x-6 tw-gap-y-1")
        template(
          v-for="row in group.rows"
          :key="row.label"
          )
          div(class="tw-text-neutral-500") {{ row.label }}
          div {{ row.value }}

  div(v-else) Loading…
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { computed } from 'vue'

import { getSinglePlayerSubscriptionOptions, isEpochTime } from '@metaplay/core'
import { MCard } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

const props = defineProps<{
  /**
   * Id of the player whose systems state to show.
   */
  playerId: string
}>()

const { data: playerData, error: playerError } = useSubscription(() =>
  getSinglePlayerSubscriptionOptions(props.playerId)
)

/**
 * The parts of PlayerModel this card reads, as the Admin API serializes them. Member names are the camelCased
 * C# member names, and private [MetaMember] fields keep their underscore prefix.
 */
interface PlayerModelJson {
  _dailyReward?:
    | {
        streakDays?: number | undefined
        claimCount?: number | undefined
        lastClaimedAt?: string | null | undefined
      }
    | undefined
  _spinWheel?:
    | {
        totalResolvedSpins?: number | undefined
        nextOrdinal?: number | undefined
        hasPendingReceipt?: boolean | undefined
      }
    | undefined
  _firstWeek?:
    | {
        hasStarted?: boolean | undefined
        startedAt?: string | null | undefined
        scheduleId?: string | null | undefined
        furthestDayIndex?: number | undefined
        claimCount?: number | undefined
        endsAt?: string | null | undefined
      }
    | undefined
  _weeklyEvent?:
    | {
        storedEvents?: WeeklyEventProgressJson[] | undefined
      }
    | undefined
}

interface WeeklyEventProgressJson {
  eventId?: string | undefined
  points?: number | undefined
  targetReached?: boolean | undefined
  isClaimed?: boolean | undefined
  claimedAt?: string | null | undefined
}

/**
 * The player's model, typed as PlayerModelJson from the untyped subscription payload.
 */
const model = computed<PlayerModelJson | undefined>(() => playerData.value?.model)

interface SystemRow {
  label: string
  value: string
}

interface SystemGroup {
  title: string
  rows: SystemRow[]
}

/**
 * Formats an ISO timestamp as a local date and time, or returns 'never' for a missing value or the epoch.
 */
function dateOrNever(iso?: string | null): string {
  if (iso === undefined || iso === null || isEpochTime(iso)) return 'never'
  return DateTime.fromISO(iso).toFormat('yyyy-MM-dd HH:mm')
}

/**
 * Formats a number as a string, treating a value missing from the payload as 0.
 */
function formatCount(value: number | undefined): string {
  return String(value ?? 0)
}

function dailyRewardGroup(m: PlayerModelJson): SystemGroup {
  const daily = m._dailyReward
  return {
    title: 'Daily Reward',
    rows: [
      { label: 'Streak', value: `${formatCount(daily?.streakDays)} days` },
      { label: 'Claims', value: formatCount(daily?.claimCount) },
      { label: 'Last claim', value: dateOrNever(daily?.lastClaimedAt) },
    ],
  }
}

function spinWheelGroup(m: PlayerModelJson): SystemGroup {
  const wheel = m._spinWheel
  return {
    title: 'Spin Wheel',
    rows: [
      { label: 'Spins resolved', value: formatCount(wheel?.totalResolvedSpins) },
      { label: 'Next ordinal', value: formatCount(wheel?.nextOrdinal) },
      { label: 'Pending receipt', value: wheel?.hasPendingReceipt ? 'yes' : 'no' },
    ],
  }
}

function firstWeekGroup(m: PlayerModelJson): SystemGroup {
  const firstWeek = m._firstWeek
  return {
    title: 'First Week',
    rows: [
      { label: 'Started', value: firstWeek?.hasStarted ? dateOrNever(firstWeek.startedAt) : 'not started' },
      { label: 'Schedule', value: firstWeek?.scheduleId ?? 'none' },
      { label: 'Furthest day', value: formatCount(firstWeek?.furthestDayIndex) },
      { label: 'Claims', value: formatCount(firstWeek?.claimCount) },
      { label: 'Ends', value: dateOrNever(firstWeek?.endsAt) },
    ],
  }
}

function weeklyEventGroup(m: PlayerModelJson): SystemGroup {
  const events = m._weeklyEvent?.storedEvents ?? []
  const latestEvent = events.length > 0 ? events[events.length - 1] : undefined
  return {
    title: 'Weekly Event',
    rows: latestEvent
      ? [
          { label: 'Event', value: latestEvent.eventId ?? 'unknown' },
          { label: 'Points', value: formatCount(latestEvent.points) },
          { label: 'Target reached', value: latestEvent.targetReached ? 'yes' : 'no' },
          { label: 'Reward claimed', value: latestEvent.isClaimed ? dateOrNever(latestEvent.claimedAt) : 'no' },
        ]
      : [{ label: 'Progress', value: 'no events scored yet' }],
  }
}

/**
 * The rows to show, one group per meta system.
 */
const groups = computed<SystemGroup[]>(() => {
  const m = model.value
  if (!m) return []
  return [dailyRewardGroup(m), spinWheelGroup(m), firstWeekGroup(m), weeklyEventGroup(m)]
})
</script>
