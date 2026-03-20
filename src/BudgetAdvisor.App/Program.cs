using BudgetAdvisor.App;
using BudgetAdvisor.App.Imports;
using BudgetAdvisor.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(builder.Configuration.GetSection(DropboxOptions.SectionName).Get<DropboxOptions>() ?? new DropboxOptions());
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<IApplicationLogService, ApplicationLogService>();
builder.Services.AddScoped<IBrowserStorageUsageService, BrowserStorageUsageService>();
builder.Services.AddScoped<IDataPruningService, DataPruningService>();
builder.Services.AddScoped<IUndoService, UndoService>();
builder.Services.AddScoped<ISyncProvider, DropboxSyncProvider>();
builder.Services.AddScoped<ManualSyncService>();
builder.Services.AddScoped<ApplicationState>();
builder.Services.AddScoped<ITransactionImporter, SwedbankCsvTransactionImporter>();
builder.Services.AddScoped<ITransactionImporter, NordeaCsvTransactionImporter>();
builder.Services.AddScoped<ITransactionImporter, SebCsvTransactionImporter>();
builder.Services.AddScoped<ITransactionImportDetector, TransactionImportDetector>();
builder.Services.AddScoped<TransactionImportService>();
builder.Services.AddMudServices();

var host = builder.Build();

await host.Services.GetRequiredService<LocalizationService>().InitializeAsync();
await host.Services.GetRequiredService<IApplicationLogService>().InitializeAsync();
await host.Services.GetRequiredService<ApplicationState>().InitializeAsync();
await host.Services.GetRequiredService<ManualSyncService>().InitializeAsync();

await host.RunAsync();
