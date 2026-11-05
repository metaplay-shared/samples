using Game.Logic;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Services;
using WebClientBase.Components;
using WebClientBase.Configuration;
using WebClientBase.Startup;

// The warm, dark "midnight burrow" palette with an amber accent. This drives the CSS variables the menu, the dialogs
// and WebClientBase's components read. wwwroot/index.html and wwwroot/manifest.webmanifest repeat BgPrimary, for the
// page before the client starts and for the installed app's window: change them with it.
ThemeColors theme = new ThemeColors
{
    BgPrimary     = "#0d0a12",
    BgSecondary   = "#14101c",
    BgCard        = "#1c1626",
    BgHover       = "#282034",
    BorderColor   = "rgba(255, 255, 255, 0.08)",
    TextPrimary   = "#f4f4f5",
    TextSecondary = "#a1a1aa",
    TextMuted     = "#71717a",
    AccentBlue    = "#f0a12b",
    AccentGreen   = "#22c55e",
    AccentAmber   = "#f59e0b",
    AccentRed     = "#ef4444",
};

await WebClientHostBuilder<App, PlayerModel, MetaplayClientService>
    .Create(args, appTitle: new GlobalOptions().Options.ProjectName, theme: theme)
    .ConfigureBuilder(builder =>
    {
        // The subsystem services live on the client service, which is the observer hub their models dispatch through.
        builder.Services.AddSingleton(sp => sp.GetRequiredService<MetaplayClientService>().Collection);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<MetaplayClientService>().Match);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<MetaplayClientService>().Matchmaking);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<MetaplayClientService>().Community);
        builder.Services.AddSingleton<StartupQuery>();
    })
    .RunAsync();
