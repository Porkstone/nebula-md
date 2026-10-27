using System.Drawing.Drawing2D;

namespace NebulaMd;

internal sealed class ThemeButton : Button
{
    internal bool Modern { get; set; }
    internal bool Selected { get; set; }
    internal Color FocusColor { get; set; }
    private bool hovered;
    private bool pressed;

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Modern) { base.OnPaint(e); return; }
        Control? background = Parent;
        while (background is not null && background.BackColor.A != 255) background = background.Parent;
        e.Graphics.Clear(background?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(4, 4, Width - 9, Height - 9);
        using var path = RoundedRectangle(bounds, 9);
        using var fill = new SolidBrush(pressed ? FlatAppearance.MouseDownBackColor : hovered ? FlatAppearance.MouseOverBackColor : BackColor);
        using var border = new Pen(FlatAppearance.BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : SystemColors.GrayText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Selected)
        {
            using var marker = new Pen(FocusColor, 2);
            e.Graphics.DrawLine(marker, bounds.Left + 9, bounds.Bottom - 3, bounds.Right - 9, bounds.Bottom - 3);
        }
        if (Focused && ShowFocusCues)
        {
            using var ring = RoundedRectangle(Rectangle.Inflate(bounds, 3, 3), 11);
            using var focus = new Pen(FocusColor, 2);
            e.Graphics.DrawPath(focus, ring);
        }
    }

    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// Native TabControl paints an unthemeable pale frame around owner-drawn tabs.
// Keep its selection, accessibility, and keyboard behavior but paint the strip.
internal sealed class ThemeTabControl : TabControl
{
    private Color stripColor;
    public override Color BackColor
    {
        get => stripColor;
        set { stripColor = value; Invalidate(); }
    }
    internal void SetModern(bool modern)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, modern);
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        for (var index = 0; index < TabCount; index++)
        {
            var state = index == SelectedIndex ? DrawItemState.Selected : DrawItemState.None;
            OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, GetTabRect(index), index, state));
        }
    }
}
