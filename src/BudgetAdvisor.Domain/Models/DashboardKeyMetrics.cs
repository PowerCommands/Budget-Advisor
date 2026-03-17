namespace BudgetAdvisor.Domain.Models;

public sealed class DashboardKeyMetrics
{
    public decimal LoanToValueRatio { get; set; }

    public decimal HousingLoans { get; set; }

    public decimal PropertyValue { get; set; }

    public decimal Credits { get; set; }

    public decimal Savings { get; set; }

    public decimal Interest { get; set; }

    public decimal Amortization { get; set; }

    public decimal Balance { get; set; }

    public decimal Change { get; set; }
}
