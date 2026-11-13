using System.Drawing;
using Peritum.Protect.Core;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Peritum.Protect.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pause;
    private readonly ContextMenuStrip _menu;

    public TrayService()
    {
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = TextCatalog.Get("trayOn")
        };
        _menu = new ContextMenuStrip();
        _menu.Items.Add(TextCatalog.Get("trayOpen"), null, (_, _) => OpenRequested?.Invoke());
        _pause = new ToolStripMenuItem(TextCatalog.Get("trayPause"));
        AddPauseItems();
        _menu.Items.Add(_pause);
        _menu.Items.Add(TextCatalog.Get("traySettings"), null, (_, _) => SettingsRequested?.Invoke());
        _menu.Items.Add(TextCatalog.Get("quit"), null, (_, _) => QuitRequested?.Invoke());
        _icon.ContextMenuStrip = _menu;
        ApplyTheme(dark: false, highContrast: false);
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenRequested?.Invoke();
        };
    }

    public event Action? OpenRequested;
    public event Action<TimeSpan>? PauseForRequested;
    public event Action? PauseUntilResumeRequested;
    public event Action? ResumeRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    private void AddPauseItems()
    {
        _pause.DropDownItems.Add(TextCatalog.Get("pause15"), null, (_, _) => PauseForRequested?.Invoke(TimeSpan.FromMinutes(15)));
        _pause.DropDownItems.Add(TextCatalog.Get("pauseHour"), null, (_, _) => PauseForRequested?.Invoke(TimeSpan.FromHours(1)));
        _pause.DropDownItems.Add(TextCatalog.Get("pauseUntil"), null, (_, _) => PauseUntilResumeRequested?.Invoke());
    }

    public void SetPauseState(bool paused, string status)
    {
        _icon.Text = status.Length <= 63 ? status : status[..63];
        _icon.Icon = LoadState(paused ? "tray-paused.ico" : "tray-on.ico");
        _pause.Text = paused ? TextCatalog.Get("trayResume") : TextCatalog.Get("trayPause");
        _pause.DropDownItems.Clear();
        if (paused)
            _pause.DropDownItems.Add(TextCatalog.Get("trayResume"), null, (_, _) => ResumeRequested?.Invoke());
        AddPauseItems();
    }

    public void SetWarningWaiting()
    {
        _icon.Text = TextCatalog.Get("trayWarning");
        _icon.Icon = LoadState("tray-warning.ico");
    }

    public void ApplyTheme(bool dark, bool highContrast)
    {
        if (highContrast)
        {
            _menu.RenderMode = ToolStripRenderMode.System;
            _menu.BackColor = System.Drawing.SystemColors.Menu;
            _menu.ForeColor = System.Drawing.SystemColors.MenuText;
        }
        else
        {
            var table = new TrayColorTable(dark);
            _menu.Renderer = new ToolStripProfessionalRenderer(table) { RoundedEdges = false };
            _menu.BackColor = table.MenuBackground;
            _menu.ForeColor = table.MenuForeground;
        }
        foreach (ToolStripItem item in _menu.Items)
            item.ForeColor = _menu.ForeColor;
    }

    public void ShowWarning(string message)
    {
        _icon.BalloonTipTitle = TextCatalog.Get("product");
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = ToolTipIcon.Warning;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static Icon LoadIcon() => LoadState("PeritumProtect.ico");

    private static Icon LoadState(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        if (File.Exists(path))
            return new Icon(path, 32, 32);
        return MakeIcon();
    }

    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(61, 126, 255));
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("P", font, Brushes.White, 8, 6);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private sealed class TrayColorTable : ProfessionalColorTable
    {
        public TrayColorTable(bool dark)
        {
            MenuBackground = dark ? Color.FromArgb(11, 18, 54) : Color.White;
            MenuForeground = dark ? Color.FromArgb(230, 241, 255) : Color.FromArgb(11, 18, 54);
            var hover = dark ? Color.FromArgb(20, 32, 68) : Color.FromArgb(237, 233, 254);
            var line = dark ? Color.FromArgb(30, 42, 85) : Color.FromArgb(220, 225, 236);
            Hover = hover;
            Line = line;
        }

        public Color MenuBackground { get; }
        public Color MenuForeground { get; }
        private Color Hover { get; }
        private Color Line { get; }

        public override Color ToolStripDropDownBackground => MenuBackground;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuBorder => Line;
        public override Color ImageMarginGradientBegin => MenuBackground;
        public override Color ImageMarginGradientMiddle => MenuBackground;
        public override Color ImageMarginGradientEnd => MenuBackground;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
    }
}
