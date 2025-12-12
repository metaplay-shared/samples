// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import { createRouter, createWebHistory } from 'vue-router'

import GameView from './views/GameView.vue'

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'game',
      component: GameView,
    },
    {
      path: '/shop',
      name: 'shop',
      // eslint-disable-next-line @typescript-eslint/explicit-function-return-type
      component: async () => await import('./views/ShopView.vue'),
    },
    {
      path: '/closePopup',
      name: 'popupFlowCompleted',
      // eslint-disable-next-line @typescript-eslint/explicit-function-return-type
      component: async () => await import('./views/PopupFlowCompleted.vue'),
    },
  ],
})
