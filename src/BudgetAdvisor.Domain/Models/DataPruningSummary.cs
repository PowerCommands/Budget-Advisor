namespace BudgetAdvisor.Domain.Models;

public sealed class DataPruningSummary
{
    public int IncomeEntriesRemoved { get; set; }

    public int ExpenseEntriesRemoved { get; set; }

    public int MonthlyBalancesRemoved { get; set; }

    public int LoanBindingPeriodsRemoved { get; set; }

    public int LoanAmortizationPlansRemoved { get; set; }

    public int SalarySeriesRemoved { get; set; }

    public int RecurringExpenseDefinitionsRemoved { get; set; }

    public int HousingDefinitionsRemoved { get; set; }

    public int TransportDefinitionsRemoved { get; set; }

    public int TransportLeasingContractsRemoved { get; set; }

    public int SavingsReturnPeriodsRemoved { get; set; }

    public int SavingsGeneratedReturnsRemoved { get; set; }

    public int TotalRemoved =>
        IncomeEntriesRemoved +
        ExpenseEntriesRemoved +
        MonthlyBalancesRemoved +
        LoanBindingPeriodsRemoved +
        LoanAmortizationPlansRemoved +
        SalarySeriesRemoved +
        RecurringExpenseDefinitionsRemoved +
        HousingDefinitionsRemoved +
        TransportDefinitionsRemoved +
        TransportLeasingContractsRemoved +
        SavingsReturnPeriodsRemoved +
        SavingsGeneratedReturnsRemoved;
}
