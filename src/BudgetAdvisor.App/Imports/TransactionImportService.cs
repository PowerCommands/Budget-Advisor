namespace BudgetAdvisor.App.Imports;

public sealed class TransactionImportService
{
    private readonly ITransactionImportDetector _detector;

    public TransactionImportService(ITransactionImportDetector detector)
    {
        _detector = detector;
    }

    public DetectedTransactionImport Parse(TransactionImportFile file)
    {
        var importer = _detector.Detect(file);
        var candidates = importer.Parse(file);

        return new DetectedTransactionImport
        {
            ImporterKey = importer.ImporterKey,
            DisplayName = importer.DisplayName,
            LogoPath = importer.LogoPath,
            Candidates = candidates
        };
    }
}
