using System.Text;

namespace DropToWin.Transfers;

static class FileNames
{
    static readonly HashSet<char> Invalid = new(Path.GetInvalidFileNameChars());

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>Convierte un nombre recibido del teléfono en un nombre de archivo seguro para Windows.</summary>
    public static string Sanitize(string? raw)
    {
        var name = raw ?? "";
        int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        if (slash >= 0) name = name[(slash + 1)..];

        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(c < 32 || c == 127 || Invalid.Contains(c) ? '_' : c);
        name = sb.ToString().Trim().TrimEnd('.', ' ');
        if (name.Length == 0) return "archivo";

        string stem = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        if (stem.Length == 0 || ext.Length > 16)
        {
            stem = name;
            ext = "";
        }
        if (Reserved.Contains(stem.TrimEnd()))
            stem = "_" + stem;

        int maxStem = 150 - ext.Length;
        if (stem.Length > maxStem)
        {
            if (char.IsHighSurrogate(stem[maxStem - 1])) maxStem--;
            stem = stem[..maxStem].TrimEnd('.', ' ');
        }
        if (stem.Length == 0) stem = "archivo";
        return stem + ext;
    }

    /// <summary>Mueve el archivo a la carpeta sin sobrescribir nada: "foto.jpg", "foto (1).jpg"…</summary>
    public static string MoveToUnique(string source, string folder, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        if (stem.Length == 0) { stem = name; ext = ""; }

        for (int i = 0; i < 100_000; i++)
        {
            var candidate = Path.Combine(folder, i == 0 ? name : $"{stem} ({i}){ext}");
            if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
            try
            {
                File.Move(source, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Otro archivo ocupó el nombre entre la comprobación y el movimiento.
            }
        }
        throw new IOException("No se encontró un nombre libre para " + name);
    }

    static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".bmp", ".tif", ".tiff", ".dng", ".svg", ".avif" };
    static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".mkv", ".avi", ".3gp", ".webm", ".m4v", ".wmv" };
    static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".flac", ".amr", ".wma" };
    static readonly HashSet<string> DocExt = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".odt", ".ods", ".odp", ".rtf", ".csv", ".md", ".epub" };

    /// <summary>Subcarpeta usada cuando está activado "Organizar por tipo".</summary>
    public static string Category(string name, string? mime)
    {
        var ext = Path.GetExtension(name);
        mime ??= "";
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || ImageExt.Contains(ext)) return "Imágenes";
        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || VideoExt.Contains(ext)) return "Videos";
        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) || AudioExt.Contains(ext)) return "Audio";
        if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || DocExt.Contains(ext)) return "Documentos";
        return "Otros";
    }
}
