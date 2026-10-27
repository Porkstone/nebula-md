using NebulaMd;

internal static class ThemeChecks
{
    internal static void RunSettingsChecks(string folder, Action<bool, string> check)
    {
        var path = Path.Combine(folder, "theme-settings-tests.json");
        var settings = new ThemeSettings(path);
        check(settings.Load() == AppTheme.Nebula, "Missing settings select the new default theme");
        foreach (var content in new[] { "not json", "null", "[]", "{}", "{\"theme\":\"Unknown\"}", "{\"theme\":9}", "{\"theme\":\"42\"}" })
        {
            File.WriteAllText(path, content);
            check(settings.Load() == AppTheme.Nebula, "Invalid appearance settings fall back to Nebula");
        }
        check(settings.Save(AppTheme.Legacy) && new ThemeSettings(path).Load() == AppTheme.Legacy, "Legacy preference round-trips on disk");
        check(settings.Save(AppTheme.Nebula) && new ThemeSettings(path).Load() == AppTheme.Nebula, "Nebula preference round-trips on disk");
        var blocked = new ThemeSettings(Path.Combine(path, "cannot-write.json"));
        check(!blocked.Save(AppTheme.Legacy) && blocked.Load() == AppTheme.Nebula,
            "Inaccessible settings fail gracefully without blocking the application");
        File.WriteAllText(path, "{\"theme\":\"Legacy\"}");
        check(settings.Load() == AppTheme.Legacy && settings.LoadMode() == AppearanceMode.Dark,
            "Existing appearance settings migrate without changing the selected theme");
        foreach (var mode in Enum.GetValues<AppearanceMode>())
            check(settings.Save(AppTheme.Nebula, mode) && new ThemeSettings(path).LoadMode() == mode,
                $"{mode} mode persists independently of the application theme");
        foreach (var invalid in new[] { "null", "9", "\"Unknown\"", "\"42\"" })
        {
            File.WriteAllText(path, "{\"theme\":\"Legacy\",\"mode\":" + invalid + "}");
            check(settings.LoadMode() == AppearanceMode.Dark && settings.Load() == AppTheme.Legacy,
                "Invalid colour mode preserves the theme and falls back to Dark");
        }
        foreach (var palette in new[] { ThemePalette.Nebula, ThemePalette.NebulaLight })
        {
            check(Contrast(palette.Text, palette.Surface) >= 4.5 && Contrast(palette.Muted, palette.Canvas) >= 4.5,
                "Body and metadata text meet normal-text contrast in both modes");
            check(new[] { palette.Button, palette.Hover, palette.Pressed }.All(bg => Contrast(palette.Text, bg) >= 4.5),
                "Neutral button labels remain readable in rest, hover and pressed states");
            check(Contrast(palette.Secondary, palette.Selected) >= 4.5 && Contrast(palette.Secondary, palette.Canvas) >= 3,
                "Selection labels and focus rings retain contrast in both modes");
            check(new[] { palette.Accent, palette.AccentHover, palette.AccentPressed }
                    .All(bg => Contrast(palette.AccentText, bg) >= 4.5),
                "Orange action text retains contrast in every interactive state");
            check(Contrast(palette.ControlOutline, palette.Editor) >= 3 && Contrast(palette.PrimaryOutline, palette.Canvas) >= 3,
                "Input and primary-action boundaries meet non-text contrast");
        }
    }

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
