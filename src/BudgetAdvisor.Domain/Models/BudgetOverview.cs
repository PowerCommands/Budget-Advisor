namespace BudgetAdvisor.Domain.Models;

public sealed class BudgetOverview
{
    public decimal Income { get; set; }

    public decimal Housing { get; set; }

    public decimal Food { get; set; }

    public decimal Transport { get; set; }

    public decimal Clothing { get; set; }

    public decimal Savings { get; set; }

    public decimal Other { get; set; }

    public decimal Credits { get; set; }

    public int ClosedMonthCount { get; set; }

    public int? StartYear { get; set; }

    public int? StartMonth { get; set; }

    public int? EndYear { get; set; }

    public int? EndMonth { get; set; }

    public decimal TotalExpenses => Housing + Food + Transport + Clothing + Savings + Other + Credits;

    public decimal NetChange => Income - TotalExpenses;
}
