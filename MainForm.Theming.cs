using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NebulaMd;

internal sealed partial class MainForm
{
    private readonly ThemeSettings themeSettings;
    private AppTheme currentTheme;
    private AppearanceMode currentMode;
    private bool systemUsesLightTheme = ReadSystemLightTheme();
    private ThemePalette palette = ThemePalette.Nebula;
    private readonly List<Action> themeBindings = new();
    private readonly Dictionary<(string Family, float Size, FontStyle Style), Font> themeFonts = new();
    private readonly ContextMenuStrip appearanceMenu = new();
    private readonly ContextMenuStrip modeMenu = new();
    private event Action? AppearanceChanged;
    private bool IsModern => currentTheme == AppTheme.Nebula;
    private bool IsLight => IsModern && (currentMode == AppearanceMode.Light ||
        currentMode == AppearanceMode.System && systemUsesLightTheme);
    private DialogResult ShowMessage(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        using var dialog = new ThemeDialog(palette, IsModern && !IsLight, ThemeFont(10f), message, title, buttons, icon);
        void Refresh() => dialog.ApplyAppearance(palette, IsModern && !IsLight);
        AppearanceChanged += Refresh;
        try { return dialog.ShowDialog(this); }
        finally { AppearanceChanged -= Refresh; }
    }
    private Color PreviewBackground => darkPreview
        ? IsModern ? ThemePalette.Nebula.Preview : Color.FromArgb(32, 34, 31)
        : IsModern ? ThemePalette.NebulaLight.Preview : palette.Preview;

    private Font ThemeFont(float size, FontStyle style = FontStyle.Regular, string? family = null)
    {
        var key = (Family: family ?? palette.FontFamily, Size: size, Style: style);
        if (!themeFonts.TryGetValue(key, out var font))
            themeFonts[key] = font = new Font(key.Family, size, style);
        return font;
    }

    private void BindTheme(Action binding)
    {
        themeBindings.Add(binding);
        binding();
    }

