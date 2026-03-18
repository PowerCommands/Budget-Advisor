namespace BudgetAdvisor.App.Imports;

public interface ITransactionImportDetector
{
    ITransactionImporter Detect(string fileContent);
}
