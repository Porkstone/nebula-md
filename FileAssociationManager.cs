using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace NebulaMd;

internal static class FileAssociationManager
{
    private const string ClassesRootPath = @"Software\Classes";
    private const string ApplicationPath = @"Applications\nebula-md.exe";
    private const string ProgId = "nebula-md.Markdown";
    private static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown"];

    public static void Register(string executablePath)
    {
        var resolvedExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(resolvedExecutablePath)
            || !Path.GetExtension(resolvedExecutablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Expected the current nebula-md Windows executable.", resolvedExecutablePath);
        }

        var command = $"\"{resolvedExecutablePath}\" \"%1\"";

        using (var applicationKey = CreateClassesSubKey(ApplicationPath))
        {
            applicationKey.SetValue("FriendlyAppName", "nebula-md", RegistryValueKind.String);
            applicationKey.SetValue(
                "ApplicationDescription",
                "Preview and edit Markdown files in nebula-md.",
                RegistryValueKind.String);
        }

        using (var applicationCommandKey = CreateClassesSubKey($@"{ApplicationPath}\shell\open\command"))
        {
            applicationCommandKey.SetValue(string.Empty, command, RegistryValueKind.String);
        }

        using (var supportedTypesKey = CreateClassesSubKey($@"{ApplicationPath}\SupportedTypes"))
        {
            foreach (var extension in MarkdownExtensions)
            {
                supportedTypesKey.SetValue(extension, string.Empty, RegistryValueKind.String);
            }
        }

        using (var progIdKey = CreateClassesSubKey(ProgId))
        {
            progIdKey.SetValue(string.Empty, "Markdown document", RegistryValueKind.String);
            progIdKey.SetValue("FriendlyTypeName", "Markdown document", RegistryValueKind.String);
        }

        using (var iconKey = CreateClassesSubKey($@"{ProgId}\DefaultIcon"))
        {
            iconKey.SetValue(string.Empty, $"\"{resolvedExecutablePath}\",0", RegistryValueKind.String);
        }

        using (var progIdCommandKey = CreateClassesSubKey($@"{ProgId}\shell\open\command"))
        {
            progIdCommandKey.SetValue(string.Empty, command, RegistryValueKind.String);
        }

        foreach (var extension in MarkdownExtensions)
        {
            using var openWithKey = CreateClassesSubKey($@"{extension}\OpenWithProgids");
            openWithKey.SetValue(ProgId, string.Empty, RegistryValueKind.String);
        }

        RefreshShellAssociations();
    }

    public static void Unregister()
    {
        using (var classesRoot = Registry.CurrentUser.OpenSubKey(ClassesRootPath, writable: true))
        {
            classesRoot?.DeleteSubKeyTree(ApplicationPath, throwOnMissingSubKey: false);
            classesRoot?.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);

            foreach (var extension in MarkdownExtensions)
            {
                using var openWithKey = classesRoot?.OpenSubKey($@"{extension}\OpenWithProgids", writable: true);
                openWithKey?.DeleteValue(ProgId, throwOnMissingValue: false);
            }
        }

        RefreshShellAssociations();
    }

    private static RegistryKey CreateClassesSubKey(string relativePath)
    {
        return Registry.CurrentUser.CreateSubKey($@"{ClassesRootPath}\{relativePath}", writable: true)
            ?? throw new InvalidOperationException($"Unable to create the registry key for {relativePath}.");
    }

    private static void RefreshShellAssociations()
    {
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
