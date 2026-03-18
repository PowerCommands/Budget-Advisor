namespace BudgetAdvisor.Domain.Models;

public sealed class LoanInterestBindingPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LoanId { get; set; }

    // Legacy compatibility for older persisted data.
    public DateOnly StartDate { get; set; }

    // Legacy compatibility for older persisted data.
    public DateOnly EndDate { get; set; }

    public DateOnly StartMonth { get; set; }

    public DateOnly EndMonth { get; set; }

    public decimal InterestRate { get; set; }

    public decimal MonthlyAmortization { get; set; }
}
