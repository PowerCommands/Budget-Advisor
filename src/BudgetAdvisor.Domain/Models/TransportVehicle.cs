using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class TransportVehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public int ModelYear { get; set; }

    public int Mileage { get; set; }

    public VehicleFuelType FuelType { get; set; } = VehicleFuelType.Petrol;

    public TransportVehicleType VehicleType { get; set; } = TransportVehicleType.Car;

    public VehicleOwnershipType OwnershipType { get; set; } = VehicleOwnershipType.Private;

    public decimal? PurchasePrice { get; set; }

    public decimal? EstimatedSaleValue { get; set; }
}
