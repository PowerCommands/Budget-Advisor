namespace BudgetAdvisor.App.Imports;

public sealed class TransactionImportException : Exception
{
    public TransactionImportException(string message)
        : base(message)
    {
    }
}
