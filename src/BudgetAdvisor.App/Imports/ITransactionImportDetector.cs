namespace BudgetAdvisor.App.Imports;

public interface ITransactionImportDetector
{
    ITransactionImporter Detect(TransactionImportFile file);
}
