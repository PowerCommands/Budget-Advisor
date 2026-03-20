using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.Services;

public sealed class ManualSyncService
{
    private const string SyncMetadataKey = "budget-advisor.sync.metadata";

    private static readonly IReadOnlyList<SyncFileDescriptor> SyncFiles =
    [
        new SyncFileDescriptor
        {
            FileKey = "application-data",
            LocalStorageKey = ApplicationState.ApplicationDataKey,
            RemotePath = "/BudgetAdvisor/application-data.json",
            DisplayName = "Application data"
        },
        new SyncFileDescriptor
        {
            FileKey = "application-log",
            LocalStorageKey = ApplicationLogService.ApplicationLogKey,
            RemotePath = "/BudgetAdvisor/application-log.json",
            DisplayName = "Activity log"
        },
        new SyncFileDescriptor
        {
            FileKey = "localization-data",
            LocalStorageKey = LocalizationService.LocalizationKey,
            RemotePath = "/BudgetAdvisor/localization.json",
            DisplayName = "Localization data"
        },
        new SyncFileDescriptor
        {
            FileKey = "localization-language",
            LocalStorageKey = LocalizationService.CurrentLanguageKey,
            RemotePath = "/BudgetAdvisor/current-language.json",
            DisplayName = "Current language"
        }
    ];

    private readonly LocalStorageService _localStorageService;
    private readonly ApplicationState _applicationState;
    private readonly IApplicationLogService _applicationLogService;
    private readonly LocalizationService _localizationService;
    private readonly IReadOnlyDictionary<string, ISyncProvider> _providers;
    private bool _isInitialized;

    public event Action? Changed;

    public SyncMetadata Metadata { get; private set; } = CreateDefaultMetadata();

    public IReadOnlyList<SyncFileDescriptor> Files => SyncFiles;

    public ManualSyncService(
        LocalStorageService localStorageService,
        ApplicationState applicationState,
        IApplicationLogService applicationLogService,
        LocalizationService localizationService,
        IEnumerable<ISyncProvider> providers)
    {
        _localStorageService = localStorageService;
        _applicationState = applicationState;
        _applicationLogService = applicationLogService;
        _localizationService = localizationService;
        _providers = providers.ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        Metadata = await _localStorageService.LoadAsync<SyncMetadata>(SyncMetadataKey) ?? CreateDefaultMetadata();
        if (string.IsNullOrWhiteSpace(Metadata.ProviderId))
        {
            Metadata.ProviderId = DropboxSyncProvider.ProviderIdValue;
        }

        await RefreshConnectionStateAsync();
        _isInitialized = true;
        Changed?.Invoke();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        await RefreshConnectionStateAsync(cancellationToken);
        Changed?.Invoke();
    }

    public async Task<SyncResult> ConnectAsync(string providerId, SyncConnectionRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var provider = GetProvider(providerId);
        var result = await provider.ConnectAsync(request, cancellationToken);
        await UpdateMetadataAfterOperationAsync(provider, result, isUpload: false, isDownload: false, cancellationToken);
        return result;
    }

    public async Task<SyncResult> UploadAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var provider = GetProvider(providerId);
        if (!await provider.IsConnectedAsync(cancellationToken))
        {
            return await StoreFailureAsync(provider, "Upload", "Connect to Dropbox before uploading.", cancellationToken);
        }

        var files = new List<SyncFilePayload>(SyncFiles.Count);
        foreach (var descriptor in SyncFiles)
        {
            var json = await _localStorageService.LoadJsonAsync(descriptor.LocalStorageKey);
            files.Add(new SyncFilePayload
            {
                Descriptor = descriptor,
                Content = json ?? string.Empty
            });
        }

