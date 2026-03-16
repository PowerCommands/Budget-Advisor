namespace BudgetAdvisor.Domain.Models;

public sealed class LocalizationResourceSet
{
    public string LanguageCode { get; set; } = "en";

    public string DisplayName { get; set; } = "English";

    public Dictionary<string, string> Resources { get; set; } = [];
}
