using DropToWin.Server;

namespace DropToWin.Ui;

/// <summary>Pregunta si se permite a un teléfono nuevo enviar archivos a este PC.</summary>
sealed class ApprovalDialog : Form
{
    readonly Label countdown = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    int remaining = 60;

    public bool Approved { get; private set; }

    public ApprovalDialog(ApprovalRequest request)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "DropToWin – Nueva conexión";
        Icon = Theme.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20, 18, 20, 14);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var picture = new PictureBox
        {
            Image = Theme.LargeIcon.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(48, 48),
            Margin = new Padding(0, 2, 16, 0),
        };
        layout.Controls.Add(picture, 0, 0);
        layout.SetRowSpan(picture, 5);

        var where = request.Peer.Transport == "bluetooth"
            ? $"Dispositivo Bluetooth: {request.Peer.DisplayName}" +
              (request.Peer.BtAddress is null ? "" : $" ({request.Peer.BtAddress})")
            : $"Conexión desde {request.Peer.DisplayName}";

        layout.Controls.Add(new Label { Text = "Nueva conexión", Font = Theme.Title(13f), AutoSize = true, Margin = new Padding(0, 0, 0, 6) }, 1, 0);
        layout.Controls.Add(new Label
        {
            Text = $"«{request.DeviceName}» quiere enviarte archivos a este PC.",
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 0, 0, 4),
        }, 1, 1);
        layout.Controls.Add(new Label { Text = where, AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(380, 0) }, 1, 2);
        layout.Controls.Add(new Label
        {
            Text = "Permite solo dispositivos que reconozcas. Podrás quitar el permiso en la pestaña Dispositivos.",
            AutoSize = true,
            ForeColor = Theme.Muted,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 10, 0, 0),
        }, 1, 3);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
            WrapContents = false,
        };
        var allow = new Button { Text = "Permitir", AutoSize = true, MinimumSize = new Size(96, 30) };
        var deny = new Button { Text = "Rechazar", AutoSize = true, MinimumSize = new Size(96, 30), DialogResult = DialogResult.Cancel };
        allow.Click += (_, _) => { Approved = true; Close(); };
        deny.Click += (_, _) => Close();
        buttons.Controls.Add(allow);
        buttons.Controls.Add(deny);
        countdown.AutoSize = true;
        countdown.ForeColor = Theme.Muted;
        countdown.Margin = new Padding(0, 9, 12, 0);
        buttons.Controls.Add(countdown);
        layout.Controls.Add(buttons, 1, 4);
        CancelButton = deny;

        Controls.Add(layout);
        ResumeLayout(false);
        PerformLayout();

        UpdateCountdown();
        timer.Tick += (_, _) =>
        {
            remaining--;
            UpdateCountdown();
            if (remaining <= 0) Close();
        };
        timer.Start();
    }

    void UpdateCountdown() => countdown.Text = $"Se rechazará en {remaining} s";

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        System.Media.SystemSounds.Asterisk.Play();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Muestra un texto recibido completo.</summary>
sealed class TextViewerDialog : Form
{
    public TextViewerDialog(string sender, string text)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"Texto de {sender}";
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 320);
        MinimumSize = new Size(360, 220);
        ShowInTaskbar = false;
        Padding = new Padding(12);

        var box = new TextBox
        {
            Text = text.ReplaceLineEndings("\r\n"),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10f),
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        var close = new Button { Text = "Cerrar", AutoSize = true, MinimumSize = new Size(90, 28), DialogResult = DialogResult.OK };
        var copy = new Button { Text = "Copiar", AutoSize = true, MinimumSize = new Size(90, 28) };
        copy.Click += (_, _) => { MainForm.TrySetClipboard(text); copy.Text = "Copiado ✓"; };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        if (Shell.TryGetWebLink(text, out var uri))
        {
            var open = new Button { Text = "Abrir enlace", AutoSize = true, MinimumSize = new Size(90, 28) };
            open.Click += (_, _) => Shell.Open(uri.AbsoluteUri);
            buttons.Controls.Add(open);
        }
        Controls.Add(box);
        Controls.Add(buttons);
        AcceptButton = close;
        ResumeLayout(false);
        PerformLayout();
    }
}
