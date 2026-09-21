using System.Runtime.InteropServices;

namespace NebulaMd;

internal sealed partial class MainForm
{
    private readonly ThemeSettings themeSettings;
    private AppTheme currentTheme;
    private ThemePalette palette = ThemePalette.Nebula;
    private readonly List<Action> themeBindings = new();
    private readonly Dictionary<(string Family, float Size, FontStyle Style), Font> themeFonts = new();
    private readonly ContextMenuStrip appearanceMenu = new();
    private bool IsModern => currentTheme == AppTheme.Nebula;
    private Color PreviewBackground => darkPreview
        ? IsModern ? palette.Preview : Color.FromArgb(32, 34, 31)
        : IsModern ? ColorTranslator.FromHtml("#f4f8f9") : palette.Preview;

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
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(12, 12, 12, 16) };
        var label = new Label { Text = "APPEARANCE", Dock = DockStyle.Top, Height = 23 };
        var selector = CreateToolbarButton("THEME", "Choose the application theme", (_, _) => { }, 180);
        selector.AccessibleName = "Application theme";
        selector.Dock = DockStyle.Bottom;
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
        footer.Controls.Add(selector);
        footer.Controls.Add(label);
        BindTheme(() =>
        {
            footer.BackColor = DeepInk;
            label.ForeColor = Muted;
            label.Font = ThemeFont(7.5f, FontStyle.Bold);
            selector.Text = $"{currentTheme}  ▾";
            appearanceMenu.BackColor = palette.Button;
            appearanceMenu.ForeColor = Paper;
            appearanceMenu.Font = ThemeFont(9.5f);
            nebula.Checked = currentTheme == AppTheme.Nebula;
            legacy.Checked = currentTheme == AppTheme.Legacy;
        });
        return footer;
    }

    private void ApplyTheme(AppTheme theme, bool persist = true)
    {
        currentTheme = theme;
        palette = ThemePalette.For(theme);
        darkPreview = IsModern;
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
        RenderPreview();
        if (persist && !themeSettings.Save(theme)) statusLabel.Text = "THEME CHANGED · COULD NOT SAVE PREFERENCE";
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
        var enabled = IsModern ? 1 : 0;
        _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
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
