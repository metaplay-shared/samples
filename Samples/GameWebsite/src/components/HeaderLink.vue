<template lang="pug">
RouterLink(
  v-if="to"
  :to="to"
  class="rounded-lg px-3 py-2 font-semibold hover:bg-gray-200 active:bg-gray-300"
  :class="[{ 'bg-gray-100': active }]"
  :active="active"
  )
  slot
button(
  v-else
  class="rounded-lg px-3 py-2 font-semibold hover:bg-gray-200 active:bg-gray-300"
  )
  slot
</template>

<script lang="ts" setup>
import { RouterLink } from 'vue-router'

defineProps<{
  to?: string
  active?: boolean
}>()
</script>
