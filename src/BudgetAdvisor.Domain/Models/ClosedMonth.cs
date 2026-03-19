namespace BudgetAdvisor.Domain.Models;

public sealed class ClosedMonth
{
    public int Year { get; set; }

    public int Month { get; set; }

    public Guid? GeneratedExpenseId { get; set; }

    public DateOnly ToDateOnly() => new(Year, Month, 1);
}
