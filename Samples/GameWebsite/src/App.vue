<template lang="pug">
div(class="flex h-screen flex-col")
  //- Header bar
  header(
    v-if="!!route.name && route.name !== 'popupFlowCompleted'"
    class="z-10 flex flex-shrink-0 basis-16 items-center justify-between bg-white px-4 shadow"
    )
    nav(class="flex items-center space-x-4")
      h1(class="mr-6 inline text-2xl") HelloWeb
      HeaderLink(
        to="/"
        :active="route.name === 'game'"
        ) Game
      HeaderLink(
        to="/shop"
        :active="route.name === 'shop'"
        ) Shop

    span(v-if="!isInitialized") Loading...
    span(
      v-else-if="isLoggedIn"
      class="space-x-3"
      )
      HeaderLink(
        @click="logout()"
        data-testid="logout"
        ) Logout
    span(
      v-else
      class="space-x-3"
      )
      HeaderLink(
        @click="loginWithRedirect('google')"
        data-testid="login-with-redirect"
        ) Login (redirect)
      HeaderLink(
        @click="loginWithPopup('google')"
        data-testid="login-with-popup"
        ) Login (popup)

  //- Body content container with the selected route.
  div(class="flex-grow bg-white")
    RouterView
</template>

<script setup lang="ts">
import { RouterView, useRoute } from 'vue-router'

import HeaderLink from './components/HeaderLink.vue'
import { useMetaplayBrowserSdk } from './useMetaplayBrowserSdk'

const route = useRoute()
const { isLoggedIn, loginWithPopup, loginWithRedirect, logout, isInitialized } = useMetaplayBrowserSdk()
</script>
