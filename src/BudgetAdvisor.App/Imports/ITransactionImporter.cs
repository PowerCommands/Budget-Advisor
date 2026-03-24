using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.App.Imports;

public interface ITransactionImporter
{
    string ImporterKey { get; }

    string DisplayName { get; }

    string LogoPath { get; }

    bool CanImport(TransactionImportFile file);

    IReadOnlyList<ImportedTransactionCandidate> Parse(TransactionImportFile file);
}
