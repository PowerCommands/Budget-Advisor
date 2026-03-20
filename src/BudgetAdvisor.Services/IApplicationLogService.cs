using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.Services;

public interface IApplicationLogService
{
    event Action? Changed;

    IReadOnlyList<ApplicationLogEntry> Entries { get; }

    Task InitializeAsync();

    Task ReloadAsync();

    Task AddEntryAsync(string description, string activity, string status);

    Task<string> ExportAsync();

    Task ClearAsync();
}
