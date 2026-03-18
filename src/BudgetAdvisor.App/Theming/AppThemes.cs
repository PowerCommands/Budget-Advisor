using BudgetAdvisor.Domain.Theming;
using MudBlazor;

namespace BudgetAdvisor.App.Theming;

public static class AppThemes
{
    private static readonly Typography SharedCustomTypography = new()
    {
        Default = new DefaultTypography
        {
            FontFamily = ["Inter", "Roboto", "Segoe UI", "Arial", "sans-serif"]
        },
        H1 = new H1Typography
        {
            FontFamily = ["Montserrat", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "700",
            FontSize = "3rem",
            LineHeight = "1.15",
            LetterSpacing = "-0.02em"
        },
        H2 = new H2Typography
        {
            FontFamily = ["Montserrat", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "700",
            FontSize = "2.375rem",
            LineHeight = "1.2",
            LetterSpacing = "-0.015em"
        },
        H3 = new H3Typography
        {
            FontFamily = ["Montserrat", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "600",
            FontSize = "1.875rem",
            LineHeight = "1.25",
            LetterSpacing = "-0.01em"
        },
        H4 = new H4Typography
        {
            FontFamily = ["Montserrat", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "600",
            FontSize = "1.5rem",
            LineHeight = "1.3"
        },
        H5 = new H5Typography
        {
            FontFamily = ["Montserrat", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "500",
            FontSize = "1.25rem",
            LineHeight = "1.35"
        },
        H6 = new H6Typography
        {
            FontFamily = ["Montserrat", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "500",
            FontSize = "1.05rem",
            LineHeight = "1.4"
        },
        Subtitle1 = new Subtitle1Typography
        {
            FontFamily = ["Inter", "Roboto", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "500",
            FontSize = "1rem",
            LineHeight = "1.5"
        },
        Subtitle2 = new Subtitle2Typography
        {
            FontFamily = ["Inter", "Roboto", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "500",
            FontSize = "0.875rem",
            LineHeight = "1.45"
        },
        Body1 = new Body1Typography
        {
            FontFamily = ["Inter", "Roboto", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "400",
            FontSize = "1rem",
            LineHeight = "1.6"
        },
        Body2 = new Body2Typography
        {
            FontFamily = ["Inter", "Roboto", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "400",
            FontSize = "0.925rem",
            LineHeight = "1.55"
        },
        Button = new ButtonTypography
        {
            FontFamily = ["Inter", "Roboto", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "500",
            FontSize = "0.9375rem",
            LineHeight = "1.4",
            LetterSpacing = "0.01em",
            TextTransform = "none"
        },
        Caption = new CaptionTypography
        {
            FontFamily = ["Inter", "Roboto", "Segoe UI", "Arial", "sans-serif"],
            FontWeight = "400",
            FontSize = "0.75rem",
            LineHeight = "1.4"
        }
    };

    private static readonly MudTheme StandardTheme = new();

    private static readonly MudTheme DarkTheme = new();

    private static readonly MudTheme DeepPurpleTheme = CreateCustomTheme(
        primary: "#673AB7",
        primaryContrastText: "#FFFFFF",
        secondary: "#9575CD",
        tertiary: "#B39DDB",
        info: "#7E57C2",
        success: "#66BB6A",
        warning: "#FFA726",
        error: "#EF5350",
        background: "#F4F1FA",
        surface: "#FFFFFF",
        appbarBackground: "#5E35B1",
        drawerBackground: "#FFFFFF",
        drawerText: "#2D1B46",
        textPrimary: "#2D1B46",
        textSecondary: "#6B5A85");

    private static readonly MudTheme BlueOceanTheme = CreateCustomTheme(
        primary: "#1565C0",
        primaryContrastText: "#FFFFFF",
        secondary: "#42A5F5",
        tertiary: "#80D8FF",
        info: "#29B6F6",
        success: "#66BB6A",
        warning: "#FFB300",
        error: "#EF5350",
        background: "#EEF6FB",
        surface: "#FFFFFF",
        appbarBackground: "#0D47A1",
        drawerBackground: "#FFFFFF",
        drawerText: "#12324A",
        textPrimary: "#12324A",
        textSecondary: "#52738A");

    private static readonly MudTheme GreenForestTheme = CreateCustomTheme(
        primary: "#2E7D32",
        primaryContrastText: "#FFFFFF",
        secondary: "#66BB6A",
        tertiary: "#A5D6A7",
        info: "#26A69A",
        success: "#43A047",
        warning: "#FFB300",
        error: "#E53935",
        background: "#F1F8F2",
        surface: "#FFFFFF",
        appbarBackground: "#1B5E20",
        drawerBackground: "#FFFFFF",
        drawerText: "#1F3A24",
        textPrimary: "#1F3A24",
        textSecondary: "#5F7A64");

    public static MudTheme GetTheme(string? themeName) =>
        AppThemeNames.Normalize(themeName) switch
        {
            AppThemeNames.Dark => DarkTheme,
            AppThemeNames.DeepPurple => DeepPurpleTheme,
            AppThemeNames.BlueOcean => BlueOceanTheme,
            AppThemeNames.GreenForest => GreenForestTheme,
            _ => StandardTheme
        };

    private static MudTheme CreateCustomTheme(
        string primary,
        string primaryContrastText,
        string secondary,
        string tertiary,
        string info,
        string success,
        string warning,
        string error,
        string background,
        string surface,
        string appbarBackground,
        string drawerBackground,
        string drawerText,
        string textPrimary,
        string textSecondary) => new()
        {
            PaletteLight = new PaletteLight
            {
                Primary = primary,
                PrimaryContrastText = primaryContrastText,
                Secondary = secondary,
                Tertiary = tertiary,
                Info = info,
                Success = success,
                Warning = warning,
                Error = error,
                Background = background,
                Surface = surface,
                AppbarBackground = appbarBackground,
                DrawerBackground = drawerBackground,
                DrawerText = drawerText,
                TextPrimary = textPrimary,
                TextSecondary = textSecondary
            },
            Typography = SharedCustomTypography
        };
}
