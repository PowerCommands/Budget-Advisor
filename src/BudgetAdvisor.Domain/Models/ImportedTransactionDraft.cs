using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class ImportedTransactionDraft
{
    public Guid? MemberId { get; set; }

    public DateOnly Date { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public decimal SignedAmount { get; set; }

    public bool IsRecurring { get; set; }

    public Guid ImportId { get; set; }

    public int ImportOccurrence { get; set; }

    public SubcategoryMainCategory MainCategory { get; set; }

    public string Subcategory { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
