using System.IO.Compression;
using System.Text;
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

    public async Task<string?> LoadJsonAsync(string key) =>
        await _jsRuntime.InvokeAsync<string?>("budgetAdvisor.storage.load", key);

    public async Task<string> BackupAsync<T>(string fileName, T value)
    {
        var json = Serialize(value);
        await _jsRuntime.InvokeVoidAsync("budgetAdvisor.files.downloadText", fileName, json, "application/json");
        return json;
    }

    public async Task<byte[]> BackupArchiveAsync(string fileName, IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(files);

        var archiveBytes = CreateZipArchive(files);
        await _jsRuntime.InvokeVoidAsync(
            "budgetAdvisor.files.downloadBytes",
            fileName,
            Convert.ToBase64String(archiveBytes),
            "application/zip");

        return archiveBytes;
    }

    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions);

    public async Task SaveJsonAsync(string key, string json)
    {
        await _jsRuntime.InvokeVoidAsync("budgetAdvisor.storage.save", key, json);
    }

    public async Task RemoveAsync(string key)
    {
        await _jsRuntime.InvokeVoidAsync("budgetAdvisor.storage.remove", key);
    }

    public async Task SetCookieAsync(string name, string value, int days = 3650)
    {
        await _jsRuntime.InvokeVoidAsync("budgetAdvisor.cookies.set", name, value, days);
    }

    public async Task<string?> GetCookieAsync(string name) =>
        await _jsRuntime.InvokeAsync<string?>("budgetAdvisor.cookies.get", name);

    public async Task RemoveCookieAsync(string name)
    {
        await _jsRuntime.InvokeVoidAsync("budgetAdvisor.cookies.remove", name);
    }

    public Task<T?> RestoreAsync<T>(string json)
    {
        var value = Deserialize<T>(json);
        return Task.FromResult(value);
    }

    public Task<IReadOnlyDictionary<string, string>> RestoreArchiveAsync(byte[] archiveBytes)
    {
        ArgumentNullException.ThrowIfNull(archiveBytes);

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8);
            files[entry.Name] = reader.ReadToEnd();
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(files);
    }

    private static byte[] CreateZipArchive(IReadOnlyDictionary<string, string> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(file.Value);
            }
        }

        return stream.ToArray();
    }
}
