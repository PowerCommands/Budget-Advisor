using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class ImportedExpenseCategorySuggestion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Description { get; set; } = string.Empty;

    public ExpenseCategory Category { get; set; }

    public string Subcategory { get; set; } = string.Empty;

    public ImportSuggestionMatchType MatchType { get; set; } = ImportSuggestionMatchType.Exact;

    public ImportSuggestionAction Action { get; set; } = ImportSuggestionAction.ApplySuggestion;
}
