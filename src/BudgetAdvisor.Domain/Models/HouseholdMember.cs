namespace BudgetAdvisor.Domain.Models;

public sealed class HouseholdMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
}
