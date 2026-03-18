namespace BudgetAdvisor.Domain.Models;

public sealed class SalaryIncomePeriodDisplay
{
    public Guid Id { get; set; }

    public Guid SeriesId { get; set; }

    public Guid MemberId { get; set; }

    public string MemberName { get; set; } = string.Empty;

    public decimal MonthlyAmount { get; set; }

    public int StartYear { get; set; }

    public int StartMonth { get; set; }

    public int EndYear { get; set; }

    public int EndMonth { get; set; }
}
