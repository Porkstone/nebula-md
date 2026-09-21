using System.Drawing.Drawing2D;

namespace NebulaMd;

internal sealed class ThemeButton : Button
{
    internal bool Modern { get; set; }
    private bool hovered;
    private bool pressed;

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Modern) { base.OnPaint(e); return; }
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = RoundedRectangle(bounds, 9);
        using var fill = new SolidBrush(pressed ? FlatAppearance.MouseDownBackColor : hovered ? FlatAppearance.MouseOverBackColor : BackColor);
        using var border = new Pen(FlatAppearance.BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : SystemColors.GrayText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -5, -5), ForeColor, BackColor);
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
