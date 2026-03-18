using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.Services;

public interface IBrowserStorageUsageService
{
    Task<BrowserStorageUsageSnapshot> GetUsageAsync();
}
