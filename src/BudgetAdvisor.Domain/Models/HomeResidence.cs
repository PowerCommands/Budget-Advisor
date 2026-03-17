using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class HomeResidence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public HomeResidenceType ResidenceType { get; set; } = HomeResidenceType.RentalApartment;

    public int? PurchaseYear { get; set; }

    public decimal? PurchasePrice { get; set; }

    public decimal? CurrentMarketValue { get; set; }

    public Guid? AssetId { get; set; }
}
