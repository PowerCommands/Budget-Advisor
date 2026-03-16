namespace BudgetAdvisor.Domain.Models;

public sealed class MonthlyOverview
{
    public int Year { get; set; }

    public int Month { get; set; }

    public decimal Income { get; set; }

    public decimal Housing { get; set; }

    public decimal Food { get; set; }

    public decimal Transport { get; set; }

    public decimal Clothing { get; set; }

    public decimal Savings { get; set; }

    public decimal Other { get; set; }

    public decimal Credits { get; set; }

    public decimal TotalExpenses => Housing + Food + Transport + Clothing + Savings + Other + Credits;

    public decimal NetChange => Income - TotalExpenses;
}
