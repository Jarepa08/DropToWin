namespace DropToWin.Ui;

/// <summary>Muestra el código QR de un solo uso con el que un iPhone se conecta a este PC.</summary>
sealed class IphoneDialog : Form
{
    readonly AppHost host;
    readonly PictureBox qr = new();
    readonly ComboBox addressBox = new();
    readonly TextBox urlBox = new();
    readonly Label status = new();
    readonly System.Windows.Forms.Timer closeTimer = new() { Interval = 4000 };

    public IphoneDialog(AppHost host)
    {
        this.host = host;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "DropToWin – Conectar un iPhone";
        Icon = Theme.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20, 18, 20, 14);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        var qrFrame = new Panel { BackColor = Color.White, Padding = new Padding(8), AutoSize = true, Margin = new Padding(0, 0, 20, 0) };
        qr.SizeMode = PictureBoxSizeMode.AutoSize;
        qrFrame.Controls.Add(qr);
        layout.Controls.Add(qrFrame, 0, 0);
        layout.SetRowSpan(qrFrame, 6);

        layout.Controls.Add(new Label { Text = "Conectar un iPhone", Font = Theme.Title(13f), AutoSize = true, Margin = new Padding(0, 0, 0, 8) }, 1, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Margin = new Padding(0, 0, 0, 8),
            Text =
                "1.  Conecta el iPhone a la misma red Wi-Fi que este PC.\n" +
                "2.  Abre la Cámara del iPhone, apunta a este código y toca el enlace amarillo.\n" +
                "3.  En la página que se abre, escribe un nombre para el iPhone y toca «Conectar».\n\n" +
                "Desde esa página ya podrá enviar fotos, videos y texto, y verá cómo añadir «Enviar al PC» " +
                "al menú Compartir de iOS con la app Atajos.",
        }, 1, 1);

        var addressRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        addressRow.Controls.Add(new Label { Text = "Dirección de este PC:", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 6, 0) });
        addressBox.DropDownStyle = ComboBoxStyle.DropDownList;
        addressBox.Width = 160;
        addressBox.SelectedIndexChanged += (_, _) => Regenerate();
        addressRow.Controls.Add(addressBox);
        layout.Controls.Add(addressRow, 1, 2);

        urlBox.ReadOnly = true;
        urlBox.Width = 400;
        urlBox.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(urlBox, 1, 3);

        status.AutoSize = true;
        status.MaximumSize = new Size(400, 0);
        status.ForeColor = Theme.Muted;
        layout.Controls.Add(status, 1, 4);

        var close = new Button { Text = "Cerrar", AutoSize = true, MinimumSize = new Size(96, 30), DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = new Padding(0, 16, 0, 0) };
        layout.Controls.Add(close, 1, 5);
        CancelButton = close;

        Controls.Add(layout);
        ResumeLayout(false);
        PerformLayout();

        closeTimer.Tick += (_, _) => Close();
        host.Iphone.Paired += OnPaired;

        var addresses = host.Iphone.Addresses();
        if (!host.Iphone.IsListening)
        {
            ShowProblem($"El receptor para iPhone no está disponible: {host.Iphone.Error}.");
        }
        else if (addresses.Count == 0)
        {
            ShowProblem("Este PC no está conectado a ninguna red. Conéctalo a la misma Wi-Fi que el iPhone.");
        }
        else
        {
            foreach (var a in addresses) addressBox.Items.Add(a);
            addressBox.SelectedIndex = 0; // dispara Regenerate()
        }
    }

    void Regenerate()
    {
        if (addressBox.SelectedItem is not string ip) return;
        var code = host.Iphone.NewPairingCode();
        var url = $"http://{ip}:{host.Iphone.Port}/iphone#p={code}";
        var old = qr.Image;
        qr.Image = IconArt.Qr(url, Math.Max(4, LogicalToDeviceUnits(6)));
        old?.Dispose();
        urlBox.Text = url;
        status.ForeColor = Theme.Muted;
        status.Text = "Esperando al iPhone… El código sirve una sola vez y caduca en 10 minutos.";
    }

    void ShowProblem(string message)
    {
        addressBox.Enabled = false;
        status.ForeColor = Theme.Bad;
        status.Text = message;
    }

    void OnPaired(TrustedDevice device)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            status.ForeColor = Theme.Ok;
            status.Text = $"✓ «{device.Name}» se conectó. Ya puede enviarte archivos.";
            qr.Visible = false;
            closeTimer.Start();
        });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        host.Iphone.Paired -= OnPaired;
        host.Iphone.CancelPairing();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closeTimer.Dispose();
            qr.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
