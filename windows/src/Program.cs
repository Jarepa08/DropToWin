using DropToWin.Ui;

namespace DropToWin;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var options = AppOptions.Parse(args);

        using var instance = new Mutex(true, @"Local\DropToWin.Instance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            SingleInstance.SignalExisting();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("App", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Task", e.Exception); e.SetObserved(); };

        AppHost host;
        try
        {
            host = new AppHost(options);
        }
        catch (Exception ex)
        {
            Log.Error("Inicio", ex);
            MessageBox.Show($"No se pudo iniciar DropToWin:\n\n{ex.Message}", "DropToWin",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var context = new ApplicationContext();
        using var form = new MainForm(host);
        form.ExitRequested += () => context.ExitThread();
        SingleInstance.Listen(() => form.ShowFromTray());

        host.Start();
        if (!options.StartMinimized)
            form.Show();

        Application.Run(context);

        form.HideTrayIcon();
        host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
    }
}

/// <summary>Opciones de línea de comandos.</summary>
sealed class AppOptions
{
    /// <summary>Carpeta de datos alternativa (modo portátil o pruebas).</summary>
    public string? DataDir { get; private set; }
    /// <summary>Arranca oculto en la bandeja (usado al iniciar con Windows).</summary>
    public bool StartMinimized { get; private set; }
    /// <summary>Wi-Fi turbo solo en 127.0.0.1 (pruebas, evita el aviso del firewall).</summary>
    public bool Loopback { get; private set; }
    /// <summary>Habilita el transporte de prueba que simula Bluetooth en 127.0.0.1:47819.</summary>
    public bool TestTransport { get; private set; }

    public static AppOptions Parse(string[] args)
    {
        var o = new AppOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--data" when i + 1 < args.Length: o.DataDir = Path.GetFullPath(args[++i]); break;
                case "--minimized": o.StartMinimized = true; break;
                case "--loopback": o.Loopback = true; break;
                case "--test-transport": o.TestTransport = true; break;
            }
        }
        return o;
    }
}

/// <summary>Permite que una segunda ejecución traiga al frente la ventana de la primera.</summary>
static class SingleInstance
{
    const string EventName = @"Local\DropToWin.Show";

    public static void SignalExisting()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(EventName);
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    public static void Listen(Action onSignal)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var thread = new Thread(() =>
        {
            while (ev.WaitOne())
                onSignal();
        })
        { IsBackground = true, Name = "SingleInstance" };
        thread.Start();
    }
}