    private Control BuildThemeSelector()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 168, Padding = new Padding(12), ColumnCount = 1, RowCount = 4
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 24, 40, 32, 40 }) footer.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var label = new Label { Text = "APPEARANCE", Dock = DockStyle.Fill, Margin = Padding.Empty };
        var selector = CreateToolbarButton("THEME", "Choose the application theme", (_, _) => { }, 180);
        selector.AccessibleName = "Application theme";
        selector.Dock = DockStyle.Fill;
        selector.Margin = Padding.Empty;
        selector.Height = 38;
        selector.Click += (_, _) => appearanceMenu.Show(selector, new Point(0, -appearanceMenu.PreferredSize.Height));
        var nebula = new ToolStripMenuItem("Nebula (default)") { CheckOnClick = false };
        var legacy = new ToolStripMenuItem("Legacy") { CheckOnClick = false };
        nebula.Click += (_, _) => ApplyTheme(AppTheme.Nebula);
        legacy.Click += (_, _) => ApplyTheme(AppTheme.Legacy);
        appearanceMenu.Items.AddRange([nebula, legacy]);
        appearanceMenu.ShowImageMargin = false;
        appearanceMenu.ShowCheckMargin = true;
        appearanceMenu.Renderer = new ToolStripProfessionalRenderer(new AppearanceMenuColors(() => palette));
        var modeLabel = new Label { Text = "COLOUR MODE", Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty };
        var modeSelector = CreateToolbarButton("MODE", "Choose Light, Dark or follow Windows", (_, _) => { }, 180);
        modeSelector.AccessibleName = "Application colour mode";
        modeSelector.Dock = DockStyle.Fill;
        modeSelector.Margin = Padding.Empty;
        modeSelector.Click += (_, _) => modeMenu.Show(modeSelector, new Point(0, -modeMenu.PreferredSize.Height));
        foreach (var mode in Enum.GetValues<AppearanceMode>())
        {
            var item = new ToolStripMenuItem(mode.ToString()) { Tag = mode };
            item.Click += (_, _) => ApplyMode(mode);
            modeMenu.Items.Add(item);
        }
        modeMenu.ShowImageMargin = false;
        modeMenu.ShowCheckMargin = true;
        modeMenu.Renderer = new ToolStripProfessionalRenderer(new AppearanceMenuColors(() => palette));
        footer.Controls.Add(label, 0, 0);
        footer.Controls.Add(selector, 0, 1);
        footer.Controls.Add(modeLabel, 0, 2);
        footer.Controls.Add(modeSelector, 0, 3);
        BindTheme(() =>
        {
            footer.BackColor = DeepInk;
            label.ForeColor = Muted;
            label.Font = ThemeFont(7.5f, FontStyle.Bold);
            modeLabel.ForeColor = Muted;
            modeLabel.Font = label.Font;
            selector.Text = $"{currentTheme}  ▾";
            modeSelector.Text = IsModern ? $"{currentMode}  ▾" : "Legacy colours";
            modeSelector.Enabled = IsModern;
            appearanceMenu.BackColor = palette.Button;
            appearanceMenu.ForeColor = Paper;
            appearanceMenu.Font = ThemeFont(9.5f);
            nebula.Checked = currentTheme == AppTheme.Nebula;
            legacy.Checked = currentTheme == AppTheme.Legacy;
            modeMenu.BackColor = palette.Button;
            modeMenu.ForeColor = Paper;
            modeMenu.Font = ThemeFont(9.5f);
            foreach (ToolStripMenuItem item in modeMenu.Items)
            {
                item.ForeColor = Paper;
                item.Checked = (AppearanceMode)item.Tag! == currentMode;
            }
        });
        return footer;
    }

    private void ApplyTheme(AppTheme theme, bool persist = true)
    {
        currentTheme = theme;
        palette = ThemePalette.For(theme, IsLight);
        darkPreview = IsModern && !IsLight;
        SuspendLayout();
        try
        {
            BackColor = Ink;
            ForeColor = Paper;
            Font = ThemeFont(9.5f);
            foreach (var binding in themeBindings) binding();
            foreach (var page in documentPages.Values) page.BackColor = Ink;
            UpdateModeButtons();
            ApplyTitleBarTheme();
            Invalidate(true);
        }
        finally { ResumeLayout(true); }
        AppearanceChanged?.Invoke();
        RenderPreview();
        if (persist && !themeSettings.Save(theme, currentMode)) statusLabel.Text = "THEME CHANGED · COULD NOT SAVE PREFERENCE";
    }

    private void ApplyMode(AppearanceMode mode)
    {
        currentMode = mode;
        // Read again in case Windows changed while an explicit mode was selected.
        systemUsesLightTheme = ReadSystemLightTheme();
        ApplyTheme(currentTheme);
    }

    private static bool ReadSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return false; }
    }

    private void SystemAppearanceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsHandleCreated || IsDisposed || Disposing) return;
        try { BeginInvoke(() => ApplySystemAppearance(ReadSystemLightTheme())); }
        catch (InvalidOperationException) { /* The window closed during notification. */ }
    }

    private void ApplySystemAppearance(bool light)
    {
        if (IsDisposed || Disposing || systemUsesLightTheme == light) return;
        systemUsesLightTheme = light;
        if (IsModern && currentMode == AppearanceMode.System) ApplyTheme(currentTheme, persist: false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
    {
        if (!IsHandleCreated) return;
        // Windows 10 20H1+ and Windows 11; older Windows ignores this attribute.
        var enabled = IsModern && !IsLight ? 1 : 0;
        _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        var caption = ColorTranslator.ToWin32(DeepInk);
        var text = ColorTranslator.ToWin32(Paper);
        _ = DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private sealed class AppearanceMenuColors(Func<ThemePalette> palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette().Button;
        public override Color MenuBorder => palette().Border;
        public override Color MenuItemBorder => palette().Secondary;
        public override Color MenuItemSelected => palette().Selected;
        public override Color CheckBackground => palette().Selected;
        public override Color CheckSelectedBackground => palette().Selected;
        public override Color ImageMarginGradientBegin => palette().Button;
        public override Color ImageMarginGradientMiddle => palette().Button;
        public override Color ImageMarginGradientEnd => palette().Button;
    }
}
