<template lang="pug">
span You can close this window
</template>

<script lang="ts" setup>
import { onMounted } from 'vue'

import { handleOauthPopupCallback } from '@metaplay/browser-sdk'

onMounted(() => {
  handleOauthPopupCallback()
})
</script>
