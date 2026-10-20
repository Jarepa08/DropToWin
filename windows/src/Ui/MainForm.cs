using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using DropToWin.Server;
using DropToWin.Transfers;

namespace DropToWin.Ui;

sealed class MainForm : Form
{
    readonly AppHost host;
    readonly NotifyIcon tray;
    Action? balloonAction;
    bool exiting;
    bool trayHintShown;
    bool loadingSettings;

    // Encabezado
    readonly StatusDot statusDot = new();
    readonly Label statusTitle = new();
    readonly Label statusDetail = new();
    readonly Label pcLabel = new();
    readonly Button bluetoothSettingsButton = new();

    // Recibidos
    readonly ListView receivedList = new();
    readonly ImageList fileIcons = new();
    readonly Label receivedEmpty = new();

    // Dispositivos
    readonly ListView deviceList = new();
    readonly Button revokeButton = new();

    // Ajustes
    readonly TextBox folderBox = new();
    readonly TextBox pcNameBox = new();
    readonly CheckBox organizeCheck = new();
    readonly CheckBox wifiCheck = new();
    readonly CheckBox iphoneCheck = new();
    readonly Label iphoneStatusLabel = new();
    readonly CheckBox notifyCheck = new();
    readonly CheckBox linksCheck = new();
    readonly CheckBox startupCheck = new();
    readonly Label wifiStatusLabel = new();
    readonly LinkLabel networkLink = new();

    // Progreso
    readonly TableLayoutPanel progressPanel = new();
    readonly Label progressTitle = new();
    readonly Label progressDetail = new();
    readonly ProgressBar progressBar = new();
    readonly System.Windows.Forms.Timer progressTimer = new() { Interval = 1000 };
    DateTime lastProgressUtc;
    bool progressCompleted;

    // Aviso agrupado de archivos recibidos
    readonly System.Windows.Forms.Timer noticeTimer = new() { Interval = 2500 };
    string? noticeSender;
    string? noticeLastPath;
    int noticeCount;
    long noticeBytes;
    bool noticeSeveralFolders;

    public event Action? ExitRequested;

