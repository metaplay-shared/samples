// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import { createPinia } from 'pinia'
import { createApp } from 'vue'

// Import the app's root component.
import App from './App.vue'
// Apply Tailwind CSS and global styles.
import './assets/styles.css'
// Import the app's local routes.
import { router } from './router'

// Create the Vue app instance.
// eslint-disable-next-line @typescript-eslint/no-unsafe-argument
const app = createApp(App)

// Apply the Pinia plugin and the app's local routes.
app.use(createPinia())
app.use(router)

// Mount the app to the DOM.
app.mount('#app')
