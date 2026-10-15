using Game.Logic;
using WebClient;
using WebClient.Services;
using WebClientBase.Components;
using WebClientBase.Configuration;
using WebClientBase.Startup;

// Dark, cinematic navy base palette with a command-blue accent. App.razor emits these as the root CSS variables
// (--bg-*, --text-*, --accent-*) that the game's stylesheets (wwwroot/css/) and the shared components read.
ThemeColors theme = new ThemeColors
{
    BgPrimary   = "#02060a",
    BgSecondary = "#070d15",
    BgCard      = "#0c141f",
    BgHover     = "#16202e",
    BorderColor = "rgba(255, 255, 255, 0.08)",
    AccentBlue  = "#1f7fff",
};

// Offline practice can be pointed at one named map with "?env=offline&course=<map id>", in place of the
// published practice map. Ignored unless the offline environment was asked for; online, the map comes from the
// server.
if (OfflineCourseOverride.ParseFromQuery() is { } requestedCourse)
    OfflineCourseOverride.RequestedCourseId = CourseId.FromString(requestedCourse);

await WebClientHostBuilder<App, MetaplayClientService>
    .Create(args, appTitle: new GlobalOptions().Options.ProjectName, theme: theme)
    .RunAsync();
