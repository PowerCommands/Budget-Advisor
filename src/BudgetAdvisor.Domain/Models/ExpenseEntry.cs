using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class ExpenseEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public decimal Amount { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public ExpenseCategory Category { get; set; }

    public string Subcategory { get; set; } = string.Empty;

    public string Metadata { get; set; } = string.Empty;

    public Guid? SubscriptionDefinitionId { get; set; }

    public Guid? HousingDefinitionId { get; set; }

    public Guid? TransportDefinitionId { get; set; }

    public Guid? LoanId { get; set; }

    public Guid? LoanInterestBindingPeriodId { get; set; }

    public Guid? LoanAmortizationPlanId { get; set; }

    public Guid? TransportLeasingContractId { get; set; }

    public Guid? TransportVehicleId { get; set; }

    public Guid? CreditId { get; set; }

    public Guid? SavingsAccountId { get; set; }

    public Guid? AssetId { get; set; }

    public CreditCostSource? CreditCostSource { get; set; }

    public string Description { get; set; } = string.Empty;
}
