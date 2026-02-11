// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import type { App } from 'vue'

// Import the integration API.
import { setGameSpecificInitialization, OverviewListItem } from '@metaplay/core'

// Example of importing your own global style sheet.
import './styles/game-specific-styles.css'

/**
 * This is a Vue 3 plugin function that gets called after the SDK CorePlugin is registered but before the application is mounted.
 * Use this function to register any Vue components or plugins that you want to use to customize the dashboard.
 * @param app The Vue app instance.
 */
export function GameSpecificPlugin(_app: App): void {
  // Feel free to add any customization logic here for your game!
  setGameSpecificInitialization(async (initializationApi) => {
    // Custom resources(shown in the player overview card).
    initializationApi.addPlayerResources([
      {
        displayName: 'Gold',
        getAmount: (playerModel): number => playerModel.wallet.numGold,
      },
      {
        displayName: 'Gems',
        getAmount: (playerModel): number => playerModel.wallet.numGems,
      },
    ])

    // Custom rewards (shown in many places like the player's inbox).
    initializationApi.addPlayerRewards([
      {
        $type: 'Game.Logic.RewardGold',
        getDisplayValue: (reward): string => `💰 Gold x${reward.amount}`,
      },
      {
        $type: 'Game.Logic.RewardGems',
        getDisplayValue: (reward): string => `💎 Gems x${reward.amount}`,
      },
      {
        $type: 'Game.Logic.RewardProducer',
        getDisplayValue: (reward): string => `🛠 ${reward.producerId} x${reward.amount}`,
      },
    ])

    // Inject custom content into the player details page to render a player's producers nicely.
    initializationApi.addUiComponent(
      'Players/Details/Tab0',
      {
        uniqueId: 'ProducerListCard',
        vueComponent: async () => await import('./ProducersCard.vue'),
        width: 'full',
      },
      { position: 'before' }
    )

    // Inject custom action button into the player admin tools.
    initializationApi.addUiComponent('Players/Details/AdminActions:Disruptive', {
      uniqueId: 'EditPlayerWallet',
      vueComponent: async () => await import('./PlayerActionEditPlayerWallet.vue'),
      // Set the 'displayPermission' property to require permission to view this component.
      // The component will be hidden from users who do not have the specified permission.
      // displayPermission: 'api.players.set_wallet'
    })

    // Custom IAPs (shown in the purchase history card).
    initializationApi.addInAppPurchaseContents([
      {
        $type: 'Game.Logic.ResolvedPurchaseGameContent',
        getDisplayContent: (purchase): string[] => [`💰 Gold x${purchase.numGold}`, `💎 Gems x${purchase.numGems}`],
      },
      {
        $type: 'Game.Logic.LegacyShopOfferDynamicPurchaseContent',
        getDisplayContent: (purchase): string[] => [`Legacy shop offer ${purchase.offerId}`],
      },
    ])

    // Custom label in the player details overview card.
    initializationApi.addPlayerDetailsOverviewListItem(
      OverviewListItem.asNumber(
        'Highest Producer Level',
        (player) => {
          // eslint-disable-next-line @typescript-eslint/no-unsafe-argument -- Typing is not known.
          const producers = Object.entries(player.model.producers)
          const maxProducerLevel = producers.reduce(
            // eslint-disable-next-line @typescript-eslint/no-unsafe-argument -- Typing is not known.
            (p, c: any) => Math.max(c[1].level, p),
            0
          )
          return maxProducerLevel
        }
        // Set the 'displayPermission' property to require permission to view this list-item.
        // The item will be hidden from users who do not have the required permission.
        // 'api.players.set_wallet'
      ),
      { position: 'after', targetId: 'Joined' }
    )

    // Custom label in the player account reconnect pop-over.
    initializationApi.addPlayerReconnectAccountPreviewListItem(
      OverviewListItem.asNumber('Highest Producer Level', (player) => {
        // eslint-disable-next-line @typescript-eslint/no-unsafe-argument -- Typing is not known.
        const producers = Object.entries(player.model.producers)
        const maxProducerLevel = producers.reduce(
          // eslint-disable-next-line @typescript-eslint/no-unsafe-argument -- Typing is not known.
          (p, c: any) => Math.max(c[1].level, p),
          0
        )
        return maxProducerLevel
      }),
      { position: 'after', targetId: 'Joined' }
    )

    // Custom decoration of ProducerTypeId to make it more human friendly.
    const gameData = await initializationApi.getGameDataByLibrary(['Producers', 'ProducerKinds'])
    initializationApi.addStringIdDecorator('Game.Logic.ProducerTypeId', (stringId: string): string => {
      const producer = gameData.gameConfig.Producers[stringId]
      const producerKind = gameData.gameConfig.ProducerKinds[producer.kind]
      return `${producer.name} (${producerKind.name})`
    })

    // An example of how to add a custom label in the guild details overview card.
    // initializationApi.addGuildDetailsOverviewListItem(
    //   OverviewListItem.asString('My Custom Value', (guild: any) => {
    //     return guild.model.myCustomValue
    //   }),
    //   { position: 'before', targetId: 'Members Online' }
    // )

    // An example of how to add new routes into the sidebar navigation.
    // initializationApi.addNavigationEntry({
    //   path: '/myFeature',
    //   name: 'View My Feature',
    //   component: async () => await import('./NewFeature.vue'),
    // },
    // {
    //   icon: 'calendar-alt',
    //   sidebarTitle: 'My Feature',
    //   sidebarOrder: 15,
    //   category: 'LiveOps',
    //   permission: 'api.my_feature.view'
    // })
  })
}
