using BudgetAdvisor.App;
using BudgetAdvisor.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<ApplicationState>();
builder.Services.AddMudServices();

var host = builder.Build();

await host.Services.GetRequiredService<LocalizationService>().InitializeAsync();
await host.Services.GetRequiredService<ApplicationState>().InitializeAsync();

await host.RunAsync();
