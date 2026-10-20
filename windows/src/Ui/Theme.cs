using System.Runtime.InteropServices;

namespace DropToWin.Ui;

static class Theme
{
    public static bool Dark => Application.IsDarkModeEnabled;
    public static Color Accent => Dark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(37, 99, 235);
    public static Color Ok => Dark ? Color.FromArgb(74, 222, 128) : Color.FromArgb(22, 163, 74);
    public static Color Warn => Dark ? Color.FromArgb(251, 191, 36) : Color.FromArgb(217, 119, 6);
    public static Color Bad => Dark ? Color.FromArgb(248, 113, 113) : Color.FromArgb(220, 38, 38);
    public static Color Muted => SystemColors.GrayText;

    public static Icon AppIcon { get; } = LoadIcon(new Size(32, 32));
    public static Icon TrayIcon { get; } = LoadIcon(SystemInformation.SmallIconSize);
    public static Icon LargeIcon { get; } = LoadIcon(new Size(64, 64));

    static Icon LoadIcon(Size size)
    {
        using var stream = typeof(Theme).Assembly.GetManifestResourceStream("DropToWin.app.ico")!;
        return new Icon(stream, size);
    }

    public static Font Title(float size = 14f) => new("Segoe UI Semibold", size);
}

/// <summary>Punto de color del estado (verde/ámbar/rojo).</summary>
sealed class StatusDot : Control
{
    Color color = SystemColors.GrayText;

    public StatusDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color DotColor
    {
        get => color;
        set { color = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>Iconos del sistema según la extensión (los mismos que muestra el Explorador).</summary>
static class ShellIcons
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    const uint ShgfiIcon = 0x100, ShgfiSmallIcon = 0x1, ShgfiUseFileAttributes = 0x10, FileAttributeNormal = 0x80;

    public static Icon? SmallIcon(string extension)
    {
        var info = new SHFILEINFO();
        var result = SHGetFileInfo("archivo" + extension, FileAttributeNormal, ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(), ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            using var temp = Icon.FromHandle(info.hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
