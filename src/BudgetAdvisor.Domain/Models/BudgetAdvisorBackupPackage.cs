namespace BudgetAdvisor.Domain.Models;

public sealed class BudgetAdvisorBackupPackage
{
    public ApplicationData ApplicationData { get; set; } = new();

    public TransactionImportData TransactionImportData { get; set; } = new();
}
