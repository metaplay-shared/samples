<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData")
  MActionModalButton(
    modal-title="Set Player's Wallet Contents"
    :action="setPlayerWallet"
    trigger-button-label="Edit Player Wallet"
    variant="warning"
    ok-button-label="Set Wallet"
    permission="api.players.set_wallet"
    :ok-button-disabled-tooltip="!newGold && !newGems ? 'Change at least one value.' : undefined"
    @show="resetModal"
    data-testid="action-set-player-wallet"
    )
    template(#default)
      p(class="tw-mb-1") You can manually change the amount of gold and gems that #[MBadge {{ playerData.model.playerName }}] has in their wallet.
      MInputNumber(
        label="Gold"
        :model-value="newGold"
        :min="0"
        :hint-message="`Player currently has ${playerData.model.wallet.numGold} gold.`"
        allow-undefined
        placeholder="Leave blank to keep the original value"
        @update:model-value="newGold = $event"
        )
      MInputNumber(
        label="Gems"
        :model-value="newGems"
        :min="0"
        :hint-message="`Player currently has ${playerData.model.wallet.numGems} gems.`"
        allow-undefined
        placeholder="Leave blank to keep the original value"
        @update:model-value="newGems = $event"
        )
    template(#bottom-panel)
      meta-no-seatbelts(:name="playerData.model.playerName")
</template>

<script lang="ts" setup>
import { ref } from 'vue'

import { getSinglePlayerSubscriptionOptions } from '@metaplay/core'
import { useGameServerApi } from '@metaplay/game-server-api'
import { MetaNoSeatbelts } from '@metaplay/meta-ui'
import { MBadge, MInputNumber, MActionModalButton, useNotifications } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

const props = defineProps<{
  /**
   * ID of the player to edit.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const { data: playerData, refresh: playerRefresh } = useSubscription(() =>
  getSinglePlayerSubscriptionOptions(props.playerId)
)

const newGold = ref<number>()
const newGems = ref<number>()

function resetModal(): void {
  newGold.value = undefined
  newGems.value = undefined
}

const { showSuccessNotification } = useNotifications()

async function setPlayerWallet(): Promise<void> {
  await gameServerApi.post(`/players/${props.playerId}/setWallet`, {
    newGold: newGold.value ?? null,
    newGems: newGems.value ?? null,
  })
  showSuccessNotification('Player wallet changed.')
  playerRefresh()
}
</script>
