namespace BudgetAdvisor.App.Imports;

public sealed class TransactionImportService
{
    private readonly ITransactionImportDetector _detector;

    public TransactionImportService(ITransactionImportDetector detector)
    {
        _detector = detector;
    }

    public DetectedTransactionImport Parse(string fileContent)
    {
        var importer = _detector.Detect(fileContent);
        var candidates = importer.Parse(fileContent);

        return new DetectedTransactionImport
        {
            ImporterKey = importer.ImporterKey,
            DisplayName = importer.DisplayName,
            LogoPath = importer.LogoPath,
            Candidates = candidates
        };
    }
}
