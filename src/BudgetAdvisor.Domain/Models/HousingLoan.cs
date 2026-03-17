namespace BudgetAdvisor.Domain.Models;

public sealed class HousingLoan
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Lender { get; set; } = string.Empty;

    public DateOnly LoanStartDate { get; set; }

    public decimal InitialLoanAmount { get; set; }

    public decimal StartingRemainingDebt { get; set; }

    public decimal RemainingDebt { get; set; }

    public decimal CurrentAmortization { get; set; }
}
