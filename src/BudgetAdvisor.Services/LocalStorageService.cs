using System.Text.Json;
using Microsoft.JSInterop;

namespace BudgetAdvisor.Services;

public sealed class LocalStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IJSRuntime _jsRuntime;

    public LocalStorageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task SaveAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await _jsRuntime.InvokeVoidAsync("budgetAdvisor.storage.save", key, json);
    }

    public async Task<T?> LoadAsync<T>(string key)
    {
        var json = await _jsRuntime.InvokeAsync<string?>("budgetAdvisor.storage.load", key);
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async Task<string> BackupAsync<T>(string fileName, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await _jsRuntime.InvokeVoidAsync("budgetAdvisor.files.downloadText", fileName, json, "application/json");
        return json;
    }

    public Task<T?> RestoreAsync<T>(string json)
    {
        var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
        return Task.FromResult(value);
    }
}