    public MainForm(AppHost host)
    {
        this.host = host;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "DropToWin";
        Icon = Theme.AppIcon;
        ClientSize = new Size(920, 600);
        MinimumSize = new Size(740, 500);
        StartPosition = FormStartPosition.CenterScreen;

        // Barra de navegación propia: el TabControl de WinForms no se ve bien en modo oscuro.
        var nav = new NavBar { Dock = DockStyle.Top, Items = { "Recibidos", "Dispositivos", "Ajustes" } };
        Control[] pages = [BuildReceivedPage(), BuildDevicesPage(), BuildSettingsPage()];
        var content = new Panel { Dock = DockStyle.Fill };
        foreach (var page in pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = page == pages[0];
            content.Controls.Add(page);
        }
        nav.SelectedChanged += index =>
        {
            for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
            if (index == 2) UpdateWifiStatus();
        };

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 14) };
        body.Controls.Add(content);
        body.Controls.Add(nav);
        Controls.Add(body);
        Controls.Add(BuildProgressPanel());
        Controls.Add(BuildHeader());

        tray = new NotifyIcon
        {
            Icon = Theme.TrayIcon,
            Text = "DropToWin",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        tray.DoubleClick += (_, _) => ShowFromTray();
        tray.BalloonTipClicked += (_, _) =>
        {
            var action = balloonAction;
            balloonAction = null;
            action?.Invoke();
        };

        ResumeLayout(false);
        PerformLayout();
        _ = Handle; // Permite recibir eventos aunque la ventana arranque oculta en la bandeja.

        host.ApprovalHandler = ApproveAsync;
        host.Bluetooth.StateChanged += () => Post(UpdateStatus);
        host.History.Added += item => Post(() => AddHistoryRow(item, atTop: true));
        host.TextReceived += item => Post(() => OnTextReceived(item));
        host.Transfers.Progress += p => Post(() => OnProgress(p));
        host.Transfers.BatchCompleted += b => Post(() => OnBatchCompleted(b));
        host.Config.DevicesChanged += () => Post(RefreshDevices);
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        progressTimer.Tick += (_, _) => CheckProgressIdle();
        noticeTimer.Tick += (_, _) => FlushNotice();

        LoadHistory();
        RefreshDevices();
        LoadSettings();
        UpdateStatus();
    }

    // ───────────────────────────── Construcción de la interfaz ─────────────────────────────

    Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(20, 16, 20, 10),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        statusDot.Size = new Size(14, 14);
        statusDot.Margin = new Padding(0, 9, 12, 0);
        statusTitle.AutoSize = true;
        statusTitle.Font = Theme.Title(15f);
        statusTitle.Margin = new Padding(0);
        statusDetail.AutoSize = true;
        statusDetail.ForeColor = Theme.Muted;
        statusDetail.MaximumSize = new Size(560, 0);
        statusDetail.Margin = new Padding(2, 2, 0, 0);
        pcLabel.AutoSize = true;
        pcLabel.ForeColor = Theme.Muted;
        pcLabel.Anchor = AnchorStyles.Right;
        pcLabel.Margin = new Padding(12, 6, 0, 0);
        bluetoothSettingsButton.Text = "Ajustes de Bluetooth…";
        bluetoothSettingsButton.AutoSize = true;
        bluetoothSettingsButton.Anchor = AnchorStyles.Right;
        bluetoothSettingsButton.Visible = false;
        bluetoothSettingsButton.Click += (_, _) => Shell.OpenSettings("bluetooth");

        header.Controls.Add(statusDot, 0, 0);
        header.SetRowSpan(statusDot, 2);
        header.Controls.Add(statusTitle, 1, 0);
        header.Controls.Add(statusDetail, 1, 1);
        header.Controls.Add(pcLabel, 2, 0);
        header.Controls.Add(bluetoothSettingsButton, 2, 1);
        return header;
    }

    Control BuildReceivedPage()
    {
        var page = new Panel { Padding = new Padding(0, 12, 0, 0) };

        fileIcons.ColorDepth = ColorDepth.Depth32Bit;
        fileIcons.ImageSize = SystemInformation.SmallIconSize;
        receivedList.Dock = DockStyle.Fill;
        receivedList.View = View.Details;
        receivedList.FullRowSelect = true;
        receivedList.HideSelection = false;
        receivedList.BorderStyle = BorderStyle.None;
        receivedList.SmallImageList = fileIcons;
        receivedList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        receivedList.Columns.Add("Nombre", LogicalToDeviceUnits(320));
        receivedList.Columns.Add("Tamaño", LogicalToDeviceUnits(80), HorizontalAlignment.Right);
        receivedList.Columns.Add("De", LogicalToDeviceUnits(150));
        receivedList.Columns.Add("Vía", LogicalToDeviceUnits(80));
        receivedList.Columns.Add("Fecha", LogicalToDeviceUnits(130));
        receivedList.DoubleClick += (_, _) => OpenSelected();
        receivedList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) OpenSelected();
            else if (e.KeyCode == Keys.Delete) RemoveSelected();
        };
        receivedList.ItemDrag += (_, _) => DragSelectedFiles();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => OpenSelected());
        menu.Items.Add("Mostrar en la carpeta", null, (_, _) => { if (SelectedItem() is { Path: string p }) Shell.Reveal(p); });
        menu.Items.Add("Copiar", null, (_, _) => CopySelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitar de la lista", null, (_, _) => RemoveSelected());
        receivedList.ContextMenuStrip = menu;

        receivedEmpty.Dock = DockStyle.Fill;
        receivedEmpty.TextAlign = ContentAlignment.MiddleCenter;
        receivedEmpty.ForeColor = Theme.Muted;
        receivedEmpty.Text =
            "Todavía no has recibido nada.\n\n" +
            "En el teléfono, toca Compartir › «Enviar al PC» desde cualquier app.\n" +
            "Si aún no lo has conectado, mira la pestaña Dispositivos.";

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(MakeButton("Abrir carpeta de recibidos", () => Shell.Open(host.Config.ReceiveFolder)));
        buttons.Controls.Add(MakeButton("Limpiar lista", ClearHistory));

        page.Controls.Add(receivedList);
        page.Controls.Add(receivedEmpty);
        page.Controls.Add(buttons);
        return page;
    }

    Control BuildDevicesPage()
    {
        var page = new Panel { Padding = new Padding(0, 12, 0, 0) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var group = new GroupBox
        {
            Text = "Conectar un teléfono",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 8, 12, 10),
        };
        var groupLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        groupLayout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Margin = new Padding(0, 4, 0, 6),
            Text =
                "Android, por Bluetooth:\n" +
                "    1.  Empareja el teléfono con este PC por Bluetooth (solo la primera vez).\n" +
                "    2.  Instala la app DropToWin en el teléfono (archivo DropToWin.apk).\n" +
                "    3.  En la app toca «Elegir PC» y selecciona este equipo. Aquí aparecerá una solicitud: pulsa Permitir.\n" +
                "iPhone, por Wi-Fi (iOS no permite enviar archivos por Bluetooth a un PC):\n" +
                "    Pulsa «Conectar un iPhone…» y escanea el código con la cámara del iPhone.",
        });
        var groupButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        groupButtons.Controls.Add(MakeButton("Abrir ajustes de Bluetooth", () => Shell.OpenSettings("bluetooth")));
        groupButtons.Controls.Add(MakeButton("Mostrar la app Android (APK)", RevealApk));
        groupButtons.Controls.Add(MakeButton("Conectar un iPhone…", ShowIphoneDialog));
        groupLayout.Controls.Add(groupButtons);
        group.Controls.Add(groupLayout);
        layout.Controls.Add(group);

        layout.Controls.Add(new Label { Text = "Teléfonos autorizados", AutoSize = true, Font = Theme.Title(10.5f), Margin = new Padding(0, 14, 0, 6) });

        deviceList.Dock = DockStyle.Fill;
        deviceList.View = View.Details;
        deviceList.FullRowSelect = true;
        deviceList.HideSelection = false;
        deviceList.MultiSelect = false;
        deviceList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        deviceList.Columns.Add("Nombre", LogicalToDeviceUnits(200));
        deviceList.Columns.Add("Tipo", LogicalToDeviceUnits(140));
        deviceList.Columns.Add("Bluetooth", LogicalToDeviceUnits(140));
        deviceList.Columns.Add("Autorizado", LogicalToDeviceUnits(150));
        deviceList.Columns.Add("Última conexión", LogicalToDeviceUnits(150));
        deviceList.SelectedIndexChanged += (_, _) => revokeButton.Enabled = deviceList.SelectedItems.Count > 0;
        layout.Controls.Add(deviceList);

        revokeButton.Text = "Quitar autorización";
        revokeButton.AutoSize = true;
        revokeButton.Enabled = false;
        revokeButton.Margin = new Padding(0, 8, 0, 0);
        revokeButton.Click += (_, _) => RevokeSelectedDevice();
        layout.Controls.Add(revokeButton);

        page.Controls.Add(layout);
        return page;
    }

    Control BuildSettingsPage()
    {
        var page = new Panel { Padding = new Padding(4, 16, 0, 0), AutoScroll = true };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            grid.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = Theme.Muted,
                Margin = new Padding(0, 7, 18, 10),
            });
            control.Margin = new Padding(0, 3, 0, 10);
            grid.Controls.Add(control);
        }

        static FlowLayoutPanel Stack(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            foreach (var c in controls)
            {
                c.Margin = new Padding(0, 0, 0, 4);
                panel.Controls.Add(c);
            }
            return panel;
        }

        folderBox.ReadOnly = true;
        folderBox.Width = 400;
        var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        folderRow.Controls.Add(folderBox);
        folderRow.Controls.Add(MakeButton("Cambiar…", ChangeFolder));
        folderRow.Controls.Add(MakeButton("Abrir", () => Shell.Open(host.Config.ReceiveFolder)));
        organizeCheck.Text = "Organizar en subcarpetas por tipo (Imágenes, Videos, Audio, Documentos, Otros)";
        organizeCheck.AutoSize = true;
        organizeCheck.CheckedChanged += (_, _) => SaveSetting(() => host.Config.OrganizeByType = organizeCheck.Checked);
        AddRow("Carpeta de recepción", Stack(folderRow, organizeCheck));

        pcNameBox.Width = 260;
        pcNameBox.MaxLength = 60;
        pcNameBox.Leave += (_, _) => SavePcName();
        pcNameBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { SavePcName(); e.SuppressKeyPress = true; } };
        AddRow("Nombre de este PC", Stack(pcNameBox, MutedLabel("Es el nombre que verás en el teléfono.")));

        wifiCheck.Text = "Acelerar con Wi-Fi cuando el teléfono esté en la misma red";
        wifiCheck.AutoSize = true;
        wifiCheck.CheckedChanged += (_, _) => SaveSetting(() => host.Config.WifiTurbo = wifiCheck.Checked, refreshStatus: true);
        wifiStatusLabel.AutoSize = true;
        wifiStatusLabel.ForeColor = Theme.Muted;
        wifiStatusLabel.MaximumSize = new Size(620, 0);
        networkLink.AutoSize = true;
        networkLink.MaximumSize = new Size(620, 0);
        networkLink.LinkColor = Theme.Accent;
        networkLink.ActiveLinkColor = Theme.Accent;
        networkLink.ForeColor = Theme.Muted;
        networkLink.LinkClicked += (_, _) => Shell.OpenSettings("network-status");
        var explain = MutedLabel(
            "Bluetooth siempre establece la conexión y la autorización. Si ambos equipos comparten red, el contenido " +
            "pesado viaja cifrado por Wi-Fi (decenas de MB/s en lugar de ~0,2 MB/s).");
        explain.MaximumSize = new Size(620, 0);
        AddRow("Wi-Fi turbo", Stack(wifiCheck, explain, wifiStatusLabel, networkLink,
            MakeButton("Permitir en el Firewall de Windows…", AllowFirewall)));

        iphoneCheck.Text = "Recibir desde iPhone por Wi-Fi";
        iphoneCheck.AutoSize = true;
        iphoneCheck.CheckedChanged += (_, _) => SaveSetting(() => host.Config.IphoneEnabled = iphoneCheck.Checked, refreshStatus: true);
        iphoneStatusLabel.AutoSize = true;
        iphoneStatusLabel.ForeColor = Theme.Muted;
        iphoneStatusLabel.MaximumSize = new Size(620, 0);
        AddRow("iPhone", Stack(iphoneCheck, iphoneStatusLabel, MakeButton("Conectar un iPhone…", ShowIphoneDialog)));

        notifyCheck.Text = "Avisar al recibir archivos o texto";
        notifyCheck.AutoSize = true;
        notifyCheck.CheckedChanged += (_, _) => SaveSetting(() => host.Config.Notifications = notifyCheck.Checked);
        linksCheck.Text = "Abrir en el navegador los enlaces recibidos";
        linksCheck.AutoSize = true;
        linksCheck.CheckedChanged += (_, _) => SaveSetting(() => host.Config.OpenLinks = linksCheck.Checked);
        AddRow("Al recibir", Stack(notifyCheck, linksCheck, MutedLabel("El texto recibido siempre se copia al portapapeles.")));

        startupCheck.Text = "Iniciar DropToWin con Windows (en la bandeja del sistema)";
        startupCheck.AutoSize = true;
        startupCheck.CheckedChanged += (_, _) =>
        {
            if (loadingSettings) return;
            StartupRegistration.Set(startupCheck.Checked, host.Options.DataDir);
        };
        AddRow("Inicio", startupCheck);

        var fp = string.Join(" ", Enumerable.Range(0, 8).Select(i => host.Fingerprint.Substring(i * 4, 4))).ToUpperInvariant();
        var about = MutedLabel($"DropToWin {Application.ProductVersion.Split('+')[0]}\nHuella del certificado Wi-Fi: {fp}…");
        var dataLink = new LinkLabel { Text = "Abrir carpeta de datos (configuración y registro)", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent };
        dataLink.LinkClicked += (_, _) => Shell.Open(host.DataDir);
        AddRow("Acerca de", Stack(about, dataLink));

        page.Controls.Add(grid);
        return page;
    }

    Control BuildProgressPanel()
    {
        progressPanel.Dock = DockStyle.Bottom;
        progressPanel.AutoSize = true;
        progressPanel.ColumnCount = 1;
        progressPanel.Padding = new Padding(20, 4, 20, 14);
        progressPanel.Visible = false;
        progressTitle.AutoSize = true;
        progressTitle.Font = Theme.Title(10f);
        progressTitle.Margin = new Padding(0, 0, 0, 6);
        progressTitle.AutoEllipsis = true;
        progressBar.Dock = DockStyle.Fill;
        progressBar.Height = 10;
        progressBar.Maximum = 1000;
        progressBar.Style = ProgressBarStyle.Continuous;
        progressBar.Margin = new Padding(0, 0, 0, 6);
        progressDetail.AutoSize = true;
        progressDetail.ForeColor = Theme.Muted;
        progressPanel.Controls.Add(progressTitle);
        progressPanel.Controls.Add(progressBar);
        progressPanel.Controls.Add(progressDetail);
        return progressPanel;
    }

    ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        var open = menu.Items.Add("Abrir DropToWin", null, (_, _) => ShowFromTray());
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add("Abrir carpeta de recibidos", null, (_, _) => Shell.Open(host.Config.ReceiveFolder));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApp());
        return menu;
    }

    static Button MakeButton(string text, Action onClick)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 28), Padding = new Padding(6, 0, 6, 0) };
        button.Click += (_, _) => onClick();
        return button;
    }

    static Label MutedLabel(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Muted };

    // ───────────────────────────── Estado ─────────────────────────────

    void UpdateStatus()
    {
        var bt = host.Bluetooth;
        bluetoothSettingsButton.Visible = bt.State is not (BluetoothState.Active or BluetoothState.Starting);
        switch (bt.State)
        {
            case BluetoothState.Active:
                SetStatus(Theme.Ok, "Listo para recibir", "Bluetooth activo" + WifiSummary() + IphoneSummary());
                break;
            case BluetoothState.Starting:
                SetStatus(Theme.Muted, "Iniciando…", "Preparando el servicio Bluetooth.");
                break;
            case BluetoothState.Off:
                SetStatus(Theme.Warn, "Bluetooth desactivado", "Actívalo para poder recibir desde el teléfono.");
                break;
            case BluetoothState.NoAdapter:
                SetStatus(Theme.Bad, "No se encontró Bluetooth", "Este PC necesita un adaptador Bluetooth para usar DropToWin.");
                break;
            case BluetoothState.Unsupported:
                SetStatus(Theme.Bad, "Bluetooth no compatible", "El adaptador no admite Bluetooth clásico (RFCOMM).");
                break;
            default:
                SetStatus(Theme.Bad, "Error de Bluetooth", bt.ErrorMessage ?? "Error desconocido.");
                break;
        }
        pcLabel.Text = "Este PC: " + host.Config.PcName;
        if (!progressPanel.Visible)
            SetTrayText("DropToWin – " + statusTitle.Text);
        UpdateWifiStatus();
    }

    void SetStatus(Color color, string title, string detail)
    {
        statusDot.DotColor = color;
        statusTitle.Text = title;
        statusDetail.Text = detail;
    }

    string WifiSummary()
    {
        if (!host.Config.WifiTurbo) return " · Wi-Fi turbo desactivado";
        if (!host.Wifi.IsListening) return " · Wi-Fi turbo no disponible";
        var first = host.Wifi.OfferAddresses().FirstOrDefault();
        return first is null ? " · sin red (solo Bluetooth)" : $" · Wi-Fi turbo en {first.Split('/')[0]}";
    }

    string IphoneSummary()
    {
        if (!host.Config.IphoneEnabled) return "";
        return host.Iphone.IsListening ? " · iPhone listo" : " · iPhone no disponible";
    }

    void UpdateWifiStatus()
    {
        iphoneStatusLabel.Text = !host.Config.IphoneEnabled
            ? "Desactivado: los iPhone conectados no podrán enviar."
            : host.Iphone.IsListening
                ? $"Escuchando en el puerto {host.Iphone.Port}. Los iPhone envían por Wi-Fi desde el menú Compartir (app Atajos) o desde Safari."
                : $"No disponible: {host.Iphone.Error}.";
        if (!host.Wifi.IsListening)
        {
            wifiStatusLabel.Text = $"No disponible: {host.Wifi.Error}.";
        }
        else
        {
            var addrs = host.Wifi.OfferAddresses();
            wifiStatusLabel.Text = addrs.Count == 0
                ? "Sin conexión de red: se usará solo Bluetooth."
                : $"Escuchando en {string.Join(", ", addrs.Select(a => a.Split('/')[0]))} (puerto {host.Wifi.Port}).";
        }

        var profiles = LocalNetwork.GetProfiles();
        var publicNet = profiles.FirstOrDefault(p => p.Category == "Pública");
        if (publicNet is not null)
        {
            const string action = "Marcar la red como privada…";
            networkLink.Text = $"La red «{publicNet.Name}» es Pública: Windows bloqueará el Wi-Fi turbo.\n{action}";
            networkLink.LinkArea = new LinkArea(networkLink.Text.Length - action.Length, action.Length);
            networkLink.ForeColor = Theme.Warn;
        }
        else
        {
            networkLink.Text = profiles.Count == 0
                ? "Red: sin conexión"
                : "Red: " + string.Join(", ", profiles.Select(p => $"{p.Name} ({p.Category})"));
            networkLink.LinkArea = new LinkArea(0, 0);
            networkLink.ForeColor = Theme.Muted;
        }
    }

    void OnNetworkChanged(object? sender, EventArgs e) => Post(UpdateStatus);

    // ───────────────────────────── Recibidos ─────────────────────────────

    void LoadHistory()
    {
        receivedList.BeginUpdate();
        foreach (var item in Enumerable.Reverse(host.History.Snapshot()))
            AddHistoryRow(item, atTop: false);
        receivedList.EndUpdate();
        UpdateReceivedEmpty();
    }

    void AddHistoryRow(HistoryItem item, bool atTop)
    {
        var row = new ListViewItem(item.Name) { Tag = item, ImageKey = IconKey(item) };
        row.SubItems.Add(item.IsText ? "Texto" : Format.Size(item.Size));
        row.SubItems.Add(item.Sender);
        row.SubItems.Add(Format.TransportName(item.Transport));
        row.SubItems.Add(Format.When(item.TimeUtc));
        if (atTop) receivedList.Items.Insert(0, row);
        else receivedList.Items.Add(row);
        UpdateReceivedEmpty();
    }

    string IconKey(HistoryItem item)
    {
        var ext = item.IsText ? ".txt" : Path.GetExtension(item.Name).ToLowerInvariant();
        if (ext.Length == 0) ext = ".bin";
        if (!fileIcons.Images.ContainsKey(ext) && ShellIcons.SmallIcon(ext) is Icon icon)
            fileIcons.Images.Add(ext, icon);
        return ext;
    }

    void UpdateReceivedEmpty()
    {
        bool empty = receivedList.Items.Count == 0;
        receivedEmpty.Visible = empty;
        receivedList.Visible = !empty;
    }

    HistoryItem? SelectedItem() =>
        receivedList.SelectedItems.Count > 0 ? receivedList.SelectedItems[0].Tag as HistoryItem : null;

    void OpenSelected()
    {
        if (SelectedItem() is not HistoryItem item) return;
        if (item.IsText)
        {
            using var viewer = new TextViewerDialog(item.Sender, item.Text ?? "");
            viewer.ShowDialog(this);
            return;
        }
        if (item.Path is null || !File.Exists(item.Path))
        {
            MessageBox.Show(this, "El archivo ya no está donde se guardó (se movió o se borró).", "DropToWin",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Shell.Open(item.Path);
    }

    void CopySelected()
    {
        var items = receivedList.SelectedItems.Cast<ListViewItem>().Select(r => (HistoryItem)r.Tag!).ToList();
        if (items.Count == 0) return;
        if (items.Count == 1 && items[0].IsText)
        {
            TrySetClipboard(items[0].Text ?? "");
            return;
        }
        var files = new System.Collections.Specialized.StringCollection();
        files.AddRange(items.Where(i => i.Path is not null && File.Exists(i.Path)).Select(i => i.Path!).ToArray());
        if (files.Count > 0)
        {
            try { Clipboard.SetFileDropList(files); }
            catch (ExternalException) { }
        }
    }

    void RemoveSelected()
    {
        foreach (ListViewItem row in receivedList.SelectedItems)
        {
            host.History.Remove((HistoryItem)row.Tag!);
            receivedList.Items.Remove(row);
        }
        UpdateReceivedEmpty();
    }

    void ClearHistory()
    {
        if (receivedList.Items.Count == 0) return;
        if (MessageBox.Show(this, "¿Vaciar la lista? Los archivos recibidos no se borran.", "DropToWin",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        host.History.Clear();
        receivedList.Items.Clear();
        UpdateReceivedEmpty();
    }

    void DragSelectedFiles()
    {
        var paths = receivedList.SelectedItems.Cast<ListViewItem>()
            .Select(r => ((HistoryItem)r.Tag!).Path)
            .Where(p => p is not null && File.Exists(p))
            .Select(p => p!)
            .ToArray();
        if (paths.Length > 0)
            receivedList.DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
    }

    void OnTextReceived(HistoryItem item)
    {
        var text = item.Text ?? "";
        TrySetClipboard(text);
        bool isLink = Shell.TryGetWebLink(text, out var uri);
        if (isLink && host.Config.OpenLinks)
            Shell.Open(uri.AbsoluteUri);
        Notify(isLink ? "Enlace recibido" : "Texto recibido",
            $"De {item.Sender}: {item.Name}\nCopiado al portapapeles.",
            isLink ? () => Shell.Open(uri.AbsoluteUri) : () =>
            {
                ShowFromTray();
                using var viewer = new TextViewerDialog(item.Sender, text);
                viewer.ShowDialog(this);
            });
    }

    // Los avisos se agrupan: Atajos de iOS envía cada archivo en una petición (y un lote) distinto.
    void OnBatchCompleted(Batch batch)
    {
        if (noticeSender is not null && noticeSender != batch.Sender) FlushNotice();
        noticeSender = batch.Sender;
        foreach (var f in batch.Files)
        {
            if (noticeLastPath is not null && Path.GetDirectoryName(noticeLastPath) != Path.GetDirectoryName(f.SavedPath))
                noticeSeveralFolders = true;
            noticeLastPath = f.SavedPath;
            noticeCount++;
            noticeBytes += f.Size;
        }
        noticeTimer.Stop();
        noticeTimer.Start();
    }

    void FlushNotice()
    {
        noticeTimer.Stop();
        if (noticeSender is null || noticeLastPath is null) return;
        var (sender, path, count, bytes, several) = (noticeSender, noticeLastPath, noticeCount, noticeBytes, noticeSeveralFolders);
        noticeSender = null;
        noticeLastPath = null;
        noticeCount = 0;
        noticeBytes = 0;
        noticeSeveralFolders = false;
        if (count == 1)
            Notify("Archivo recibido", $"{Path.GetFileName(path)} ({Format.Size(bytes)}) de {sender}", () => Shell.Reveal(path));
        else
            Notify($"{count} archivos recibidos", $"{Format.Size(bytes)} de {sender}",
                () => Shell.Open(several ? host.Config.ReceiveFolder : Path.GetDirectoryName(path)!));
    }

    void ShowIphoneDialog()
    {
        if (!host.Config.IphoneEnabled)
        {
            if (MessageBox.Show(this, "La recepción desde iPhone está desactivada. ¿Activarla?", "DropToWin",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            iphoneCheck.Checked = true;
        }
        ShowFromTray();
        using var dialog = new IphoneDialog(host);
        dialog.ShowDialog(this);
    }

    // ───────────────────────────── Progreso ─────────────────────────────

    void OnProgress(BatchProgress p)
    {
        lastProgressUtc = DateTime.UtcNow;
        progressPanel.Visible = true;
        progressTimer.Start();
        var via = Format.TransportName(p.Transport);

        if (p.Completed)
        {
            progressCompleted = true;
            progressTitle.Text = p.FileCount == 1
                ? $"Recibido de {p.Sender}: {p.FileName}"
                : $"Recibidos {p.FileCount} archivos de {p.Sender}";
            progressBar.Value = progressBar.Maximum;
            progressDetail.Text = $"{Format.Size(p.Total)} por {via}";
            SetTrayText("DropToWin – " + statusTitle.Text);
            return;
        }

        progressCompleted = false;
        progressTitle.Text = p.FileCount == 1
            ? $"Recibiendo de {p.Sender} por {via}: {p.FileName}"
            : $"Recibiendo de {p.Sender} por {via}: {p.FileName} ({p.FileIndex + 1} de {p.FileCount})";
        int permille = p.Total > 0 ? (int)Math.Clamp(p.Received * 1000 / p.Total, 0, 1000) : 0;
        progressBar.Value = permille;
        var eta = p.BytesPerSecond > 1 ? Format.Duration((p.Total - p.Received) / p.BytesPerSecond) : "";
        progressDetail.Text = $"{permille / 10} % · {Format.Size(p.Received)} de {Format.Size(p.Total)} · {Format.Speed(p.BytesPerSecond)}" +
                              (eta.Length > 0 ? $" · quedan {eta}" : "");
        SetTrayText($"DropToWin – recibiendo {permille / 10} %");
    }

    void CheckProgressIdle()
    {
        var idle = DateTime.UtcNow - lastProgressUtc;
        if ((progressCompleted && idle > TimeSpan.FromSeconds(6)) || (!progressCompleted && idle > TimeSpan.FromMinutes(2)))
        {
            progressPanel.Visible = false;
            progressTimer.Stop();
            SetTrayText("DropToWin – " + statusTitle.Text);
        }
        else if (!progressCompleted && idle > TimeSpan.FromSeconds(5))
        {
            progressDetail.Text = "En pausa: esperando a que el teléfono se reconecte…";
        }
    }

    // ───────────────────────────── Dispositivos ─────────────────────────────

    void RefreshDevices()
    {
        deviceList.BeginUpdate();
        deviceList.Items.Clear();
        foreach (var d in host.Config.DevicesSnapshot().OrderByDescending(d => d.LastSeenUtc))
        {
            var row = new ListViewItem(d.Name) { Tag = d.DeviceId };
            row.SubItems.Add(d.Kind == "iphone" ? "iPhone (Wi-Fi)" : "Android (Bluetooth)");
            row.SubItems.Add(d.BtAddress ?? "—");
            row.SubItems.Add(Format.When(d.ApprovedUtc));
            row.SubItems.Add(Format.When(d.LastSeenUtc));
            deviceList.Items.Add(row);
        }
        deviceList.EndUpdate();
        revokeButton.Enabled = deviceList.SelectedItems.Count > 0;
    }

    void RevokeSelectedDevice()
    {
        if (deviceList.SelectedItems.Count == 0) return;
        var row = deviceList.SelectedItems[0];
        if (MessageBox.Show(this, $"¿Quitar la autorización de «{row.Text}»? Tendrá que pedir permiso de nuevo para enviar archivos.",
                "DropToWin", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        host.RevokeDevice((string)row.Tag!);
    }

    void RevealApk()
    {
        var apk = Path.Combine(AppContext.BaseDirectory, "DropToWin.apk");
        if (File.Exists(apk))
        {
            Shell.Reveal(apk);
            return;
        }
        MessageBox.Show(this,
            "No se encontró DropToWin.apk junto a DropToWin.exe.\n\nCópialo al teléfono (por cable, correo o la nube) y ábrelo para instalarlo.",
            "DropToWin", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    Task<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ApprovalDialog? dialog = null;
        ct.Register(() =>
        {
            result.TrySetCanceled();
            Post(() => { if (dialog is { IsDisposed: false }) dialog.Close(); });
        });
        Post(() =>
        {
            if (ct.IsCancellationRequested) return;
            dialog = new ApprovalDialog(request);
            dialog.FormClosed += (_, _) =>
            {
                result.TrySetResult(dialog.Approved);
                dialog.Dispose();
            };
            Notify("Solicitud de conexión", $"«{request.DeviceName}» quiere enviarte archivos.",
                () => dialog?.Activate(), force: true);
            dialog.Show();
            dialog.Activate();
        });
        return result.Task;
    }

    // ───────────────────────────── Ajustes ─────────────────────────────

    void LoadSettings()
    {
        loadingSettings = true;
        var c = host.Config;
        folderBox.Text = c.ReceiveFolder;
        organizeCheck.Checked = c.OrganizeByType;
        pcNameBox.Text = c.PcName;
        wifiCheck.Checked = c.WifiTurbo;
        iphoneCheck.Checked = c.IphoneEnabled;
        notifyCheck.Checked = c.Notifications;
        linksCheck.Checked = c.OpenLinks;
        startupCheck.Checked = StartupRegistration.IsEnabled();
        loadingSettings = false;
    }

    void SaveSetting(Action apply, bool refreshStatus = false)
    {
        if (loadingSettings) return;
        apply();
        host.Config.Save();
        if (refreshStatus) UpdateStatus();
    }

    void SavePcName()
    {
        var name = pcNameBox.Text.Trim();
        if (name.Length == 0)
        {
            pcNameBox.Text = host.Config.PcName;
            return;
        }
        if (name == host.Config.PcName) return;
        SaveSetting(() => host.Config.PcName = name, refreshStatus: true);
    }

    void ChangeFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Carpeta donde se guardarán los archivos recibidos",
            UseDescriptionForTitle = true,
            InitialDirectory = host.Config.ReceiveFolder,
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SaveSetting(() => host.Config.ReceiveFolder = dialog.SelectedPath);
        folderBox.Text = dialog.SelectedPath;
    }

    async void AllowFirewall()
    {
        string? error = null;
        bool ok = await Task.Run(() => FirewallHelper.AllowWifiTurbo($"{host.Wifi.Port},{host.Iphone.Port}", out error));
        if (ok)
            MessageBox.Show(this, "Listo: el Firewall de Windows permite el Wi-Fi turbo y la recepción desde iPhone en redes privadas.", "DropToWin",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        else if (error is not null)
            MessageBox.Show(this, "No se pudo configurar el firewall: " + error, "DropToWin",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        UpdateWifiStatus();
    }

    // ───────────────────────────── Bandeja y ventana ─────────────────────────────

    void Notify(string title, string text, Action? onClick, bool force = false)
    {
        if (!force && !host.Config.Notifications) return;
        balloonAction = onClick;
        tray.ShowBalloonTip(5000, title, text, ToolTipIcon.None);
    }

    void SetTrayText(string text) => tray.Text = text.Length > 127 ? text[..127] : text;

    public void ShowFromTray() => Post(() =>
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    });

    public void HideTrayIcon() => tray.Visible = false;

    void ExitApp()
    {
        exiting = true;
        tray.Visible = false;
        ExitRequested?.Invoke();
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                Notify("DropToWin sigue activo", "Seguirá recibiendo en segundo plano. Para cerrarlo usa «Salir» en el icono de la bandeja.",
                    ShowFromTray, force: true);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (!exiting)
        {
            exiting = true;
            ExitRequested?.Invoke();
        }
    }

    void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    internal static void TrySetClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch (ExternalException ex) { Log.Error("Portapapeles", ex); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            progressTimer.Dispose();
            noticeTimer.Dispose();
            tray.Dispose();
            fileIcons.Dispose();
        }
        base.Dispose(disposing);
    }
}
