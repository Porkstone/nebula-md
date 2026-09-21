using System.Diagnostics;
using System.Text.Json;

namespace NebulaMd;

internal enum AppTheme { Nebula, Legacy }

internal sealed record ThemePalette(
    Color Surface, Color Canvas, Color Secondary, Color Accent, Color Text, Color Muted,
    Color Border, Color Editor, Color Selected, Color Button, Color Hover, Color Pressed,
    Color Preview, Color PreviewHeading, Color Warning, Color Update, string FontFamily)
{
    internal static ThemePalette For(AppTheme theme) => theme == AppTheme.Legacy ? Legacy : Nebula;
    private static Color Hex(string value) => ColorTranslator.FromHtml(value);
    internal static readonly ThemePalette Nebula = new(
        Hex("#08171c"), Hex("#040e12"), Hex("#75d9d2"), Hex("#d08748"), Hex("#dfebed"), Hex("#8ca6ae"),
        Hex("#1d3b44"), Hex("#071419"), Hex("#0e3035"), Hex("#0b2027"), Hex("#12353c"), Hex("#0b292f"),
        Hex("#071419"), Hex("#0b2027"), Hex("#38291d"), Hex("#103335"), "Segoe UI");
    internal static readonly ThemePalette Legacy = new(
        Color.FromArgb(28, 30, 27), Color.FromArgb(20, 21, 19), Color.FromArgb(171, 183, 157),
        Color.FromArgb(218, 166, 65), Color.FromArgb(246, 241, 230), Color.FromArgb(139, 143, 133),
        Color.FromArgb(61, 63, 57), Color.FromArgb(35, 37, 33), Color.FromArgb(65, 70, 59),
        Color.FromArgb(41, 43, 39), Color.FromArgb(56, 59, 53), Color.FromArgb(31, 33, 29),
        Color.FromArgb(246, 241, 230), Color.FromArgb(236, 231, 219), Color.FromArgb(92, 67, 29),
        Color.FromArgb(55, 66, 47), "Bahnschrift");
}

internal sealed class ThemeSettings
{
    private readonly string path;
    internal ThemeSettings(string? path = null) => this.path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nebula-md", "appearance.json");

    internal AppTheme Load()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("theme", out var value) && value.ValueKind == JsonValueKind.String &&
                Enum.TryParse<AppTheme>(value.GetString(), out var theme) && Enum.IsDefined(theme)) return theme;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Debug.WriteLine($"Cannot read appearance: {ex.Message}"); }
        return AppTheme.Nebula;
    }

    internal bool Save(AppTheme theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { theme = theme.ToString() }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Debug.WriteLine($"Cannot save appearance: {ex.Message}"); return false; }
    }
}
