using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DropToWin;

static class KnownFolders
{
    static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out string path);

    public static string Downloads =>
        SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var path) == 0
            ? path
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}

static class Shell
{
    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { Log.Error("Shell", ex); }
    }

    public static void Reveal(string path)
    {
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Path.GetDirectoryName(path) is string dir && Directory.Exists(dir))
            Open(dir);
    }

    public static void OpenSettings(string page) => Open("ms-settings:" + page);

    /// <summary>¿El texto es exactamente un enlace web?</summary>
    public static bool TryGetWebLink(string text, out Uri uri)
    {
        var trimmed = text.Trim();
        if (!trimmed.Contains(' ') && !trimmed.Contains('\n') &&
            Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }
}

static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "DropToWin";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command &&
               command.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled, string? dataDir)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }
        var command = $"\"{Environment.ProcessPath}\" --minimized";
        if (dataDir is not null) command += $" --data \"{dataDir}\"";
        key.SetValue(ValueName, command);
    }
}

static class FirewallHelper
{
    public const string RuleName = "DropToWin (Wi-Fi)";

    /// <summary>
    /// Pide elevación (UAC) y deja una única regla de entrada para los puertos de red de DropToWin
    /// (Wi-Fi turbo y receptor de iPhone) en redes privadas y de dominio, quitando antes las reglas
    /// automáticas (incluidas las de bloqueo que crea Windows si se pulsó "Cancelar" en su aviso).
    /// </summary>
    /// <param name="ports">Lista separada por comas, p. ej. "47811,47812".</param>
    public static bool AllowWifiTurbo(string ports, out string? error)
    {
        error = null;
        var exe = (Environment.ProcessPath ?? "").Replace("'", "''");
        var script = $"""
            $ErrorActionPreference = 'Stop'
            Get-NetFirewallApplicationFilter -Program '{exe}' -ErrorAction SilentlyContinue | Get-NetFirewallRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
            Get-NetFirewallRule -DisplayName '{RuleName}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
            New-NetFirewallRule -DisplayName '{RuleName}' -Direction Inbound -Action Allow -Program '{exe}' -Protocol TCP -LocalPort {ports} -Profile Private,Domain | Out-Null
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null) return false;
            process.WaitForExit(60_000);
            if (process.ExitCode != 0) error = $"PowerShell terminó con el código {process.ExitCode}.";
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = "Se canceló la solicitud de permisos de administrador.";
            return false;
        }
        catch (Win32Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
