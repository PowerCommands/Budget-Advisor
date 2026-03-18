using BudgetAdvisor.Domain.Models;
using Microsoft.JSInterop;

namespace BudgetAdvisor.Services;

public sealed class BrowserStorageUsageService : IBrowserStorageUsageService
{
    private readonly IJSRuntime _jsRuntime;

    public BrowserStorageUsageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<BrowserStorageUsageSnapshot> GetUsageAsync()
    {
        var usedBytes = await _jsRuntime.InvokeAsync<long>("budgetAdvisor.storage.getUsageBytes");

        return new BrowserStorageUsageSnapshot
        {
            TotalBytes = BrowserStorageUsageSnapshot.DefaultTotalBytes,
            UsedBytes = Math.Max(0L, usedBytes)
        };
    }
}
