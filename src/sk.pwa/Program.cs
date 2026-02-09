using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using sk.pwa;
using sk.pwa.Services;
using sk.shared.Interfaces;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddHttpClient("api", client =>
{
    var env = builder.HostEnvironment;
    client.BaseAddress = env.IsProduction()
        ? new Uri("https://schafkopf.pax77.org")
        : new Uri("http://localhost:5283");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddScoped(sp =>
{
    var uri = builder.HostEnvironment.IsProduction()
        ? new Uri("https://schafkopf.pax77.org/gameHub")
        : new Uri("http://localhost:5283/gameHub");

    return new HubConnectionBuilder()
        .WithUrl(uri)
        .WithAutomaticReconnect()
        .Build();
});

builder.Services.AddScoped<ConnectInfoState>();
builder.Services.AddScoped<IGameHubClient, GameHubClient>();

await builder.Build().RunAsync();
