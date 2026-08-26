<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
MCard(title="Custom component")
  div(
    style="font-size: 5em"
    class="tw-m-2 tw-text-center tw-font-bold"
    ) Hello, World!
  div(class="tw-py-3 tw-text-center") Total number of clicks: {{ playerData.model.numClicks }}
</template>

<script lang="ts" setup>
import { getSinglePlayerSubscriptionOptions } from '@metaplay/core'
import { MCard } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

// This component takes in the player as a property and will automatically react to any updates to this passed data.
const props = defineProps<{
  playerId: string
}>()

// Fetch data from the game server using our subscriptions.
const { data: playerData } = useSubscription(() => getSinglePlayerSubscriptionOptions(props.playerId))
</script>
