namespace BudgetAdvisor.Domain.Models;

public sealed class SyncFilePayload
{
    public SyncFileDescriptor Descriptor { get; set; } = new();

    public string Content { get; set; } = string.Empty;
}
