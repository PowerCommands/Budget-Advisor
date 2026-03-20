using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BudgetAdvisor.Domain.Models;
using Microsoft.JSInterop;

namespace BudgetAdvisor.Services;

public sealed class DropboxSyncProvider : ISyncProvider
{
    public const string ProviderIdValue = "dropbox";
    public const string CallbackPath = "auth/dropbox/callback";
    private const string AuthStateKey = "budget-advisor.sync.dropbox.auth";
    private const string DropboxTokenUrl = "https://api.dropboxapi.com/oauth2/token";
    private const string DropboxCurrentAccountUrl = "https://api.dropboxapi.com/2/users/get_current_account";
    private const string DropboxCreateFolderUrl = "https://api.dropboxapi.com/2/files/create_folder_v2";
    private const string DropboxUploadUrl = "https://content.dropboxapi.com/2/files/upload";
    private const string DropboxDownloadUrl = "https://content.dropboxapi.com/2/files/download";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly LocalStorageService _localStorageService;
    private readonly DropboxOptions _options;
    private DropboxAuthState? _authState;

    public string ProviderId => ProviderIdValue;

    public string ProviderDisplayName => "Dropbox";

    public DropboxSyncProvider(HttpClient httpClient, IJSRuntime jsRuntime, LocalStorageService localStorageService, DropboxOptions options)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _localStorageService = localStorageService;
        _options = options;
    }

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
    {
        var authState = await LoadAuthStateAsync();
        return authState is not null &&
               !string.IsNullOrWhiteSpace(authState.PublicClientKey) &&
               !string.IsNullOrWhiteSpace(authState.RefreshToken);
    }

    public async Task<string?> GetConnectedAccountNameAsync(CancellationToken cancellationToken = default)
    {
        var authState = await LoadAuthStateAsync();
        return authState?.AccountDisplayName;
    }

    public async Task<SyncResult> ConnectAsync(SyncConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var publicClientKey = _options.AppKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(publicClientKey))
        {
            return CreateResult(false, "Connect", "Dropbox app key is missing from configuration.");
        }

        if (await IsConnectedAsync(cancellationToken))
        {
            var connectedAccountName = await GetConnectedAccountNameAsync(cancellationToken);
            return CreateResult(
                true,
                "Connect",
                string.IsNullOrWhiteSpace(connectedAccountName)
                    ? "Dropbox is already connected."
                    : $"Dropbox is already connected as {connectedAccountName}.");
        }

        try
        {
            var authorization = await _jsRuntime.InvokeAsync<DropboxAuthorizationResponse>(
                "budgetAdvisor.dropbox.connect",
                cancellationToken,
                publicClientKey,
                CallbackPath);

            if (authorization is null ||
                string.IsNullOrWhiteSpace(authorization.Code) ||
                string.IsNullOrWhiteSpace(authorization.CodeVerifier) ||
                string.IsNullOrWhiteSpace(authorization.RedirectUri))
            {
                return CreateResult(false, "Connect", "Dropbox did not return a valid authorization response.");
            }

            var token = await ExchangeAuthorizationCodeAsync(publicClientKey, authorization, cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return CreateResult(false, "Connect", "Dropbox token exchange failed.");
            }

            var account = await GetCurrentAccountAsync(token.AccessToken, cancellationToken);
            if (account is null)
            {
                return CreateResult(false, "Connect", "Connected to Dropbox, but failed to load the account profile.");
            }

            _authState = new DropboxAuthState
            {
                PublicClientKey = publicClientKey,
                AccessToken = token.AccessToken,
                AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 60)),
                RefreshToken = string.IsNullOrWhiteSpace(token.RefreshToken) ? _authState?.RefreshToken ?? string.Empty : token.RefreshToken,
                AccountDisplayName = account.Name?.DisplayName?.Trim() ?? "Dropbox account",
                AccountEmail = account.Email?.Trim() ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(_authState.RefreshToken))
            {
                return CreateResult(false, "Connect", "Dropbox did not return a refresh token. Confirm that offline access is enabled for the app.");
            }

            await PersistAuthStateAsync();
            return CreateResult(true, "Connect", $"Connected to Dropbox as {_authState.AccountDisplayName}.");
        }
        catch (JSException exception)
        {
            return CreateResult(false, "Connect", exception.Message);
        }
        catch (Exception exception)
        {
            return CreateResult(false, "Connect", $"Dropbox connection failed. {exception.Message}");
        }
    }

    public async Task<SyncResult> UploadAsync(IReadOnlyList<SyncFilePayload> files, CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await EnsureAccessTokenAsync(cancellationToken);
            await EnsureRemoteFolderAsync(accessToken, cancellationToken);

            foreach (var file in files)
            {
                await UploadFileAsync(accessToken, file, cancellationToken);
            }

            return CreateResult(true, "Upload", $"Uploaded {files.Count} file(s) to Dropbox.");
        }
        catch (Exception exception)
        {
            return CreateResult(false, "Upload", $"Dropbox upload failed. {exception.Message}");
        }
    }

    public async Task<SyncDownloadResult> DownloadAsync(IReadOnlyList<SyncFileDescriptor> files, CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await EnsureAccessTokenAsync(cancellationToken);
            var payloads = new List<SyncFilePayload>(files.Count);

            foreach (var file in files)
            {
                var content = await DownloadFileAsync(accessToken, file, cancellationToken);
                payloads.Add(new SyncFilePayload
                {
                    Descriptor = file,
                    Content = content
                });
            }

            return new SyncDownloadResult
            {
                IsSuccess = true,
                Operation = "Download",
                Message = $"Downloaded {payloads.Count} file(s) from Dropbox.",
                TimestampUtc = DateTimeOffset.UtcNow,
                Files = payloads
            };
        }
        catch (Exception exception)
        {
            return new SyncDownloadResult
            {
                IsSuccess = false,
                Operation = "Download",
                Message = $"Dropbox download failed. {exception.Message}",
                TimestampUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private async Task<DropboxAuthState?> LoadAuthStateAsync()
    {
        if (_authState is not null)
        {
            return _authState;
        }

        _authState = await _localStorageService.LoadAsync<DropboxAuthState>(AuthStateKey);
        return _authState;
    }

    private async Task PersistAuthStateAsync()
    {
        if (_authState is null)
        {
            return;
        }

        await _localStorageService.SaveAsync(AuthStateKey, _authState);
    }

    private async Task<string> EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        var authState = await LoadAuthStateAsync();
        if (authState is null ||
            string.IsNullOrWhiteSpace(authState.PublicClientKey) ||
            string.IsNullOrWhiteSpace(authState.RefreshToken))
        {
            throw new InvalidOperationException("Dropbox is not connected.");
        }

        if (!string.IsNullOrWhiteSpace(authState.AccessToken) &&
            authState.AccessTokenExpiresUtc > DateTimeOffset.UtcNow)
        {
            return authState.AccessToken;
        }

        var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", authState.RefreshToken),
            new KeyValuePair<string, string>("client_id", authState.PublicClientKey)
        ]);

        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxTokenUrl)
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var token = await ReadRequiredJsonAsync<DropboxTokenResponse>(response, cancellationToken, "Dropbox token refresh failed.");
        if (token is null)
        {
            throw new InvalidOperationException("Dropbox token refresh returned an empty response.");
        }

        authState.AccessToken = token.AccessToken;
        authState.AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 60));
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            authState.RefreshToken = token.RefreshToken;
        }

        _authState = authState;
        await PersistAuthStateAsync();
        return authState.AccessToken;
    }

    private async Task<DropboxTokenResponse?> ExchangeAuthorizationCodeAsync(
        string publicClientKey,
        DropboxAuthorizationResponse authorization,
        CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", authorization.Code),
            new KeyValuePair<string, string>("client_id", publicClientKey),
            new KeyValuePair<string, string>("code_verifier", authorization.CodeVerifier),
            new KeyValuePair<string, string>("redirect_uri", authorization.RedirectUri)
        ]);

        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxTokenUrl)
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadRequiredJsonAsync<DropboxTokenResponse>(response, cancellationToken, "Dropbox token exchange failed.");
    }

    private async Task<DropboxCurrentAccountResponse?> GetCurrentAccountAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxCurrentAccountUrl);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadRequiredJsonAsync<DropboxCurrentAccountResponse>(response, cancellationToken, "Failed to load the Dropbox account profile.");
    }

    private async Task EnsureRemoteFolderAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxCreateFolderUrl)
        {
            Content = JsonContent.Create(new { path = "/BudgetAdvisor", autorename = false })
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Contains("conflict", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"Failed to prepare the Dropbox sync folder. {ExtractErrorMessage(body)}");
    }

    private async Task UploadFileAsync(string accessToken, SyncFilePayload file, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(file.Content));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxUploadUrl)
        {
            Content = content
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(
            "Dropbox-API-Arg",
            JsonSerializer.Serialize(new
            {
                path = file.Descriptor.RemotePath,
                mode = "overwrite",
                autorename = false,
                mute = true,
                strict_conflict = false
            }, JsonOptions));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Failed to upload '{file.Descriptor.DisplayName}'. {ExtractErrorMessage(body)}");
    }

    private async Task<string> DownloadFileAsync(string accessToken, SyncFileDescriptor file, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxDownloadUrl)
        {
            Content = new ByteArrayContent([])
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(
            "Dropbox-API-Arg",
            JsonSerializer.Serialize(new { path = file.RemotePath }, JsonOptions));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to download '{file.DisplayName}'. {ExtractErrorMessage(body)}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<T?> ReadRequiredJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        string defaultMessage)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"{defaultMessage} {ExtractErrorMessage(body)}");
    }

    private static string ExtractErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "No additional details were returned by Dropbox.";
        }

        try
        {
            var error = JsonSerializer.Deserialize<DropboxErrorResponse>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(error?.ErrorSummary))
            {
                return error.ErrorSummary.Trim();
            }
        }
        catch
        {
        }

        return body.Trim();
    }

    private static SyncResult CreateResult(bool isSuccess, string operation, string message) =>
        new()
        {
            IsSuccess = isSuccess,
            Operation = operation,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow
        };

    private sealed class DropboxAuthState
    {
        public string PublicClientKey { get; set; } = string.Empty;

        public string AccessToken { get; set; } = string.Empty;

        public DateTimeOffset AccessTokenExpiresUtc { get; set; }

        public string RefreshToken { get; set; } = string.Empty;

        public string AccountDisplayName { get; set; } = string.Empty;

        public string AccountEmail { get; set; } = string.Empty;
    }

    private sealed class DropboxAuthorizationResponse
    {
        public string Code { get; set; } = string.Empty;

        public string CodeVerifier { get; set; } = string.Empty;

        public string RedirectUri { get; set; } = string.Empty;
    }

    private sealed class DropboxTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
    }

    private sealed class DropboxCurrentAccountResponse
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("name")]
        public DropboxAccountName? Name { get; set; }
    }

    private sealed class DropboxAccountName
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }

    private sealed class DropboxErrorResponse
    {
        [JsonPropertyName("error_summary")]
        public string? ErrorSummary { get; set; }
    }
}
