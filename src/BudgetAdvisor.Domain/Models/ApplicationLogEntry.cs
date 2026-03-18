namespace BudgetAdvisor.Domain.Models;

public sealed class ApplicationLogEntry
{
    public DateTime Timestamp { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Activity { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}