        var result = await provider.UploadAsync(files, cancellationToken);
        await UpdateMetadataAfterOperationAsync(provider, result, isUpload: result.IsSuccess, isDownload: false, cancellationToken);
        return result;
    }

    public async Task<SyncResult> DownloadAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var provider = GetProvider(providerId);
        if (!await provider.IsConnectedAsync(cancellationToken))
        {
            return await StoreFailureAsync(provider, "Download", "Connect to Dropbox before downloading.", cancellationToken);
        }

        var result = await provider.DownloadAsync(SyncFiles, cancellationToken);
        if (!result.IsSuccess)
        {
            await UpdateMetadataAfterOperationAsync(provider, result, isUpload: false, isDownload: false, cancellationToken);
            return result;
        }

        var validationError = ValidateDownloadPayload(result.Files);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return await StoreFailureAsync(provider, "Download", validationError, cancellationToken);
        }

        var originalFiles = await CaptureLocalFilesAsync();

        try
        {
            foreach (var payload in result.Files)
            {
                await _localStorageService.SaveJsonAsync(payload.Descriptor.LocalStorageKey, payload.Content);
            }

            await _applicationState.ReloadFromStorageAsync();
            await _applicationLogService.ReloadAsync();
            await _localizationService.ReloadAsync();

            var success = CreateResult(true, "Download", $"Downloaded {result.Files.Count} file(s) from Dropbox and restored local data.");
            await UpdateMetadataAfterOperationAsync(provider, success, isUpload: false, isDownload: true, cancellationToken);
            return success;
        }
        catch (Exception exception)
        {
            foreach (var originalFile in originalFiles)
            {
                await _localStorageService.SaveJsonAsync(originalFile.Key, originalFile.Value);
            }

            await _applicationState.ReloadFromStorageAsync();
            await _applicationLogService.ReloadAsync();
            await _localizationService.ReloadAsync();

            return await StoreFailureAsync(
                provider,
                "Download",
                $"Download completed, but restoring local data failed. The previous local state was restored. {exception.Message}",
                cancellationToken);
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }
    }

    private static SyncMetadata CreateDefaultMetadata() =>
        new()
        {
            ProviderId = DropboxSyncProvider.ProviderIdValue,
            ProviderName = "Dropbox"
        };

    private ISyncProvider GetProvider(string providerId)
    {
        if (_providers.TryGetValue(providerId, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException($"Sync provider '{providerId}' is not registered.");
    }

    private async Task RefreshConnectionStateAsync(CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(Metadata.ProviderId);
        Metadata.ProviderName = provider.ProviderDisplayName;
        Metadata.IsConnected = await provider.IsConnectedAsync(cancellationToken);
        Metadata.ConnectedAccountName = await provider.GetConnectedAccountNameAsync(cancellationToken) ?? string.Empty;

        if (Metadata.IsConnected &&
            Metadata.LatestResult is not null &&
            string.Equals(Metadata.LatestResult.Operation, "Connect", StringComparison.OrdinalIgnoreCase) &&
            !Metadata.LatestResult.IsSuccess)
        {
            Metadata.LatestResult = new SyncResult
            {
                IsSuccess = true,
                Operation = "Connect",
                Message = string.IsNullOrWhiteSpace(Metadata.ConnectedAccountName)
                    ? "Dropbox is connected."
                    : $"Dropbox is connected as {Metadata.ConnectedAccountName}.",
                TimestampUtc = DateTimeOffset.UtcNow
            };
        }

        await PersistMetadataAsync();
    }

    private async Task UpdateMetadataAfterOperationAsync(
        ISyncProvider provider,
        SyncResult result,
        bool isUpload,
        bool isDownload,
        CancellationToken cancellationToken)
    {
        Metadata.ProviderId = provider.ProviderId;
        Metadata.ProviderName = provider.ProviderDisplayName;
        Metadata.IsConnected = await provider.IsConnectedAsync(cancellationToken);
        Metadata.ConnectedAccountName = await provider.GetConnectedAccountNameAsync(cancellationToken) ?? string.Empty;
        Metadata.LatestResult = result;

        if (isUpload)
        {
            Metadata.LastUploadUtc = result.TimestampUtc;
        }

        if (isDownload)
        {
            Metadata.LastDownloadUtc = result.TimestampUtc;
        }

        await PersistMetadataAsync();
        await LogSyncResultAsync(provider, result);
        Changed?.Invoke();
    }

    private async Task<SyncResult> StoreFailureAsync(ISyncProvider provider, string operation, string message, CancellationToken cancellationToken)
    {
        var failure = CreateResult(false, operation, message);
        await UpdateMetadataAfterOperationAsync(provider, failure, isUpload: false, isDownload: false, cancellationToken);
        return failure;
    }

    private async Task PersistMetadataAsync()
    {
        await _localStorageService.SaveAsync(SyncMetadataKey, Metadata);
    }

    private async Task LogSyncResultAsync(ISyncProvider provider, SyncResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Operation))
        {
            return;
        }

        await _applicationLogService.AddEntryAsync(
            $"Cloud Sync-{provider.ProviderDisplayName}",
            result.Message,
            result.IsSuccess ? "OK" : "Failed");
    }

    private string? ValidateDownloadPayload(IReadOnlyList<SyncFilePayload> files)
    {
        foreach (var descriptor in SyncFiles)
        {
            var payload = files.FirstOrDefault(item => string.Equals(item.Descriptor.FileKey, descriptor.FileKey, StringComparison.Ordinal));
            if (payload is null)
            {
                return $"Dropbox download did not include the required '{descriptor.DisplayName}' file.";
            }

            if (string.IsNullOrWhiteSpace(payload.Content))
            {
                return $"The downloaded '{descriptor.DisplayName}' file is empty.";
            }

            if (string.Equals(descriptor.LocalStorageKey, ApplicationState.ApplicationDataKey, StringComparison.Ordinal))
            {
                if (_localStorageService.Deserialize<BudgetAdvisor.Domain.Models.ApplicationData>(payload.Content) is null)
                {
                    return "The downloaded application data file is invalid.";
                }
            }
            else if (string.Equals(descriptor.LocalStorageKey, ApplicationLogService.ApplicationLogKey, StringComparison.Ordinal))
            {
                if (_localStorageService.Deserialize<List<BudgetAdvisor.Domain.Models.ApplicationLogEntry>>(payload.Content) is null)
                {
                    return "The downloaded activity log file is invalid.";
                }
            }
            else if (string.Equals(descriptor.LocalStorageKey, LocalizationService.LocalizationKey, StringComparison.Ordinal))
            {
                if (_localStorageService.Deserialize<List<BudgetAdvisor.Domain.Models.LocalizationResourceSet>>(payload.Content) is null)
                {
                    return "The downloaded localization data file is invalid.";
                }
            }
            else if (string.Equals(descriptor.LocalStorageKey, LocalizationService.CurrentLanguageKey, StringComparison.Ordinal))
            {
                if (_localStorageService.Deserialize<string>(payload.Content) is null)
                {
                    return "The downloaded current language file is invalid.";
                }
            }
        }

        return null;
    }

    private async Task<Dictionary<string, string>> CaptureLocalFilesAsync()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var descriptor in SyncFiles)
        {
            files[descriptor.LocalStorageKey] = await _localStorageService.LoadJsonAsync(descriptor.LocalStorageKey) ?? string.Empty;
        }

        return files;
    }

    private static SyncResult CreateResult(bool isSuccess, string operation, string message) =>
        new()
        {
            IsSuccess = isSuccess,
            Operation = operation,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow
        };
}
