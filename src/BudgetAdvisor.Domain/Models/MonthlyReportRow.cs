namespace BudgetAdvisor.Domain.Models;

public sealed class MonthlyReportRow
{
    public string Period { get; set; } = string.Empty;

    public int Year { get; set; }

    public int Month { get; set; }

    public decimal Income { get; set; }

    public decimal Expenses { get; set; }

    public decimal HousingLoans { get; set; }

    public decimal Credits { get; set; }

    public decimal Savings { get; set; }

    public decimal Interest { get; set; }

    public decimal Amortization { get; set; }

    public decimal Balance { get; set; }

    public bool IsClosed { get; set; }
}
