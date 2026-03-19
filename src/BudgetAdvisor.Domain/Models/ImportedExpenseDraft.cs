using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class ImportedExpenseDraft
{
    public DateOnly Date { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public decimal Amount { get; set; }

    public bool IsRecurring { get; set; }

    public ExpenseCategory Category { get; set; }

    public string Subcategory { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
