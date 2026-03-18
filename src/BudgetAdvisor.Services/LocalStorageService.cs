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
        var json = Serialize(value);
        await SaveJsonAsync(key, json);
    }

    public async Task<T?> LoadAsync<T>(string key)
    {
        var json = await _jsRuntime.InvokeAsync<string?>("budgetAdvisor.storage.load", key);
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async Task<string> BackupAsync<T>(string fileName, T value)
    {
        var json = Serialize(value);
        await _jsRuntime.InvokeVoidAsync("budgetAdvisor.files.downloadText", fileName, json, "application/json");
        return json;
    }

    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions);

    public async Task SaveJsonAsync(string key, string json)
    {
        await _jsRuntime.InvokeVoidAsync("budgetAdvisor.storage.save", key, json);
    }

    public Task<T?> RestoreAsync<T>(string json)
    {
        var value = Deserialize<T>(json);
        return Task.FromResult(value);
    }
}
