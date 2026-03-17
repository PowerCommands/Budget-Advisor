namespace BudgetAdvisor.Domain.Models;

public sealed class SavingsReturnPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SavingsAccountId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public decimal RatePercent { get; set; }

    public decimal TaxPercent { get; set; }
}
