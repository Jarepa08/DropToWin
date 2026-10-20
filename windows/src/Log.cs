namespace DropToWin;

/// <summary>Registro sencillo en un archivo de texto dentro de la carpeta de datos.</summary>
static class Log
{
    static readonly object Gate = new();
    static string? path;

    public static void Init(string dataDir)
    {
        path = Path.Combine(dataDir, "registro.txt");
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 1_000_000)
                File.Move(path, Path.Combine(dataDir, "registro.anterior.txt"), overwrite: true);
        }
        catch (IOException) { }
    }

    public static void Info(string area, string message) => Write("INFO", area, message);

    public static void Error(string area, Exception? ex) => Write("ERROR", area, ex?.ToString() ?? "(desconocido)");

    public static void Error(string area, string message) => Write("ERROR", area, message);

    static void Write(string level, string area, string message)
    {
        if (path is null) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {area}: {message}{Environment.NewLine}";
        lock (Gate)
        {
            try { File.AppendAllText(path, line); }
            catch (IOException) { }
        }
    }
}
