namespace BudgetAdvisor.Domain.Models;

public sealed class Credit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public decimal CreditLimit { get; set; }

    public decimal StartingRemainingDebt { get; set; }

    public decimal RemainingDebt { get; set; }

    public decimal MonthlyInterestRate { get; set; }

    public bool ResetAtEndOfMonth { get; set; }
}
