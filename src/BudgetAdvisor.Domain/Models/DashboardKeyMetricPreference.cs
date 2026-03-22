namespace BudgetAdvisor.Domain.Models;

public sealed class DashboardKeyMetricPreference
{
    public const string LoanToValueRatioKey = "loanToValueRatio";
    public const string HousingLoansKey = "housingLoans";
    public const string PropertyValueKey = "propertyValue";
    public const string CreditsKey = "credits";
    public const string SavingsKey = "savings";
    public const string InterestKey = "interest";
    public const string AmortizationKey = "amortization";
    public const string BalanceKey = "balance";
    public const string LeasingKey = "leasing";
    public const string AverageSalaryKey = "averageSalary";
    public const string AverageElectricityKey = "averageElectricity";
    public const string AverageFuelKey = "averageFuel";
    public const string AverageInsuranceKey = "averageInsurance";

    public static IReadOnlyList<string> DefaultOrder { get; } =
    [
        LoanToValueRatioKey,
        HousingLoansKey,
        PropertyValueKey,
        CreditsKey,
        SavingsKey,
        InterestKey,
        AmortizationKey,
        BalanceKey,
        LeasingKey,
        AverageSalaryKey,
        AverageElectricityKey,
        AverageFuelKey,
        AverageInsuranceKey
    ];

    public string Key { get; set; } = string.Empty;

    public bool IsVisible { get; set; } = true;
}
