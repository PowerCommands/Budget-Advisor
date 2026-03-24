namespace BudgetAdvisor.App.Imports;

public sealed class TransactionImportDetector : ITransactionImportDetector
{
    private readonly IReadOnlyList<ITransactionImporter> _importers;

    public TransactionImportDetector(IEnumerable<ITransactionImporter> importers)
    {
        _importers = importers.ToList();
    }

    public ITransactionImporter Detect(TransactionImportFile file)
    {
        if (file.Content.Length == 0)
        {
            throw new TransactionImportException("The selected file is empty.");
        }

        var importer = _importers.FirstOrDefault(candidate => candidate.CanImport(file));
        if (importer is null)
        {
            throw new TransactionImportException("The selected file format is not recognized.");
        }

        return importer;
    }
}
