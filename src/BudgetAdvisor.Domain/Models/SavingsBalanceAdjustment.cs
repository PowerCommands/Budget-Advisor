namespace BudgetAdvisor.Domain.Models;

public sealed class SavingsBalanceAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SavingsAccountId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public decimal Amount { get; set; }
}
