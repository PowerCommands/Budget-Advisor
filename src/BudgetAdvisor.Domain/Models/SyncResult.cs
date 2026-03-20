namespace BudgetAdvisor.Domain.Models;

public class SyncResult
{
    public bool IsSuccess { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
