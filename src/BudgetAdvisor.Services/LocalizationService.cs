using System.Net.Http.Json;
using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.Services;

public sealed class LocalizationService
{
    public const string LocalizationKey = "budget-advisor.localization";
    public const string CurrentLanguageKey = "budget-advisor.localization.current-language";

    private readonly HttpClient _httpClient;
    private readonly LocalStorageService _localStorageService;
    private readonly Dictionary<string, LocalizationResourceSet> _resources = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public string CurrentLanguage { get; private set; } = "en";

    public IReadOnlyCollection<string> Languages => _resources.Keys.OrderBy(code => code).ToArray();

    public LocalizationService(HttpClient httpClient, LocalStorageService localStorageService)
    {
        _httpClient = httpClient;
        _localStorageService = localStorageService;
    }

    public async Task InitializeAsync()
    {
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        _resources.Clear();
        await LoadDefaultLanguageAsync("en");
        await LoadDefaultLanguageAsync("sv");

        var storedSets = await _localStorageService.LoadAsync<List<LocalizationResourceSet>>(LocalizationKey);
        if (storedSets is not null)
        {
            foreach (var set in storedSets.Where(set => !string.IsNullOrWhiteSpace(set.LanguageCode)))
            {
                _resources[set.LanguageCode] = set;
            }
        }

        var storedLanguage = await _localStorageService.LoadAsync<string>(CurrentLanguageKey);
        if (!string.IsNullOrWhiteSpace(storedLanguage) && _resources.ContainsKey(storedLanguage))
        {
            CurrentLanguage = storedLanguage;
        }
        else
        {
            CurrentLanguage = "en";
        }

        Changed?.Invoke();
    }

    public string GetString(string key)
    {
        if (_resources.TryGetValue(CurrentLanguage, out var current) &&
            current.Resources.TryGetValue(key, out var translatedValue) &&
            !string.IsNullOrWhiteSpace(translatedValue))
        {
            return translatedValue;
        }

        if (_resources.TryGetValue("en", out var english) &&
            english.Resources.TryGetValue(key, out var fallbackValue) &&
            !string.IsNullOrWhiteSpace(fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        if (!_resources.ContainsKey(languageCode))
        {
            return;
        }

        CurrentLanguage = languageCode;
        await _localStorageService.SaveAsync(CurrentLanguageKey, CurrentLanguage);
        Changed?.Invoke();
    }

    public async Task<string> ExportLanguageAsync(string languageCode)
    {
        if (!_resources.TryGetValue(languageCode, out var resourceSet))
        {
            throw new InvalidOperationException($"Language '{languageCode}' does not exist.");
        }

        return await _localStorageService.BackupAsync(BuildExportFileName(languageCode), resourceSet);
    }

    public async Task ImportLanguageAsync(string json)
    {
        var resourceSet = await _localStorageService.RestoreAsync<LocalizationResourceSet>(json);
        if (resourceSet is null || string.IsNullOrWhiteSpace(resourceSet.LanguageCode))
        {
            throw new InvalidOperationException("The localization file is invalid.");
        }

        resourceSet.Resources ??= [];
        _resources[resourceSet.LanguageCode] = resourceSet;
        await PersistAsync();
        Changed?.Invoke();
    }

    public string this[string key] => GetString(key);

    private async Task LoadDefaultLanguageAsync(string languageCode)
    {
        var resourceSet = await _httpClient.GetFromJsonAsync<LocalizationResourceSet>($"localization/{languageCode}.json");
        if (resourceSet is not null)
        {
            _resources[languageCode] = resourceSet;
        }
    }

    private async Task PersistAsync()
    {
        await _localStorageService.SaveAsync(LocalizationKey, _resources.Values.OrderBy(set => set.LanguageCode).ToList());
    }

    private static string BuildExportFileName(string baseName) =>
        $"{baseName}-{DateTime.Now:yyyyMMddHHmm}.json";
}
