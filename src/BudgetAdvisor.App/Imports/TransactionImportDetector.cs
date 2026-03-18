namespace BudgetAdvisor.App.Imports;

public sealed class TransactionImportDetector : ITransactionImportDetector
{
    private readonly IReadOnlyList<ITransactionImporter> _importers;

    public TransactionImportDetector(IEnumerable<ITransactionImporter> importers)
    {
        _importers = importers.ToList();
    }

    public ITransactionImporter Detect(string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            throw new TransactionImportException("The selected file is empty.");
        }

        var importer = _importers.FirstOrDefault(candidate => candidate.CanImport(fileContent));
        if (importer is null)
        {
            throw new TransactionImportException("The selected file format is not recognized.");
        }

        return importer;
    }
}
