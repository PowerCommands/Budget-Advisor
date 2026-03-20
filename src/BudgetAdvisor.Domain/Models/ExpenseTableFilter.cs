using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class ExpenseTableFilter
{
    public ExpenseQueryScope Scope { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CategoryFilter { get; set; } = string.Empty;

    public string SubcategoryFilter { get; set; } = string.Empty;

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }
}
