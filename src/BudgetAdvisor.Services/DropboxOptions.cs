namespace BudgetAdvisor.Services;

public sealed class DropboxOptions
{
    public const string SectionName = "Dropbox";

    public string AppKey { get; set; } = string.Empty;
}
