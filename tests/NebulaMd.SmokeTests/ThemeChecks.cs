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
    }
}
