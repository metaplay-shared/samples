<template lang="pug">
div(class="container mx-auto flex h-full items-center justify-around space-x-20 p-6")
  //- Unity runtime.
  div(
    v-if="!unityRuntime"
    class="relative rounded-xl bg-gray-700 shadow-lg"
    style="height: 800px; flex-basis: 600px"
    )
    div(
      v-if="!unityRuntime && unityLoadingProgress"
      class="absolute inset-0 flex items-center justify-center"
      )
      div(class="text-2xl font-semibold text-white") {{ Math.round(unityLoadingProgress * 100) }}%

  //- Container for Unity canvas. Rendered unconditionally so that cached nodes don't accidentally keep
  //- references to the canvas and leak memory.
  div(
    ref="unityContainer"
    id="unityContainer"
    )

  //- Game info sidebar.
  div(
    class="w-full rounded-xl border bg-gray-50 p-4 shadow-lg"
    style="flex-basis: 30rem"
    )
    h1(class="mb-1 text-xl font-semibold") Browser Controls
    p(class="mb-3 text-sm text-gray-700") All code in this card is JavaScript running in the browser. It demonstrates communication with the Unity runtime and the game server.

    //- Unity runtime
    div(class="mb-3 space-y-3")
      h2(class="font-semibold") Unity Runtime
      p(
        v-if="unityLoadingWarningText"
        class="text-sm text-yellow-500"
        ) {{ unityLoadingWarningText }}
      p(
        v-if="unityLoadingErrorMessage"
        class="text-sm text-red-500"
        ) {{ unityLoadingErrorMessage }}

      UiButton(
        v-if="!unityRuntime"
        @click="loadUnityRuntime"
        data-testid="start-unity"
        ) {{ unityLoadingErrorMessage ? 'Retry' : 'Start Unity' }}
      UiButton(
        v-else
        @click="unloadUnityRuntime"
        data-testid="stop-unity"
        ) Stop Unity

      //- Auth
      h2(class="font-semibold") Player
      UiButton(
        v-if="!isLoggedIn"
        @click="loginWithRedirect('google')"
        ) Login
      div(v-else)
        p Logged in with #[span(class="text-sm") {{ currentTokens?.authPlatform }}]
        p(v-if="tokenExpiresIn") Token expires in {{ tokenExpiresIn }}s
        p(v-else) Token expired!
        UiButton(@click="refreshTokens()") Refresh token
</template>

<script lang="ts" setup>
import { onUnmounted, ref, computed, onMounted, watch, useTemplateRef } from 'vue'

import type { UnityRuntime } from '@metaplay/browser-sdk'

import UiButton from '../components/UiButton.vue'
import { useMetaplayBrowserSdk } from '../useMetaplayBrowserSdk'
import { loadAndExecuteScriptFromUrl } from '../utils'

// Browser SDK --------------------------------------------------------------------------------------------------------

const {
  isLoggedIn,
  currentTokens,
  loginWithRedirect,
  refreshTokens,
  initializeUnityRuntime,
  isInitialized: browserSdkInitialized,
} = useMetaplayBrowserSdk()

// Unity runtime ------------------------------------------------------------------------------------------------------

/**
 * This is the container for the canvas element that the game wil be rendered to.
 */
const unityContainer = useTemplateRef('unityContainer')
let unityCanvas: HTMLCanvasElement | null = null

/**
 * Unity runtime instance, once it has been loaded.
 */
const unityRuntime = ref<UnityRuntime>()

/**
 * Error message to show if the Unity loader script fails to load.
 */
const unityLoadingErrorMessage = ref<string>()

/**
 * Warning message from the Unity loader script.
 */
const unityLoadingWarningText = ref<string>()

/**
 * Progress label for the Unity loader script.
 */
const unityLoadingProgress = ref(0)

/**
 * Current time, updated every second
 */
const now = ref(new Date())
onMounted(() => {
  setInterval(() => {
    now.value = new Date()
  }, 1000)
})

const tokenExpiresIn = computed(() => {
  if (!currentTokens.value) return undefined
  const expiresAt = new Date(currentTokens.value.expiresAt)
  if (now.value > expiresAt) return undefined
  return Math.floor((expiresAt.getTime() - now.value.getTime()) / 1000)
})

// Load the Unity runtime once browserSdk init is complete
onMounted(() => {
  watch(
    browserSdkInitialized,
    (initialized, _) => {
      if (initialized) {
        void loadUnityRuntime()
      }
    },
    { immediate: true }
  )
})
onUnmounted(unloadUnityRuntime)

/**
 * Load and initialize the Unity instance.
 */
