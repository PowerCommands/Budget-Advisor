namespace BudgetAdvisor.Domain.Models;

public readonly record struct MonthKey(int Year, int Month)
{
    public DateOnly ToDateOnly() => new(Year, Month, 1);
}
