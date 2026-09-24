using Game.Logic;
using Microsoft.Extensions.DependencyInjection;
using WebClient;
using WebClient.Services;
using WebClientBase.Components;
using WebClientBase.Startup;

await WebClientHostBuilder<App, PlayerModel, MetaplayClientService>
    .Create(args, appTitle: "Table Stakes", logoEmoji: "🃏", theme: GameTheme.Colors)
    // MetaStateService builds the meta shell screens' views from the player state. Fixtures fill in data before a
    // session exists and for slices that a ?meta= scenario overrides (docs/meta-shell.md).
    .ConfigureBuilder(builder =>
    {
        builder.Services.AddScoped<MetaStateService>();

        // WalletBurstService runs the reward animation and the HUD balance it updates. It is scoped to the app instead
        // of a screen because the HUD is on every meta route and the player can leave the granting screen before
        // the animation ends.
        builder.Services.AddScoped<WalletBurstService>();
    })
    .RunAsync();
