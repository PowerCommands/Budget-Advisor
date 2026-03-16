namespace BudgetAdvisor.Domain.Models;

public sealed class MonthlyReportRow
{
    public int Year { get; set; }

    public int Month { get; set; }

    public decimal Income { get; set; }

    public decimal Expenses { get; set; }

    public decimal Change { get; set; }

    public decimal Balance { get; set; }
}
