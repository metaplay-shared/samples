<template lang="pug">
button(
  v-if="!disabled"
  class="rounded bg-blue-400 px-3 py-2 font-semibold text-white hover:bg-blue-500 active:bg-blue-700"
  )
  slot
button(
  v-else
  class="rounded bg-gray-400 px-3 py-2 font-semibold text-white"
  disabled
  )
  slot
</template>

<script lang="ts" setup>
defineProps<{
  disabled?: boolean
}>()
</script>
