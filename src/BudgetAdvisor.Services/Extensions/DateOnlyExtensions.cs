namespace BudgetAdvisor.Services.Extensions;

internal static class DateOnlyExtensions
{
    public static DateOnly AddMonthsSafe(this DateOnly date, int months) => date.AddMonths(months);
}
