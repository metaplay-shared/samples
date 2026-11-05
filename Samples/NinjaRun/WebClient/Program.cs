using Game.Logic;
using WebClient.Services;
using WebClientBase.Components;
using WebClientBase.Configuration;
using WebClientBase.Startup;

// Dark, cinematic navy base palette with a command-blue accent. This drives the CSS variables used
// across the game's stylesheets (wwwroot/css/) and WebClientBase's components.
// wwwroot/index.html repeats BgPrimary, for the page before the client starts: change it with it.
ThemeColors theme = new ThemeColors
{
    BgPrimary   = "#02060a",
    BgSecondary = "#070d15",
    BgCard      = "#0c141f",
    BgHover     = "#16202e",
    BorderColor = "rgba(255, 255, 255, 0.08)",
    AccentBlue  = "#1f7fff",
};

await WebClientHostBuilder<App, PlayerModel, MetaplayClientService>
    .Create(args, appTitle: new GlobalOptions().Options.ProjectName, theme: theme)
    .RunAsync();
