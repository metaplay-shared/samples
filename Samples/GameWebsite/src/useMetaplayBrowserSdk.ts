// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import { computed, onMounted, ref, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import {
  type GameServerAccessTokens,
  type BrowserSdkClient,
  initializeBrowserSdkClient,
  isTokenError,
  type UnityRuntime,
} from '@metaplay/browser-sdk'

/**
 * Game server URL from app build
 */
const gameServerUrl = new URL(String(import.meta.env.VITE_GAMESERVER_URL))

/**
 * The browser SDK client. This is initialized automatically when the app starts.
 */
const browserSdkClient = shallowRef<BrowserSdkClient>()

const isInitialized = ref(false)

/**
 */
const currentTokens = ref<GameServerAccessTokens>()

/**
 * Callback to react to changes in the tokens.
 * @param tokens New token info. Undefined when the user is not logged in.
 */
function onTokensChanged(gameServerUrlForChange: URL, tokens: GameServerAccessTokens | undefined): void {
  // Save the new state.
  if (gameServerUrl.href === gameServerUrlForChange.href) currentTokens.value = tokens
}

/**
 * Convenience computed property to check if the user is logged in.
 */
const isLoggedIn = computed(() => !!currentTokens.value)

/*
 * Only allow one initialization to be triggered.
 */
let isInitializationStarted = false

/**
 * This is a lightweight wrapper for Vue 3 that initializes the Metaplay browser SDK client and makes it available to the app as a composable.
 */
// eslint-disable-next-line @typescript-eslint/explicit-function-return-type
export function useMetaplayBrowserSdk() {
  // Automatically initialize the browser SDK client if needed.
  onMounted(async () => {
    if (isInitializationStarted) {
      return
    }
    isInitializationStarted = true

    const router = useRouter()
    const route = useRoute()

    browserSdkClient.value = initializeBrowserSdkClient(undefined, 'closePopup', onTokensChanged)
    currentTokens.value = browserSdkClient.value.getToken(gameServerUrl)

    await router.isReady()

    // Intercept authorization code
    if (!!route.query.oauth_callback && route.name !== 'popupFlowCompleted') {
      const data = new URLSearchParams(
        Object.entries(route.query).flatMap((entry) => {
          const key = entry[0]
          const valueOrArr = entry[1]
          if (Array.isArray(valueOrArr)) {
            return valueOrArr.map((subentry) => [key, String(subentry)])
          } else {
            return [[key, String(valueOrArr)]]
          }
        })
      )

      const result = await browserSdkClient.value.handleOauthCallbackAsync(data, false)
      if (isTokenError(result)) {
        // eslint-disable-next-line no-console
        console.debug(`Token exchange failed: ${result.error} -- ${result.description}`)
      } else if (result.token) {
        // eslint-disable-next-line no-console
        console.debug(`Token exchange completed: ${result.token.access_token}`)

        // Remove query params from location bar
        const newQuery: Record<string, string> = {}
        result.newQuery.forEach((val, key) => (newQuery[key] = val))
        await router.replace({
          query: newQuery,
        })
      }
    }

    isInitialized.value = true
  })

  return {
    isLoggedIn,
    currentTokens,
    isInitialized,
    logout: (): void => {
      browserSdkClient.value?.logout(gameServerUrl)
    },
    loginWithPopup: (authPlatform: string): void => {
      void browserSdkClient.value?.loginWithPopup(gameServerUrl, authPlatform, false)
    },
    loginWithRedirect: (authPlatform: string): void => {
      void browserSdkClient.value?.loginWithRedirect(gameServerUrl, authPlatform, false)
    },
    refreshTokens: (): void => {
      void browserSdkClient.value?.refreshTokens(gameServerUrl)
    },
    initializeUnityRuntime: async (unityRuntime: UnityRuntime): Promise<void> => {
      // Define the game specific API bridge callback implementations.
      const gameSpecificApiBridgeCallbackImplementations = {}

      await browserSdkClient.value?.initializeUnityRuntimeBridge(
        unityRuntime,
        {},
        gameSpecificApiBridgeCallbackImplementations
      )
    },
  }
}
