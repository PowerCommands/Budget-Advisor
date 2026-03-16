using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class ExpenseEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public decimal Amount { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public ExpenseCategory Category { get; set; }

    public string Subcategory { get; set; } = string.Empty;

    public Guid? SubscriptionDefinitionId { get; set; }

    public string Description { get; set; } = string.Empty;
}
