using System.Globalization;

namespace DropToWin;

static class Format
{
    static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static string Size(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = -1;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(value < 10 ? "0.0" : "0", Es) + " " + units[unit];
    }

    public static string Speed(double bytesPerSecond) => Size((long)bytesPerSecond) + "/s";

    public static string Duration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "";
        if (seconds < 60) return $"{Math.Ceiling(seconds)} s";
        if (seconds < 3600) return $"{Math.Ceiling(seconds / 60)} min";
        return $"{Math.Floor(seconds / 3600)} h {Math.Ceiling(seconds % 3600 / 60)} min";
    }

    public static string When(DateTime utc)
    {
        var local = utc.ToLocalTime();
        if (local.Date == DateTime.Today) return local.ToString("HH:mm", Es);
        if (local.Date == DateTime.Today.AddDays(-1)) return "Ayer " + local.ToString("HH:mm", Es);
        return local.ToString("d MMM yyyy, HH:mm", Es);
    }

    public static string TransportName(string transport) => transport switch
    {
        "wifi" => "Wi-Fi",
        "bluetooth" => "Bluetooth",
        _ => transport,
    };
}
