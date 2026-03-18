namespace BudgetAdvisor.Domain.Models;

public sealed class MonthlyBalance
{
    public Guid EntityId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public decimal Balance { get; set; }
}
