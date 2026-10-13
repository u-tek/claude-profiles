using System.Windows;
using ClaudeProfiles.Services;
using Forms = System.Windows.Forms;

namespace ClaudeProfiles;

public partial class App : Application
{
    SingleInstance? _single;
    Forms.NotifyIcon? _tray;
    MainWindow? _window;

    public static bool ProbeMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        if (args.Length == 2 && args[0] == "--export-icon")
        {
            Icons.WriteIco(args[1], Icons.AppMark);
            Shutdown();
            return;
        }

        // Development aid: render the window to a PNG and exit (does not touch a running launcher).
        var shotIdx = Array.IndexOf(args, "--screenshot");
        if (shotIdx >= 0 && shotIdx + 1 < args.Length)
        {
            Theme.Apply(args.Contains("--dark") || (!args.Contains("--light") && Theme.SystemPrefersDark()));
            var shotWindow = new MainWindow();
            shotWindow.CaptureAndExit(args[shotIdx + 1], args.Contains("--dialog"));
            return;
        }

        var message = args.Length >= 2 && args[0] == "--open" ? "open:" + args[1] : "show";
        ProbeMode = args.Contains("--probe-mode");

        _single = new SingleInstance();
        if (!_single.IsFirst)
        {
            SingleInstance.Send(message);
            Shutdown();
            return;
        }

        Theme.Apply(Theme.SystemPrefersDark());
        Log.Write($"launcher started {string.Join(' ', args)}");

        _window = new MainWindow();
        _single.Listen(m => Dispatcher.Invoke(() => HandleMessage(m)));
        SetUpTray();
        Shortcuts.EnsureStartMenu();

        if (message.StartsWith("open:"))
        {
            // Opened from a profile shortcut: launch it and stay in the tray.
            _window.OpenProfile(message[5..]);
        }
        else
        {
            _window.Show();
        }
    }

    void HandleMessage(string m)
    {
        if (_window == null) return;
        if (m.StartsWith("open:")) _window.OpenProfile(m[5..]);
        else ShowWindow();
    }

    public void ShowWindow()
    {
        if (_window == null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    void SetUpTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = "Claude Profiles",
            Icon = new System.Drawing.Icon(Icons.AppIcon()),
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowWindow(); };
        _tray.ContextMenuStrip = new Forms.ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => BuildTrayMenu();
        BuildTrayMenu();
    }

    void BuildTrayMenu()
    {
        if (_tray?.ContextMenuStrip is not { } menu || _window == null) return;
        menu.Items.Clear();
        foreach (var p in _window.Records)
        {
            var id = p.Id;
            menu.Items.Add("Open " + p.Name, null, (_, _) => Dispatcher.Invoke(() => _window.OpenProfile(id)));
        }
        if (_window.Records.Count > 0) menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Show Claude Profiles", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));
    }

    public void Notify(string title, string text)
    {
        if (_tray == null) return;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.ShowBalloonTip(4000);
    }

    public void Quit()
    {
        _window?.Shutdown();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _single?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}
