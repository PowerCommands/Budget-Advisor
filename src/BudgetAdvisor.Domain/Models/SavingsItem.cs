namespace BudgetAdvisor.Domain.Models;

public sealed class SavingsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}
