namespace BudgetAdvisor.Domain.Models;

public sealed class TransportLeasingContract
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? VehicleId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public decimal MonthlyCost { get; set; }
}
