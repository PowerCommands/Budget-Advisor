using BudgetAdvisor.Domain.Enums;

namespace BudgetAdvisor.Domain.Models;

public sealed class SubcategoryDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Key { get; set; } = string.Empty;

    public SubcategoryMainCategory MainCategory { get; set; }

    public string SwedishName { get; set; } = string.Empty;

    public string EnglishName { get; set; } = string.Empty;
}
