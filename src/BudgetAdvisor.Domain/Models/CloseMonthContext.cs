namespace BudgetAdvisor.Domain.Models;

public sealed class CloseMonthContext
{
    public DateOnly Month { get; set; }

    public bool IsClosed { get; set; }

    public decimal Surplus { get; set; }

    public IReadOnlyList<SavingsAccount> BankAccounts { get; set; } = [];

    public IReadOnlyList<SavingsAccount> FundAccounts { get; set; } = [];
}
