using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Web.WebView2.WinForms;
using NebulaMd;

// Exercise real WinForms event wiring and filesystem watchers without opening
// a visible window or requiring a third-party test runner.
internal static class Program
{
    private static readonly Type FormType = Assembly.Load("nebula-md").GetType("NebulaMd.MainForm", true)!;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        if (args.FirstOrDefault() == "--instance-host") return SingleInstanceChecks.RunHost(args);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        if (args.FirstOrDefault() == "--theme-preview")
        {
            // An isolated visual-review window: never shares the installed app's
            // single-instance identity or changes its saved appearance.
            var reviewFolder = Path.Combine(Path.GetTempPath(), "nebula-theme-review-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(reviewFolder);
            try { Application.Run(new MainForm(new ThemeSettings(Path.Combine(reviewFolder, "appearance.json")))); }
            finally { Directory.Delete(reviewFolder, recursive: true); }
            return 0;
        }
        var folder = Path.Combine(Path.GetTempPath(), "nebula-md-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            ThemeChecks.RunSettingsChecks(folder, Check);
            Task.Run(() => UpdateChecks.Run(Check)).GetAwaiter().GetResult();
            if (args.FirstOrDefault() == "--check-updates") return 0;
            if (args.FirstOrDefault() != "--check-instances") Run(folder);
            SingleInstanceChecks.Run(folder, Check);
            Console.WriteLine($"PASS: {checks} document workspace checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void Run(string folder)
    {
        var zebraPath = Path.Combine(folder, "Zebra.md");
        var alphaPath = Path.Combine(folder, "alpha.md");
        var betaPath = Path.Combine(folder, "Beta.mdown");
        File.WriteAllText(zebraPath, "# Zebra\n\nOriginal zebra\n");
        File.WriteAllText(alphaPath, "# Alpha\r\n\r\nOriginal alpha\r\n");
        File.WriteAllText(betaPath, "# Beta\n\n![Sample](sample.svg)\n");
        File.WriteAllText(Path.Combine(folder, "sample.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4\" height=\"4\"/>");

        var settings = new ThemeSettings(Path.Combine(folder, "appearance.json"));
        using var form = new MainForm(settings);
        form.Opacity = 0;
        form.ShowInTaskbar = false;
        form.Show();
        PumpUntil(() => Field<bool>(form, "webViewReady"));
        var editor = Field<TextBox>(form, "editor");
        var preview = Field<WebView2>(form, "preview");
        var tabs = Field<TabControl>(form, "documentTabs");
        var list = Field<ListBox>(form, "documentList");
        var split = Field<SplitContainer>(form, "split");
        _ = tabs.Handle;
        _ = list.Handle;
        _ = editor.Handle;
        var workspaceParent = split.Parent;
        CheckPreview(preview, "Welcome to nebula-md");
        Check(editor.Lines.Length > 10, "Welcome Markdown uses Windows line endings in the source editor");
        Check(Field<AppTheme>(form, "currentTheme") == AppTheme.Nebula && editor.BackColor == ThemePalette.Nebula.Editor,
            "First launch defaults to Nebula across application controls");
        Check(EvaluatePreview<string>(preview, "document.documentElement.dataset.appearance") == "nebula" &&
            EvaluatePreview<string>(preview, "getComputedStyle(document.documentElement).getPropertyValue('--page').trim()") == "#071419",
            "Default preview uses the matching Nebula dark palette");
        CaptureTheme(form, preview, "nebula");
        var previewNavigations = 0;
        preview.NavigationStarting += (_, _) => previewNavigations++;
        EvaluatePreview<bool>(preview, "window.nebulaPreviewMarker = true");
        Check(tabs.TabCount == 1, "Welcome has a tab");

        Call(form, "OpenFile", zebraPath);
        Check(tabs.TabCount == 1, "First file replaces untouched welcome");
        var zebra = Active(form);
        editor.Text = "# Zebra edited\r\n\r\nUnsaved zebra\r\n";
        editor.Select(4, 5);
        Call(form, "OpenFile", alphaPath);
        var alpha = Active(form);
        Call(form, "OpenFile", betaPath);
        var beta = Active(form);
        Check(tabs.TabCount == 3, "Multiple files retain separate tabs");
        CheckPreview(preview, "Beta");
        PumpUntil(() => EvaluatePreview<bool>(preview, "document.images[0]?.naturalWidth === 4"));
        var otherFolder = Path.Combine(folder, "other folder");
        Directory.CreateDirectory(otherFolder);
        File.WriteAllText(Path.Combine(otherFolder, "sample.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"7\" height=\"7\"/>");
        var otherPath = Path.Combine(otherFolder, "Other.md");
        File.WriteAllText(otherPath, "# Other folder\n\n![Sample](sample.svg)\n");
        Call(form, "OpenFile", otherPath);
        CheckPreview(preview, "Other folder");
        PumpUntil(() => EvaluatePreview<bool>(preview, "document.images[0]?.naturalWidth === 7"));
        Call(form, "CloseDocument", Active(form));
        CheckPreview(preview, "Beta");
        PumpUntil(() => EvaluatePreview<bool>(preview, "document.images[0]?.naturalWidth === 4"));
        Check(true, "Relative images resolve correctly across folders without reloading the preview page");
        Check(list.Items.Cast<object>().Select(item => Property<string>(item, "Name"))
            .SequenceEqual(new[] { "alpha.md", "Beta.mdown", "Zebra.md" }), "Sidebar sorts filenames ignoring case");
        Check(ReferenceEquals(tabs.TabPages[0].Tag, zebra), "Tabs retain opening order");

        list.SelectedItem = zebra;
        Check(ReferenceEquals(tabs.SelectedTab!.Tag, zebra) && ReferenceEquals(Active(form), zebra), "Sidebar activates matching tab");
        Check(editor.Text.Contains("Unsaved zebra") && Property<bool>(zebra, "IsDirty"), "Switching preserves unsaved text");
        Check(editor.SelectionStart == 4 && editor.SelectionLength == 5, "Switching restores selection");
        Check(ReferenceEquals(split.Parent, workspaceParent) && split.Visible, "Tab switches keep the editor and preview in a stable visible container");
        CheckPreview(preview, "Zebra edited");
        tabs.SelectedIndex = 1;
        Check(ReferenceEquals(list.SelectedItem, alpha) && editor.Text.Contains("Original alpha"), "Tab activates matching sidebar entry and text");
        CheckPreview(preview, "Alpha");
        Check(previewNavigations == 0 && EvaluatePreview<bool>(preview, "window.nebulaPreviewMarker === true"),
            "Switching tabs updates the existing preview page without navigation");
        Call(form, "TogglePreviewTheme");
        PumpUntil(() => EvaluatePreview<bool>(preview, "document.documentElement.className === 'paper'"));
        Call(form, "TogglePreviewTheme");
        PumpUntil(() => EvaluatePreview<bool>(preview, "document.documentElement.className === 'night'"));
        Check(previewNavigations == 0, "Theme changes also preserve the existing preview page");

        list.SelectedItem = zebra;
        editor.Select(4, 5);
        var preservedText = editor.Text;
        var appearanceMenu = Field<ContextMenuStrip>(form, "appearanceMenu");
        appearanceMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Legacy").PerformClick();
        PumpUntil(() => EvaluatePreview<string>(preview, "document.documentElement.dataset.appearance") == "legacy");
        Check(editor.BackColor == ThemePalette.Legacy.Editor && Field<AppTheme>(form, "currentTheme") == AppTheme.Legacy &&
            EvaluatePreview<string>(preview, "getComputedStyle(document.documentElement).getPropertyValue('--page').trim()") == "#f6f1e6",
            "Legacy selector restores olive controls and the paper preview");
        Check(settings.Load() == AppTheme.Legacy, "Choosing Legacy persists the preference");
        using (var nextLaunch = new MainForm(settings))
            Check(Field<AppTheme>(nextLaunch, "currentTheme") == AppTheme.Legacy, "Next launch restores the selected theme");
        CaptureTheme(form, preview, "legacy");
        Call(form, "TogglePreviewTheme");
        PumpUntil(() => EvaluatePreview<bool>(preview, "document.documentElement.className === 'night'"));
        appearanceMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Nebula (default)").PerformClick();
        PumpUntil(() => EvaluatePreview<string>(preview, "document.documentElement.dataset.appearance") == "nebula");
        Check(editor.BackColor == ThemePalette.Nebula.Editor && settings.Load() == AppTheme.Nebula,
            "Nebula can be reselected and persisted");
        Check(editor.Text == preservedText && editor.SelectionStart == 4 && editor.SelectionLength == 5 &&
            ReferenceEquals(Active(form), zebra) && Property<bool>(zebra, "IsDirty") && tabs.TabCount == 3,
            "Theme switches preserve active tab, selection, unsaved text, and every document");
        Check(previewNavigations == 0 && EvaluatePreview<bool>(preview, "window.nebulaPreviewMarker === true"),
            "Application theme switches do not reload or replace the preview");

        Call(form, "OpenFile", zebraPath.ToUpperInvariant());
        Check(tabs.TabCount == 3 && ReferenceEquals(Active(form), zebra), "Opening same path with different case selects existing tab");
        Check(editor.Text.Contains("Unsaved zebra"), "Reopening does not reload over edits");
        Check((bool)Call(form, "SaveDocument")!, "Save active document succeeds");
        Check(File.ReadAllText(zebraPath) == "# Zebra edited\n\nUnsaved zebra\n", "Save retains document LF endings");
        Check(File.ReadAllText(alphaPath).Contains("Original alpha"), "Save does not modify another document");
        Check(!Property<bool>(zebra, "IsDirty") && !tabs.SelectedTab!.Text.Contains('•'), "Save clears dirty indicator");

        list.SelectedItem = alpha;
        editor.AppendText("Alpha edit\r\n");
        Call(form, "SaveDocument");
        Check(File.ReadAllText(alphaPath).EndsWith("Alpha edit\r\n"), "Each document retains its own newline format");

        // Watchers must target their owning document, even after a tab switch.
        File.WriteAllText(betaPath, "# Beta changed externally\n");
        PumpUntil(() => Property<string>(beta, "Text").Contains("changed externally"));
        Check(ReferenceEquals(Active(form), alpha) && editor.Text.Contains("Alpha edit"), "Background reload leaves active document intact");
        list.SelectedItem = beta;
        Check(editor.Text.Contains("changed externally"), "Background reload appears when selected");
        editor.Text = "# Beta local unsaved\r\n";
        list.SelectedItem = alpha;
        File.WriteAllText(betaPath, "# Beta competing disk edit\n");
        PumpUntil(() => Property<bool>(beta, "ChangedOnDisk"));
        list.SelectedItem = beta;
        Check(editor.Text.Contains("local unsaved") && Property<bool>(beta, "IsDirty"), "External changes preserve inactive dirty document");
        Check(Field<Label>(form, "statusLabel").Text == "CHANGED ON DISK" && Field<Panel>(form, "externalChangeBanner").Visible, "Conflict status follows document");
        Call(form, "ReloadFromDisk", true);
        Check(editor.Text.Contains("competing disk edit") && !Property<bool>(beta, "IsDirty"), "Explicit reload replaces only active document");
        CheckPreview(preview, "Beta competing disk edit");

        File.Delete(betaPath);
        PumpUntil(() => Property<bool>(beta, "ChangedOnDisk"));
        File.WriteAllText(betaPath, "# Beta recreated\n");
        PumpUntil(() => Property<string>(beta, "Text").Contains("recreated"));
        Check(!Property<bool>(beta, "ChangedOnDisk"), "Recreated files resume synchronization");

        var modeType = FormType.GetNestedType("DisplayMode", BindingFlags.NonPublic)!;
        foreach (var mode in new[] { "Source", "Preview", "Split", "Preview", "Source", "Split" })
        {
            Call(form, "SetDisplayMode", Enum.Parse(modeType, mode));
            Check(split.Panel1Collapsed == (mode == "Preview") && split.Panel2Collapsed == (mode == "Source"), $"{mode} layout works with tabs");
        }
        form.ClientSize = new Size(1120, 620);
        form.PerformLayout();
        Check(split.Width >= 605 && list.Width > 0, "Sidebar leaves room for both panes at minimum size");
        CaptureTheme(form, preview, "nebula-minimum");
        appearanceMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Legacy").PerformClick();
        PumpUntil(() => EvaluatePreview<string>(preview, "document.documentElement.dataset.appearance") == "legacy");
        CaptureTheme(form, preview, "legacy-minimum");
        appearanceMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Nebula (default)").PerformClick();
        PumpUntil(() => EvaluatePreview<string>(preview, "document.documentElement.dataset.appearance") == "nebula");

        var drop = new DataObject(DataFormats.FileDrop, new[] { zebraPath, alphaPath, betaPath, Path.Combine(folder, "missing.md") });
        var dropped = (IEnumerable)FormType.GetMethod("GetDroppedMarkdownFiles", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { drop })!;
        Check(dropped.Cast<string>().Count() == 3, "Drop accepts every existing supported document");

        Call(form, "CloseDocument", alpha);
        Check(tabs.TabCount == 2 && ReferenceEquals(Active(form), beta), "Closing background tab retains active document");
        Call(form, "CloseDocument", beta);
        Check(tabs.TabCount == 1 && ReferenceEquals(Active(form), zebra) && !split.IsDisposed, "Closing active tab selects neighbor without disposing workspace");
        Call(form, "CloseDocument", zebra);
        Check(tabs.TabCount == 1 && Property<string?>(Active(form), "Path") is null, "Closing last tab creates welcome document");
        editor.Text = "Keep my untitled edits";
        Call(form, "OpenFile", alphaPath);
        Check(tabs.TabCount == 2 && list.Items.Cast<object>().Any(item => Property<string>(item, "Text") == "Keep my untitled edits"), "Opening preserves edited untitled document");

        Call(form, "ShowAvailableUpdate", new Version(9, 0, 0));
        var updateBanner = Field<Panel>(form, "updateBanner");
        var updateLabel = Field<Label>(form, "updateLabel");
        Check(updateBanner.Visible && updateLabel.Text.Contains("9.0.0") && updateLabel.Text.Contains("choco upgrade nebula-md -y"),
            "Update banner names the version and explains upgrading");
        Check(editor.Text == "Keep my untitled edits" || list.Items.Cast<object>().Any(item => Property<string>(item, "Text") == "Keep my untitled edits"),
            "Update alert preserves unsaved documents");
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        form.DrawToBitmap(bitmap, form.ClientRectangle);
        var screenshot = Environment.GetEnvironmentVariable("NEBULA_SMOKE_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(screenshot)) bitmap.Save(screenshot);
        updateBanner.Controls.OfType<Button>().Single(button => button.Text.Equals("DISMISS", StringComparison.OrdinalIgnoreCase)).PerformClick();
        Check(!updateBanner.Visible, "Update banner can be dismissed");
    }

    private static object Active(Form form) => Field<object>(form, "activeDocument");
    private static void CaptureTheme(Form form, WebView2 preview, string theme)
    {
        var folder = Environment.GetEnvironmentVariable("NEBULA_THEME_SCREENSHOTS");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(folder, theme + "-window.png"));
        using var output = File.Create(Path.Combine(folder, theme + "-preview.png"));
        var capture = preview.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, output);
        PumpUntil(() => capture.IsCompleted);
        capture.GetAwaiter().GetResult();
    }
    private static T Field<T>(object instance, string name) => (T)FormType.GetField(name, PrivateInstance)!.GetValue(instance)!;
    private static T Property<T>(object instance, string name) => (T)instance.GetType().GetProperty(name)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object[] args) => FormType.GetMethod(name, PrivateInstance)!.Invoke(instance, args);
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        checks++;
        Console.WriteLine("PASS " + description);
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(15))
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
        if (!condition()) throw new TimeoutException("The expected asynchronous update did not arrive.");
    }

    private static void CheckPreview(WebView2 preview, string expectedHeading)
    {
        string? heading = null;
        PumpUntil(() =>
        {
            heading = EvaluatePreview<string>(preview, "document.querySelector('h1')?.textContent");
            return heading == expectedHeading;
        });
        Check(heading == expectedHeading, $"Live preview renders {expectedHeading}");
    }

    private static T? EvaluatePreview<T>(WebView2 preview, string script)
    {
        var query = preview.ExecuteScriptAsync(script);
        PumpUntil(() => query.IsCompleted);
        return System.Text.Json.JsonSerializer.Deserialize<T>(query.GetAwaiter().GetResult());
    }
}
