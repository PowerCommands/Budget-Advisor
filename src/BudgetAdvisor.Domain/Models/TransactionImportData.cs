namespace BudgetAdvisor.Domain.Models;

public sealed class TransactionImportData
{
    public List<TransactionImportBatch> ImportBatches { get; set; } = [];

    public List<ImportedExpenseCategorySuggestion> ImportedExpenseCategorySuggestions { get; set; } = [];
}
