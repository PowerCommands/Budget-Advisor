namespace BudgetAdvisor.Domain.Models;

public sealed class TransportCostDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? VehicleId { get; set; }

    public decimal Amount { get; set; }

    public int IntervalMonths { get; set; }

    public int StartYear { get; set; }

    public int StartMonth { get; set; }

    public int EndYear { get; set; }

    public int EndMonth { get; set; }

    public string Subcategory { get; set; } = string.Empty;
}
