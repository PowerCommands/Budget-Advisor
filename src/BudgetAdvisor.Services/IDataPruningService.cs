using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.Services;

public interface IDataPruningService
{
    DataPruningSummary Prune(ApplicationData data, DateOnly cutoffDate);
}
