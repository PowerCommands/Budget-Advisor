using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class ExpenseFilterCategoryOption
{
    public string Value { get; set; } = string.Empty;

    public ExpenseCategory Category { get; set; }

    public string Subcategory { get; set; } = string.Empty;
}
