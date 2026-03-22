namespace BudgetAdvisor.Domain.Models;

public sealed class DashboardKeyMetrics
{
    public decimal LoanToValueRatio { get; set; }

    public decimal HousingLoans { get; set; }

    public decimal PropertyValue { get; set; }

    public decimal Credits { get; set; }

    public decimal Savings { get; set; }

    public decimal Interest { get; set; }

    public decimal Amortization { get; set; }

    public decimal Balance { get; set; }

    public int LeasingMonthsRemaining { get; set; }

    public string LeasingVehicleName { get; set; } = string.Empty;

    public int? LeasingEndYear { get; set; }

    public int? LeasingEndMonth { get; set; }

    public decimal AverageSalary { get; set; }

    public decimal AverageElectricity { get; set; }

    public decimal AverageFuel { get; set; }

    public decimal AverageInsurance { get; set; }

    public decimal Change { get; set; }
}
