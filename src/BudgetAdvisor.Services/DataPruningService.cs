using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.Services;

public sealed class DataPruningService : IDataPruningService
{
    public DataPruningSummary Prune(ApplicationData data, DateOnly cutoffDate)
    {
        ArgumentNullException.ThrowIfNull(data);

        var summary = new DataPruningSummary();

        summary.IncomeEntriesRemoved = data.IncomeRecords.RemoveAll(entry =>
            new DateOnly(entry.Year, entry.Month, 1) < cutoffDate);

        summary.ExpenseEntriesRemoved = data.ExpenseRecords.RemoveAll(entry =>
            new DateOnly(entry.Year, entry.Month, 1) < cutoffDate);

        summary.MonthlyBalancesRemoved = data.MonthlyBalances.RemoveAll(balance =>
            new DateOnly(balance.Year, balance.Month, 1) < cutoffDate);

        summary.ClosedMonthsRemoved = data.ClosedMonths.RemoveAll(month =>
            new DateOnly(month.Year, month.Month, 1) < cutoffDate);

        summary.LoanBindingPeriodsRemoved = data.LoanInterestBindingPeriods.RemoveAll(period =>
            NormalizeMonth(period.EndMonth) < cutoffDate);

        summary.LoanAmortizationPlansRemoved = data.LoanAmortizationPlans.RemoveAll(plan =>
            NormalizeMonth(plan.EndDate) < cutoffDate);

        var removedSeriesIds = data.SalaryIncomePeriods
            .Where(period => new DateOnly(period.EndYear, period.EndMonth, 1) < cutoffDate)
            .Select(period => period.SeriesId)
            .Distinct()
            .ToHashSet();

        summary.SalarySeriesRemoved = data.SalaryIncomePeriods.RemoveAll(period =>
            new DateOnly(period.EndYear, period.EndMonth, 1) < cutoffDate);

        if (removedSeriesIds.Count > 0)
        {
            summary.IncomeEntriesRemoved += data.IncomeRecords.RemoveAll(entry =>
                entry.SeriesId.HasValue && removedSeriesIds.Contains(entry.SeriesId.Value));
        }

        summary.RecurringExpenseDefinitionsRemoved = data.Subscriptions.RemoveAll(definition =>
            definition.EndYear.HasValue &&
            definition.EndMonth.HasValue &&
            new DateOnly(definition.EndYear.Value, definition.EndMonth.Value, 1) < cutoffDate);

        summary.HousingDefinitionsRemoved = data.HousingDefinitions.RemoveAll(definition =>
            new DateOnly(definition.EndYear, definition.EndMonth, 1) < cutoffDate);

        summary.TransportDefinitionsRemoved = data.TransportDefinitions.RemoveAll(definition =>
            new DateOnly(definition.EndYear, definition.EndMonth, 1) < cutoffDate);

        summary.TransportLeasingContractsRemoved = data.TransportLeasingContracts.RemoveAll(contract =>
            NormalizeMonth(contract.EndDate) < cutoffDate);

        summary.SavingsReturnPeriodsRemoved = data.SavingsReturnPeriods.RemoveAll(period =>
            NormalizeMonth(period.EndDate) < cutoffDate);

        summary.SavingsBalanceAdjustmentsRemoved = data.SavingsBalanceAdjustments.RemoveAll(item =>
            new DateOnly(item.Year, item.Month, 1) < cutoffDate);

        summary.SavingsGeneratedReturnsRemoved = data.SavingsGeneratedReturns.RemoveAll(item =>
            new DateOnly(item.Year, item.Month, 1) < cutoffDate);

        return summary;
    }

    private static DateOnly NormalizeMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
