using MudBlazor;

namespace LifeOS.Web.Theming;

/// <summary>
/// Calm, warm look: cream paper background, sage side panel, deep indigo as the one strong accent,
/// serif headings. System fonts only (no external font requests, works offline).
/// The same colors are mirrored as CSS variables in app.css so the page never flashes the wrong palette
/// before the Blazor circuit starts.
/// </summary>
public static class LifeOsTheme
{
    public static readonly string[] Sans =
        ["system-ui", "-apple-system", "Segoe UI", "Roboto", "Helvetica Neue", "Arial", "sans-serif"];

    public static readonly string[] Serif =
        ["Iowan Old Style", "Palatino Linotype", "Palatino", "Book Antiqua", "Georgia", "serif"];

    public static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2F4A9E",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#5E7A68",
            Info = "#4F7FA8",
            Success = "#5F8F6B",
            Warning = "#C98A3F",
            Error = "#B4533F",
            Background = "#F6F2E9",
            BackgroundGray = "#EFEADD",
            Surface = "#FFFDF8",
            AppbarBackground = "#F6F2E9",
            AppbarText = "#26303A",
            DrawerBackground = "#E7EDE6",
            DrawerText = "#26303A",
            DrawerIcon = "#4A5B50",
            TextPrimary = "#26303A",
            TextSecondary = "#66707A",
            ActionDefault = "#5C6670",
            LinesDefault = "#E6DFCF",
            LinesInputs = "#CFC8B6",
            Divider = "#E6DFCF",
            DividerLight = "#EFE9DA",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#9DB4F2",
            PrimaryContrastText = "#13161B",
            Secondary = "#8FB09B",
            Info = "#7FB0D8",
            Success = "#86B791",
            Warning = "#E0A862",
            Error = "#E08A76",
            Background = "#15181D",
            BackgroundGray = "#1A1E24",
            Surface = "#1E2229",
            AppbarBackground = "#15181D",
            AppbarText = "#ECE9E1",
            DrawerBackground = "#1A211E",
            DrawerText = "#ECE9E1",
            DrawerIcon = "#9DB0A5",
            TextPrimary = "#ECE9E1",
            TextSecondary = "#A8B0B8",
            ActionDefault = "#B6BDC4",
            LinesDefault = "#2B3139",
            LinesInputs = "#3A424B",
            Divider = "#2B3139",
            DividerLight = "#262B33",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "16px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Sans },
            H1 = new H1Typography { FontFamily = Serif, FontWeight = "600" },
            H2 = new H2Typography { FontFamily = Serif, FontWeight = "600" },
            H3 = new H3Typography { FontFamily = Serif, FontWeight = "600" },
            H4 = new H4Typography { FontFamily = Serif, FontWeight = "600" },
            H5 = new H5Typography { FontFamily = Serif, FontWeight = "600" },
            H6 = new H6Typography { FontFamily = Serif, FontWeight = "600" },
            Button = new ButtonTypography { FontFamily = Sans, FontWeight = "600", TextTransform = "none" },
        },
    };
}
