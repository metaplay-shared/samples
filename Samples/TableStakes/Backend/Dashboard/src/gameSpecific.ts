// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import type { App } from 'vue'

// Import the integration API.
import { setGameSpecificInitialization } from '@metaplay/core'

/**
 * This is a Vue 3 plugin function that gets called after the SDK CorePlugin is registered but before the application is mounted.
 * Use this function to register any Vue components or plugins that you want to use to customize the dashboard.
 * @param app The Vue app instance.
 */
export function GameSpecificPlugin(_app: App): void {
  setGameSpecificInitialization((initializationApi) => {
    // Wallet balances and lifetime record totals, shown in the resources list of the player overview card.
    // The player model is untyped JSON. Member names are the camelCased C# member names, and private
    // [MetaMember] fields keep their underscore prefix.
    initializationApi.addPlayerResources([
      {
        displayName: 'Coins',
        getAmount: (playerModel: any): number => playerModel._wallet?.coins ?? 0,
      },
      {
        displayName: 'Gems',
        getAmount: (playerModel: any): number => playerModel._wallet?.gems ?? 0,
      },
      {
        displayName: 'Spin Tokens',
        getAmount: (playerModel: any): number => playerModel._wallet?.spinTokens ?? 0,
      },
      {
        displayName: 'Games Played',
        getAmount: (playerModel: any): number => playerModel.record?.gamesPlayed ?? 0,
      },
      {
        displayName: 'Games Won',
        getAmount: (playerModel: any): number => playerModel.record?.gamesWon ?? 0,
      },
      {
        displayName: 'Tricks Won',
        getAmount: (playerModel: any): number => playerModel.record?.tricksWon ?? 0,
      },
      {
        displayName: 'At Table',
        getAmount: (playerModel: any): string => {
          const matchId: string | undefined = playerModel.currentMatchId
          return matchId && matchId !== 'None' ? matchId : 'not seated'
        },
      },
    ])

    // Show the contents granted by a validated demo purchase wherever the dashboard lists purchase contents.
    initializationApi.addInAppPurchaseContents([
      {
        $type: 'Game.Logic.ResolvedWalletBundle',
        getDisplayContent: (purchase: any): string[] =>
          (purchase.contents?.amounts ?? []).map((amount: any): string => `${amount.amount} ${amount.currency}`),
      },
    ])

    // Game-specific cards at the top of the player details summary tab, above the SDK's built-in cards.
    // Position 'before' inserts at the very front, so the second and third cards use position 'after' with
    // the previous card's id to keep them in the order declared here.
    initializationApi.addUiComponent(
      'Players/Details/Tab0',
      {
        uniqueId: 'PlayerSystemsCard',
        vueComponent: async () => await import('./PlayerSystemsCard.vue'),
      },
      { position: 'before' }
    )

    initializationApi.addUiComponent(
      'Players/Details/Tab0',
      {
        uniqueId: 'MatchHistoryCard',
        vueComponent: async () => await import('./MatchHistoryCard.vue'),
      },
      { position: 'after', targetId: 'PlayerSystemsCard' }
    )

    initializationApi.addUiComponent(
      'Players/Details/Tab0',
      {
        uniqueId: 'CosmeticsCard',
        vueComponent: async () => await import('./CosmeticsCard.vue'),
      },
      { position: 'after', targetId: 'MatchHistoryCard' }
    )
  })
}
