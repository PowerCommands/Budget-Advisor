namespace BudgetAdvisor.Domain.Models;

public sealed class ApplicationData
{
    public string CurrencyCode { get; set; } = "SEK";
    public string DecimalSeparator { get; set; } = ",";
    public string ThousandsSeparator { get; set; } = " ";
    public string ThemeMode { get; set; } = "Primary";

    public List<HouseholdMember> Members { get; set; } = [];

    public List<IncomeEntry> IncomeRecords { get; set; } = [];

    public List<ExpenseEntry> ExpenseRecords { get; set; } = [];

    public List<SubscriptionExpenseDefinition> Subscriptions { get; set; } = [];

    public List<DebtItem> Debts { get; set; } = [];

    public List<SavingsItem> Savings { get; set; } = [];

    public List<AssetItem> Assets { get; set; } = [];
}
