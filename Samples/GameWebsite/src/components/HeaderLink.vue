<template lang="pug">
RouterLink(
  v-if="to"
  :to="to"
  :class="['rounded-lg hover:bg-gray-200 active:bg-gray-300 py-2 px-3 font-semibold', { 'bg-gray-100': active }]"
  :active="active"
  )
  slot
button(
  v-else
  :class="['rounded-lg hover:bg-gray-200 active:bg-gray-300 py-2 px-3 font-semibold']"
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
