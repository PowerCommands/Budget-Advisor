namespace BudgetAdvisor.Domain.Models;

public sealed class BrowserStorageUsageSnapshot
{
    public const long DefaultTotalBytes = 10L * 1024L * 1024L;

    public long TotalBytes { get; set; } = DefaultTotalBytes;

    public long UsedBytes { get; set; }

    public long DisplayUsedBytes => Math.Min(Math.Max(UsedBytes, 0L), TotalBytes);

    public long FreeBytes => Math.Max(0L, TotalBytes - DisplayUsedBytes);
}
