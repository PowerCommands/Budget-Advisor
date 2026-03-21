using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class SubscriptionExpenseDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public int IntervalMonths { get; set; }

    public int StartYear { get; set; }

    public int StartMonth { get; set; }

    public int? EndYear { get; set; }

    public int? EndMonth { get; set; }

    public Guid? ImportId { get; set; }

    public int? ImportOccurrence { get; set; }

    public ExpenseCategory Category { get; set; }

    public string Subcategory { get; set; } = string.Empty;
}
