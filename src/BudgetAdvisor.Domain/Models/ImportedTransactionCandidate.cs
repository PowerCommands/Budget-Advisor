namespace BudgetAdvisor.Domain.Models;

public sealed class ImportedTransactionCandidate
{
    public DateOnly TransactionDate { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal? Balance { get; set; }

    public string? Currency { get; set; }

    public string? SourceBank { get; set; }

    public string? SourceFormat { get; set; }
}
