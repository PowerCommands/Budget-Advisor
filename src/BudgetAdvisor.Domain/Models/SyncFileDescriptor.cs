namespace BudgetAdvisor.Domain.Models;

public sealed class SyncFileDescriptor
{
    public string FileKey { get; set; } = string.Empty;

    public string LocalStorageKey { get; set; } = string.Empty;

    public string RemotePath { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
