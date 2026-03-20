using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.Services;

public sealed class ApplicationLogService : IApplicationLogService
{
    public const string ApplicationLogKey = "budget-advisor.application-log";

    private readonly LocalStorageService _localStorageService;
    private readonly List<ApplicationLogEntry> _entries = [];
    private bool _isInitialized;

    public event Action? Changed;

    public IReadOnlyList<ApplicationLogEntry> Entries =>
        _entries
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();

    public ApplicationLogService(LocalStorageService localStorageService)
    {
        _localStorageService = localStorageService;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await LoadEntriesAsync();
        _isInitialized = true;
    }

    public async Task ReloadAsync()
    {
        await LoadEntriesAsync();
        _isInitialized = true;
        Changed?.Invoke();
    }

    public async Task AddEntryAsync(string description, string activity, string status)
    {
        await EnsureInitializedAsync();

        _entries.Add(new ApplicationLogEntry
        {
            Timestamp = DateTime.Now,
            Description = description.Trim(),
            Activity = activity.Trim(),
            Status = status.Trim()
        });

        await PersistAsync();
        Changed?.Invoke();
    }

    public async Task<string> ExportAsync()
    {
        await EnsureInitializedAsync();
        var fileName = $"budget-advisor-activity-log-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        return await _localStorageService.BackupAsync(fileName, _entries.OrderByDescending(entry => entry.Timestamp).ToList());
    }

    public async Task ClearAsync()
    {
        await EnsureInitializedAsync();
        _entries.Clear();
        await PersistAsync();
        Changed?.Invoke();
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }
    }

    private async Task PersistAsync()
    {
        await _localStorageService.SaveAsync(ApplicationLogKey, _entries);
    }

    private async Task LoadEntriesAsync()
    {
        var storedEntries = await _localStorageService.LoadAsync<List<ApplicationLogEntry>>(ApplicationLogKey);
        _entries.Clear();
        if (storedEntries is not null)
        {
            _entries.AddRange(storedEntries);
        }
    }
}
