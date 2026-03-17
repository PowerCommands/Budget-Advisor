namespace BudgetAdvisor.Domain.Models;

public sealed class HousingCostDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public decimal Amount { get; set; }

    public int IntervalMonths { get; set; }

    public int StartYear { get; set; }

    public int StartMonth { get; set; }

    public int EndYear { get; set; }

    public int EndMonth { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Subcategory { get; set; } = string.Empty;
}