async function loadUnityRuntime(): Promise<void> {
  try {
    if (unityRuntime.value) throw new Error('Unity instance already initialized.')
    if (!browserSdkInitialized.value) throw new Error('BrowserSDK not initialized.')
    if (!unityContainer.value)
      throw new Error('Unity container element not found. This would prevent the game from loading.')
    if (unityContainer.value.id !== 'unityContainer')
      throw new Error(
        "Unity container element must have the ID 'unityContainer'. This would prevent the game from loading."
      )

    // Resolve the base url to the Unity WebGL client build from environment variable.
    const webglBuildBaseUrl = String(import.meta.env.VITE_WEBGL_BUILD_BASE_URL)

    // Try to use build props from Vite build
    // @ts-expect-error -- Unity loader magic. Do not question it.
    let props = __UNITY_BUILD_PROPS__

    // Try to load WebGL build props from file
    if (!props) {
      // Load WebGL build props.
      const propsUrl = `${webglBuildBaseUrl}/props.json`
      try {
        props = await (await fetch(propsUrl)).json()
      } catch (e) {
        throw new Error(`Unable to fetch props.json of the Unity project's WebGL build from '${propsUrl}'`, {
          cause: e,
        })
      }
    }

    // Come up with Unity loader options based on what was embedded during build time into props.json, combined with where the build is hosted.
    const unityLoaderOptions = {
      dataUrl: `${webglBuildBaseUrl}/${props.dataUrl}`,
      frameworkUrl: `${webglBuildBaseUrl}/${props.frameworkUrl}`,
      codeUrl: `${webglBuildBaseUrl}/${props.codeUrl}`,
      memoryUrl: props.memoryUrl ? `${webglBuildBaseUrl}/${props.memoryUrl}` : undefined,
      symbolsUrl: props.symbolsUrl ? `${webglBuildBaseUrl}/${props.symbolsUrl}` : undefined,
      streamingAssetsUrl: `${webglBuildBaseUrl}/StreamingAssets`,
      companyName: props.companyName,
      productName: props.productName,
      productVersion: props.productVersion,
      showBanner: onUnityError,
    }

    unityCanvas = document.createElement('canvas')
    unityCanvas.id = 'unityCanvas'
    unityContainer.value.appendChild(unityCanvas)

    // Note to anyone using this as a sample: It might not be a great idea to ge the canvas size from wthe build but instead configure it in website code.
    // This is convenient for testing, though.
    unityCanvas.style.width = `${props.playerWidth}px`
    unityCanvas.style.height = `${props.playerHeight}px`

    // This loads and executes the Unity loader script that is generated as part of the game build.
    // It adds a global "createUnityInstance" function that we can use to create the Unity runtime.
    await loadAndExecuteScriptFromUrl(`${webglBuildBaseUrl}/${props.loaderUrl}`)

    // Start loading the Unity runtime via the global "createUnityInstance" function that was added by the Unity loader script.
    // TODO: Modify the global typings to have this function to remove the error.
    // @ts-expect-error -- Unity loader magic. Do not question it.
    if (!window.createUnityInstance)
      throw new Error('Unity loader script injection failed. `createUnityInstance` not found on window.')

    // @ts-expect-error -- Unity loader magic. Do not question it.
    // eslint-disable-next-line require-atomic-updates -- TODO: Check this is safe.
    unityRuntime.value = await window.createUnityInstance(unityCanvas, unityLoaderOptions, onUnityLoadingProgress)
    if (!unityRuntime.value) throw Error('Unity instance creation failed')

    // Connect the Unity runtime to the browser SDK and pass in the game server options.
    await initializeUnityRuntime(unityRuntime.value)
  } catch (error) {
    onUnityError(String(error), 'error')
  }
}

/**
 * Utility function to unload the Unity runtime.
 */
async function unloadUnityRuntime(): Promise<void> {
  if (unityRuntime.value && unityCanvas) {
    await unityRuntime.value.Quit()
    // eslint-disable-next-line require-atomic-updates -- TODO: Check this is safe.
    unityRuntime.value = undefined
    unityContainer.value?.removeChild(unityCanvas)
    // eslint-disable-next-line require-atomic-updates -- TODO: Check this is safe.
    unityCanvas = null
  }
}

/**
 * Called when the Unity loader script fails to load.
 */
function onUnityError(msg: string, type: 'warning' | 'error'): void {
  if (type === 'warning') {
    unityLoadingWarningText.value = msg
  } else {
    unityLoadingErrorMessage.value = msg
  }
}

/**
 * Called during the loading process.
 * @param progress From 0 (loading just started) to 1 (fully loaded).
 */
function onUnityLoadingProgress(progress: number): void {
  unityLoadingProgress.value = progress
}
</script>
