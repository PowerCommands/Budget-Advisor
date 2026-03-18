namespace BudgetAdvisor.Domain.Theming;

public static class AppThemeNames
{
    public const string Standard = "Standard";
    public const string Dark = "Dark";
    public const string DeepPurple = "DeepPurple";
    public const string BlueOcean = "BlueOcean";
    public const string GreenForest = "GreenForest";

    public static IReadOnlyList<string> Names { get; } =
    [
        Standard,
        Dark,
        DeepPurple,
        BlueOcean,
        GreenForest
    ];

    public static string Normalize(string? themeName)
    {
        var normalized = themeName?.Trim() ?? string.Empty;

        return normalized switch
        {
            var value when value.Equals(Standard, StringComparison.OrdinalIgnoreCase) => Standard,
            var value when value.Equals(Dark, StringComparison.OrdinalIgnoreCase) => Dark,
            var value when value.Equals(DeepPurple, StringComparison.OrdinalIgnoreCase) => DeepPurple,
            var value when value.Equals(BlueOcean, StringComparison.OrdinalIgnoreCase) => BlueOcean,
            var value when value.Equals(GreenForest, StringComparison.OrdinalIgnoreCase) => GreenForest,
            var value when value.Equals("Primary", StringComparison.OrdinalIgnoreCase) => Standard,
            var value when value.Equals("Secondary", StringComparison.OrdinalIgnoreCase) => Standard,
            var value when value.Equals("Tertiary", StringComparison.OrdinalIgnoreCase) => Standard,
            _ => Standard
        };
    }

    public static bool IsDark(string? themeName) =>
        string.Equals(Normalize(themeName), Dark, StringComparison.Ordinal);
}
