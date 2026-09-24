using Game.Client.Services;
using Game.ClientBase.Components;
using Game.ClientBase.Configuration;
using Game.ClientBase.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);

StaticEnvironmentConfigProvider.SelectForPage(builder.HostEnvironment.BaseAddress);
if (builder.HostEnvironment.IsDevelopment())
    StaticEnvironmentConfigProvider.ApplyLocalPortOffset(builder.Configuration["LocalServer:PortOffset"]);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(new ClientConfig(AppTitle: "Sticky Paws"));
builder.Services.AddSingleton<MetaplayClientService>();
builder.Services.AddSingleton<IMetaplayConnectionService>(sp => sp.GetRequiredService<MetaplayClientService>());

// The subsystem services live on the client service, which is the observer hub their models dispatch through.
builder.Services.AddSingleton(sp => sp.GetRequiredService<MetaplayClientService>().Collection);
builder.Services.AddSingleton(sp => sp.GetRequiredService<MetaplayClientService>().Match);
builder.Services.AddSingleton(sp => sp.GetRequiredService<MetaplayClientService>().Matchmaking);
builder.Services.AddSingleton(sp => sp.GetRequiredService<MetaplayClientService>().Community);

WebAssemblyHost host = builder.Build();
host.Services.GetRequiredService<MetaplayClientService>().Connect();
await host.RunAsync();
