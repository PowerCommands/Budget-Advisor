using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.Services;

public interface ISyncProvider
{
    string ProviderId { get; }

    string ProviderDisplayName { get; }

    Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default);

    Task<string?> GetConnectedAccountNameAsync(CancellationToken cancellationToken = default);

    Task<SyncResult> ConnectAsync(SyncConnectionRequest request, CancellationToken cancellationToken = default);

    Task<SyncResult> UploadAsync(IReadOnlyList<SyncFilePayload> files, CancellationToken cancellationToken = default);

    Task<SyncDownloadResult> DownloadAsync(IReadOnlyList<SyncFileDescriptor> files, CancellationToken cancellationToken = default);
}
