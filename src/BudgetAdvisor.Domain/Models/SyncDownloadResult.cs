namespace BudgetAdvisor.Domain.Models;

public sealed class SyncDownloadResult : SyncResult
{
    public IReadOnlyList<SyncFilePayload> Files { get; set; } = [];
}
