using System.ComponentModel;

namespace DropToWin.Ui;

/// <summary>Pestañas planas con subrayado de color; se ven bien en modo claro y oscuro.</summary>
sealed class NavBar : Control
{
    int selected;
    int hover = -1;
    Font? boldFont;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<string> Items { get; } = new();

    public event Action<int>? SelectedChanged;

    public NavBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Height = 42;
        Font = new Font("Segoe UI", 10f);
        Text = "Secciones";
        AccessibleName = "Secciones";
        AccessibleRole = AccessibleRole.PageTabList;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new NavAccessible(this);

    sealed class NavAccessible(NavBar owner) : ControlAccessibleObject(owner)
    {
        public override int GetChildCount() => owner.Items.Count;
        public override AccessibleObject? GetChild(int index) =>
            index >= 0 && index < owner.Items.Count ? new ItemAccessible(owner, this, index) : null;
    }

    sealed class ItemAccessible(NavBar owner, AccessibleObject parent, int index) : AccessibleObject
    {
        public override string Name => owner.Items[index];
        public override AccessibleRole Role => AccessibleRole.PageTab;
        public override AccessibleObject Parent => parent;
        public override AccessibleStates State =>
            AccessibleStates.Focusable | AccessibleStates.Selectable |
            (index == owner.SelectedIndex ? AccessibleStates.Selected : AccessibleStates.None);
        public override Rectangle Bounds => owner.RectangleToScreen(owner.ItemBounds(index));
        public override string DefaultAction => "Seleccionar";
        public override void DoDefaultAction() => owner.SelectedIndex = index;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value < 0 || value >= Items.Count || value == selected) return;
            selected = value;
            Invalidate();
            SelectedChanged?.Invoke(value);
        }
    }

    Font BoldFont => boldFont ??= new Font(Font, FontStyle.Bold);

    protected override void OnFontChanged(EventArgs e)
    {
        boldFont?.Dispose();
        boldFont = null;
        base.OnFontChanged(e);
    }

    Rectangle ItemBounds(int index)
    {
        int pad = LogicalToDeviceUnits(14);
        int x = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            int width = TextRenderer.MeasureText(Items[i], BoldFont).Width + pad * 2;
            if (i == index) return new Rectangle(x, 0, width, Height);
            x += width;
        }
        return Rectangle.Empty;
    }

    int HitTest(Point p)
    {
        for (int i = 0; i < Items.Count; i++)
            if (ItemBounds(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int line = Math.Max(1, LogicalToDeviceUnits(1));
        using (var sep = new SolidBrush(Color.FromArgb(Theme.Dark ? 60 : 40, ForeColor)))
            g.FillRectangle(sep, 0, Height - line, Width, line);

        for (int i = 0; i < Items.Count; i++)
        {
            var r = ItemBounds(i);
            bool isSelected = i == selected;
            var color = isSelected || i == hover ? ForeColor : Theme.Muted;
            TextRenderer.DrawText(g, Items[i], isSelected ? BoldFont : Font, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (isSelected)
            {
                int bar = LogicalToDeviceUnits(3);
                int inset = LogicalToDeviceUnits(10);
                using var accent = new SolidBrush(Theme.Accent);
                g.FillRectangle(accent, r.Left + inset, Height - bar, r.Width - inset * 2, bar);
            }
            if (isSelected && Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -4, -6));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int index = HitTest(e.Location);
        if (index >= 0) SelectedIndex = index;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitTest(e.Location);
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        if (index != hover)
        {
            hover = index;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = -1;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, selected - 1);
        else if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(Items.Count - 1, selected + 1);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) boldFont?.Dispose();
        base.Dispose(disposing);
    }
}
