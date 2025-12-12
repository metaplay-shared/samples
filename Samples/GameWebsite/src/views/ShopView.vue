<template lang="pug">
div(class="container mx-auto flex h-full items-center justify-around space-x-20 p-6")
  //- Loading state.
  p(v-if="!isInitialized") Initializing...

  //- Not logged in state.
  ui-button(
    v-else-if="!isLoggedIn"
    @click="loginWithRedirect('google')"
    ) Login

  //- Logged in state.
  div(
    v-else
    class="space-y-6"
    )
    div(class="rounded-xl border bg-gray-50 p-4 shadow-lg")
      h1(class="mb-1 text-xl font-semibold") Web Shop TBD
      p(class="mb-3 text-sm text-gray-700") Available offer groups and offers for each connected game.
</template>

<script lang="ts" setup>
import UiButton from '../components/UiButton.vue'
import { useMetaplayBrowserSdk } from '../useMetaplayBrowserSdk'

const { isLoggedIn, loginWithRedirect, isInitialized } = useMetaplayBrowserSdk()
</script>
