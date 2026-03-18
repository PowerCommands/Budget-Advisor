using BudgetAdvisor.Domain.Theming;

namespace BudgetAdvisor.Domain.Models;

public sealed class ApplicationData
{
    public const int UpcomingExpensesShowAllMonths = -1;

    public string CurrencyCode { get; set; } = "SEK";
    public string DecimalSeparator { get; set; } = ",";
    public string ThousandsSeparator { get; set; } = " ";
    public string ThemeMode { get; set; } = AppThemeNames.Standard;
    public int UpcomingExpensesMonths { get; set; } = 3;
    public decimal? UpcomingExpensesMinimumAmount { get; set; }

    public List<HouseholdMember> Members { get; set; } = [];

    public List<IncomeEntry> IncomeRecords { get; set; } = [];

    public List<SalaryIncomePeriod> SalaryIncomePeriods { get; set; } = [];

    public List<ExpenseEntry> ExpenseRecords { get; set; } = [];

    public List<SubscriptionExpenseDefinition> Subscriptions { get; set; } = [];

    public List<HousingCostDefinition> HousingDefinitions { get; set; } = [];

    public List<HousingLoan> HousingLoans { get; set; } = [];

    public HomeResidence? HomeResidence { get; set; }

    public List<TransportCostDefinition> TransportDefinitions { get; set; } = [];

    public List<TransportVehicle> TransportVehicles { get; set; } = [];

    public List<TransportLoan> TransportLoans { get; set; } = [];

    public List<LoanInterestBindingPeriod> LoanInterestBindingPeriods { get; set; } = [];

    public List<LoanAmortizationPlan> LoanAmortizationPlans { get; set; } = [];

    public List<MonthlyBalance> MonthlyBalances { get; set; } = [];

    public List<TransportLeasingContract> TransportLeasingContracts { get; set; } = [];

    public List<Credit> Credits { get; set; } = [];

    public List<DebtItem> Debts { get; set; } = [];

    public List<SavingsAccount> SavingsAccounts { get; set; } = [];

    public List<SavingsReturnPeriod> SavingsReturnPeriods { get; set; } = [];

    public List<SavingsGeneratedReturn> SavingsGeneratedReturns { get; set; } = [];

    public List<SavingsItem> Savings { get; set; } = [];

    public List<AssetItem> Assets { get; set; } = [];
}
