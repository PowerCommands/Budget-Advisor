using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class SavingsAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public SavingsAccountType AccountType { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public DateOnly CreatedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public decimal OpeningBalance { get; set; }

    public decimal CurrentBalance { get; set; }
}
