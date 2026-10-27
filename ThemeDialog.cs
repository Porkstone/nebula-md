using System.Runtime.InteropServices;

namespace NebulaMd;

// App-owned messages use the same palette as the workspace. Windows file and
// print pickers retain their native shell integration and accessibility.
internal sealed class ThemeDialog : Form
{
    private ThemePalette palette;
    private bool dark;
    private readonly Bitmap symbolBitmap;

    internal ThemeDialog(ThemePalette palette, bool dark, Font font, string message, string title,
        MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        this.palette = palette;
        this.dark = dark;
        Text = title;
        Font = font;
        BackColor = palette.Surface;
        ForeColor = palette.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 200);
        Padding = new Padding(24);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var symbol = icon switch
        {
            MessageBoxIcon.Error => SystemIcons.Error,
            MessageBoxIcon.Warning => SystemIcons.Warning,
            MessageBoxIcon.Question => SystemIcons.Question,
            _ => SystemIcons.Information
        };
        symbolBitmap = symbol.ToBitmap();
        var picture = new PictureBox { Image = symbolBitmap, Size = new Size(32, 32), Margin = new Padding(0, 4, 16, 0) };
        var text = new Label { Text = message, Dock = DockStyle.Fill, AutoSize = false, Margin = new Padding(0, 0, 0, 16), UseMnemonic = false };
        // Long file names and errors wrap and can be scrolled instead of clipped.
        var measured = TextRenderer.MeasureText(message, font, new Size(440, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        ClientSize = new Size(560, Math.Clamp(measured.Height + 120, 200, 480));
        var messagePanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        text.Dock = DockStyle.Top;
        text.Height = measured.Height + 16;
        messagePanel.Controls.Add(text);
        layout.Controls.Add(picture, 0, 0);
        layout.Controls.Add(messagePanel, 1, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        layout.SetColumnSpan(actions, 2);
        layout.Controls.Add(actions, 0, 1);
        Controls.Add(layout);

        var choices = buttons switch
        {
            MessageBoxButtons.YesNoCancel => new[] { ("Cancel", DialogResult.Cancel), ("Don't save", DialogResult.No), ("Save", DialogResult.Yes) },
            MessageBoxButtons.YesNo => new[] { ("Cancel", DialogResult.No), ("Reload", DialogResult.Yes) },
            _ => new[] { ("OK", DialogResult.OK) }
        };
        foreach (var (label, result) in choices)
        {
            var primary = result is DialogResult.Yes or DialogResult.OK;
            var button = new ThemeButton
            {
                Text = label, DialogResult = result, Width = 112, Height = 40, Margin = new Padding(8, 0, 0, 0),
                FlatStyle = FlatStyle.Flat, Modern = true, FocusColor = palette.Secondary,
                ForeColor = primary ? palette.AccentText : palette.Text,
                BackColor = primary ? palette.Accent : palette.Button,
                AccessibleName = label, UseVisualStyleBackColor = false, Tag = primary
            };
            button.FlatAppearance.BorderColor = primary ? palette.PrimaryOutline : palette.ControlOutline;
            button.FlatAppearance.MouseOverBackColor = primary ? palette.AccentHover : palette.Hover;
            button.FlatAppearance.MouseDownBackColor = primary ? palette.AccentPressed : palette.Pressed;
            actions.Controls.Add(button);
            if (primary) AcceptButton = button;
            else if (result is DialogResult.Cancel or DialogResult.No) CancelButton ??= button;
        }
        // Reload is destructive: start on Cancel and make Enter safe by default.
        if (buttons == MessageBoxButtons.YesNo) AcceptButton = CancelButton;
        Shown += (_, _) => (CancelButton as Control ?? AcceptButton as Control)?.Focus();
    }

    internal void ApplyAppearance(ThemePalette nextPalette, bool nextDark)
    {
        palette = nextPalette;
        dark = nextDark;
        BackColor = palette.Surface;
        ForeColor = palette.Text;
        void RefreshButtons(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is ThemeButton button)
                {
                    var primary = button.Tag is true;
                    button.FocusColor = palette.Secondary;
                    button.ForeColor = primary ? palette.AccentText : palette.Text;
                    button.BackColor = primary ? palette.Accent : palette.Button;
                    button.FlatAppearance.BorderColor = primary ? palette.PrimaryOutline : palette.ControlOutline;
                    button.FlatAppearance.MouseOverBackColor = primary ? palette.AccentHover : palette.Hover;
                    button.FlatAppearance.MouseDownBackColor = primary ? palette.AccentPressed : palette.Pressed;
                }
                RefreshButtons(child);
            }
        }
        RefreshButtons(this);
        if (IsHandleCreated) ApplyTitleBar();
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBar();
    }

    private void ApplyTitleBar()
    {
        var enabled = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        var caption = ColorTranslator.ToWin32(palette.Canvas);
        var text = ColorTranslator.ToWin32(palette.Text);
        _ = DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) symbolBitmap.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
