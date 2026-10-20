using System.Collections;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using QRCoder;

namespace DropToWin.Ui;

/// <summary>Imágenes generadas en tiempo de ejecución: icono para iPhone y códigos QR.</summary>
static class IconArt
{
    /// <summary>Icono de 180×180 a sangre (iOS redondea las esquinas) para "Añadir a pantalla de inicio".</summary>
    public static byte[] TouchIconPng { get; } = RenderTouchIcon(180);

    static byte[] RenderTouchIcon(int s)
    {
        using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var background = new LinearGradientBrush(new Point(0, 0), new Point(0, s),
                Color.FromArgb(59, 130, 246), Color.FromArgb(29, 78, 216));
            g.FillRectangle(background, 0, 0, s, s);
            // Mismo símbolo que el icono de Windows, algo más pequeño por el redondeo de iOS.
            float k = s / 108f;
            using var pen = new Pen(Color.White, 6 * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLine(pen, 54 * k, 33 * k, 54 * k, 59 * k);
            g.DrawLines(pen, [new PointF(43 * k, 48.5f * k), new PointF(54 * k, 59.5f * k), new PointF(65 * k, 48.5f * k)]);
            g.DrawLines(pen, [new PointF(38 * k, 60 * k), new PointF(38 * k, 72 * k), new PointF(70 * k, 72 * k), new PointF(70 * k, 60 * k)]);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Código QR negro sobre blanco (con su margen de seguridad), nítido a cualquier escala.</summary>
    public static Bitmap Qr(string text, int pixelsPerModule)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        List<BitArray> matrix = data.ModuleMatrix;
        int n = matrix.Count;
        var bmp = new Bitmap(n * pixelsPerModule, n * pixelsPerModule, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (matrix[y][x])
                    g.FillRectangle(Brushes.Black, x * pixelsPerModule, y * pixelsPerModule, pixelsPerModule, pixelsPerModule);
        return bmp;
    }
}
