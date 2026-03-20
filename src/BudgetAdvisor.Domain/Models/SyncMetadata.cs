using System.Text.Json.Serialization;

namespace BudgetAdvisor.Domain.Models;

public sealed class SyncMetadata
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public bool IsConnected { get; set; }

    public string ConnectedAccountName { get; set; } = string.Empty;

    public DateTimeOffset? LastUploadUtc { get; set; }

    public DateTimeOffset? LastDownloadUtc { get; set; }

    [JsonIgnore]
    public SyncResult? LatestResult { get; set; }
}
