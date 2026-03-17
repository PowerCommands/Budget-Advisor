namespace BudgetAdvisor.Domain.Models;

public sealed class LoanAmortizationPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LoanId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public decimal MonthlyAmortizationAmount { get; set; }
}
