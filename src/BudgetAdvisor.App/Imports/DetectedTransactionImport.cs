using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.App.Imports;

public sealed class DetectedTransactionImport
{
    public string ImporterKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string LogoPath { get; set; } = string.Empty;

    public IReadOnlyList<ImportedTransactionCandidate> Candidates { get; set; } = [];
}
