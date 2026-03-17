using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class AssetItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal PurchaseValue { get; set; }

    // Legacy compatibility for older persisted asset data.
    public decimal Amount { get; set; }

    public decimal EstimatedSaleValue { get; set; }

    public DateOnly AcquisitionDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public AssetSourceType SourceType { get; set; } = AssetSourceType.Manual;

    public Guid? HomeResidenceId { get; set; }

    public Guid? TransportVehicleId { get; set; }

    public bool IsSold { get; set; }

    public DateOnly? SaleDate { get; set; }

    public decimal? SaleAmount { get; set; }

    public Guid? SaleIncomeEntryId { get; set; }
}
