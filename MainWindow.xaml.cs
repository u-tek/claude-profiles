using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClaudeProfiles.Services;

namespace ClaudeProfiles;

public sealed class CardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Card { get; set; }
    public DataTemplate? Add { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is ProfileItem { Kind: ItemKind.AddTile } ? Add : Card;
}

public partial class MainWindow : Window
{
    readonly List<ProfileRecord> _records;
    readonly ObservableCollection<ProfileItem> _claudeItems = new(), _codexItems = new();
    readonly ProfileItem _main = new(ItemKind.Main);
    readonly ProfileItem _mainCodex = new(ItemKind.Main, app: AppKind.Codex);
    readonly ProfileItem _addTile = new(ItemKind.AddTile);
    readonly ProfileItem _addCodexTile = new(ItemKind.AddTile, app: AppKind.Codex);
    readonly bool _codexInstalled = CodexApp.IsInstalled;
    readonly SignInRouter _router;
    readonly DispatcherTimer _timer;
    volatile IReadOnlyList<SignInTarget> _waiting = Array.Empty<SignInTarget>();
    bool _refreshing, _trayHintShown;
    ProfileItem? _bannerTarget;

    // Dialog state
    ProfileRecord? _editing;
    string _pickedColor = ProfileStore.Palette[0];
    AppKind _pickedApp;
    Action? _confirmAction;

    public IReadOnlyList<ProfileRecord> Records => _records;

    ObservableCollection<ProfileItem> ItemsFor(AppKind app) => app == AppKind.Codex ? _codexItems : _claudeItems;
    ProfileItem AddTileFor(AppKind app) => app == AppKind.Codex ? _addCodexTile : _addTile;

    /// <summary>Every card that stands for an app instance (the "new profile" tiles left out).</summary>
    IEnumerable<ProfileItem> Cards => _claudeItems.Concat(_codexItems).Where(i => i.Kind != ItemKind.AddTile);

    public MainWindow()
    {
        InitializeComponent();
        TitleMark.Source = Icons.AppMark(40);
        Icon = Icons.AppMark(64);

        _records = ProfileStore.Load();
        _claudeItems.Add(_main);
        if (_codexInstalled) _codexItems.Add(_mainCodex);
        foreach (var r in _records) ItemsFor(r.App).Add(new ProfileItem(ItemKind.Profile, r));
        _claudeItems.Add(_addTile);
        _codexItems.Add(_addCodexTile);
        ClaudeCards.ItemsSource = _claudeItems;
        CodexCards.ItemsSource = _codexItems;
        UpdateSections();

        _router = new SignInRouter(() => _waiting);
        _router.Routed += t => Dispatcher.BeginInvoke(() => OnRouted(t));
        _router.ProbeCaught += (pid, lag) => Dispatcher.BeginInvoke(() => ShowToast($"Probe caught after {lag.TotalMilliseconds:F0} ms"));

        NotInstalled.Visibility = ClaudeApp.IsInstalled ? Visibility.Collapsed : Visibility.Visible;
        FooterText.Text = $"Profiles are stored in {AppPaths.Root}";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();

        StateChanged += (_, _) => OnStateChanged();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // ---------- Live state ----------

    void UpdateSections()
    {
        var showCodex = _codexInstalled || _records.Any(r => r.App == AppKind.Codex);
        CodexSection.Visibility = showCodex ? Visibility.Visible : Visibility.Collapsed;
        ClaudeHeading.Visibility = showCodex ? Visibility.Visible : Visibility.Collapsed;
        ClaudeCards.Margin = new Thickness(-8, showCodex ? 10 : 22, -8, 0);
        IntroText.Text = $"Each profile is its own {(showCodex ? "Claude or Codex" : "Claude")}, with a separate sign-in, " +
                         "chats, folders and settings. Open as many as you like, side by side.";
    }

    async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var cards = Cards.ToList();
            var (procs, states) = await Task.Run(() =>
            {
                var p = ClaudeProcesses.Snapshot();
                var s = cards.ToDictionary(c => c, ReadState);
                return (p, s);
            });

            foreach (var item in cards)
            {
                item.IsRunning = ProcessFor(item, procs) != null;
                var st = states[item];
                item.IsSignedIn = st.SignedIn;
                item.AccountUuid = st.AccountUuid;
                item.LastAttempt = st.LastSignInAttempt;
            }

            MarkSharedAccounts();
            UpdateWaiting();
        }
        catch (Exception ex) { Log.Write("refresh failed: " + ex); }
        finally { _refreshing = false; }
    }

    static ProfileState ReadState(ProfileItem item) =>
        !item.IsCodex ? ProfileInspector.Read(item.DataDir)
        : ProfileInspector.ReadCodex(item.IsMain ? CodexApp.MainHome : CodexApp.HomeDir(item.DataDir));

    static ClaudeProcess? ProcessFor(ProfileItem item, List<ClaudeProcess> procs) => (item.IsMain, item.IsCodex) switch
    {
        (true, false) => ClaudeProcesses.MainClaude(procs),
        (true, true) => ClaudeProcesses.MainCodex(procs),
        (false, false) => ClaudeProcesses.MainFor(procs, item.DataDir),
        (false, true) => ClaudeProcesses.MainFor(procs, AppPaths.Normalize(CodexApp.UserDataDir(item.DataDir))),
    };

    void MarkSharedAccounts()
    {
        var cards = Cards.ToList();
        foreach (var item in cards)
        {
            var others = item.IsSignedIn && item.AccountUuid != null
                ? cards.Where(o => o != item && o.IsSignedIn && o.AccountUuid == item.AccountUuid).Select(o => o.Name).ToList()
                : new List<string>();
            item.SharedWith = others.Count == 0 ? null : string.Join(", ", others);
        }
    }

    void UpdateWaiting()
    {
        // Signed-out profiles wait for a sign-in; signed-in ones only briefly after starting a re-sign-in.
        // Codex signs in through a localhost callback that reaches the right window by itself, so it's never routed.
        var recent = DateTime.Now - TimeSpan.FromMinutes(5);
        var waiting = _claudeItems
            .Where(i => i.IsProfile && i.IsRunning && (!i.IsSignedIn || i.LastAttempt > recent))
            .ToList();

        _waiting = waiting.Select(i => new SignInTarget(i.Record!.Id, i.Name, i.DataDir, i.LastAttempt, i.Record.LastOpened)).ToList();
        _router.MainNeedsSignIn = _main.IsRunning && !_main.IsSignedIn;
        _router.SetArmed(_waiting.Count > 0 || App.ProbeMode);

        var target = waiting.Where(i => !i.IsSignedIn).OrderByDescending(i => i.LastAttempt ?? i.Record!.LastOpened ?? DateTime.MinValue).FirstOrDefault();
        _bannerTarget = target;
        if (target == null)
        {
            Banner.Visibility = Visibility.Collapsed;
            return;
        }

        BannerTitle.Text = $"Sign in to “{target.Name}”";
        BannerBody.Text = $"Sign in from the {target.Name} window, with Google or email. When your browser finishes, " +
                          $"the sign-in comes straight back to {target.Name}. In the browser, pick the account you want for this profile.";
        BannerButton.Content = $"Show {target.Name}";
        Banner.Visibility = Visibility.Visible;
    }

    void OnRouted(SignInTarget t)
    {
        ShowToast($"Signed in. Sent to {t.Name}.");
        if (!IsVisible) ((App)Application.Current).Notify("Signed in", $"Your sign-in was sent to {t.Name}.");
        _ = RefreshAsync();
    }

    // ---------- Opening profiles ----------

    public void OpenProfile(string id)
    {
        var item = Cards.FirstOrDefault(i => i.Record?.Id == id);
        if (item == null) { ((App)Application.Current).ShowWindow(); return; }
        Open(item);
    }

    void Open(ProfileItem item)
    {
        if (item.IsCodex ? !CodexApp.IsInstalled : !ClaudeApp.IsInstalled)
        {
            ((App)Application.Current).ShowWindow();
            if (item.IsCodex) ShowToast("Codex isn't installed. Get it from the Microsoft Store.");
            else NotInstalled.Visibility = Visibility.Visible;
            return;
        }
        try
        {
            if (!item.IsCodex) ClaudeApp.Launch(item.IsMain ? null : item.DataDir);
            else if (item.IsMain) CodexApp.LaunchMain();
            else CodexApp.Launch(item.DataDir);
            if (item.Record != null)
            {
                item.Record.LastOpened = DateTime.Now;
                ProfileStore.Save(_records);
                item.Refresh();
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't open {item.AppName}: " + ex.Message);
        }
        DelayedRefresh();
    }

    void DelayedRefresh()
    {
        foreach (var ms in new[] { 1500, 4000 })
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += async (_, _) => { t.Stop(); await RefreshAsync(); };
            t.Start();
        }
    }

    void Primary_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProfileItem item) Open(item);
    }

    void BannerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bannerTarget != null) Open(_bannerTarget);
    }

    // ---------- Card menu ----------

    void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ProfileItem item) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        MenuItem Add(string glyph, string text, Action action, bool enabled = true, Brush? color = null)
        {
            var mi = new MenuItem
            {
                Header = text,
                Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Glyphs"), FontSize = 13, Foreground = color },
                IsEnabled = enabled,
            };
            if (color != null) mi.Foreground = color;
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
            return mi;
        }

        if (item.IsProfile)
        {
            var r = item.Record!;
            Add("", "Rename or change colour", () => ShowEditDialog(r));
            if (Shortcuts.HasDesktopShortcut(r))
                Add("", "Remove desktop shortcut", () => { Shortcuts.RemoveForProfile(r); ShowToast("Desktop shortcut removed"); });
            else
                Add("", "Add desktop shortcut", () => CreateShortcut(r));
            Add("", "Open data folder", () => Reveal(r.DataDir));
            menu.Items.Add(new Separator());
            Add("", "Quit this profile", () => QuitProfile(item), item.IsRunning);
            Add("", "Start fresh (sign out)…", () => ConfirmReset(item), item.IsSignedIn || item.IsRunning);
            Add("", "Delete profile…", () => ConfirmDelete(item), true, (Brush)FindResource("Danger"));
        }
        else
        {
            Add("", "Open data folder", () => Reveal(item.DataDir));
        }
        menu.IsOpen = true;
    }

    void CreateShortcut(ProfileRecord r)
    {
        try { Shortcuts.CreateForProfile(r); ShowToast("Shortcut added to your desktop"); }
        catch (Exception ex) { ShowToast("Couldn't add shortcut: " + ex.Message); }
    }

    void QuitProfile(ProfileItem item)
    {
        if (ProcessFor(item, ClaudeProcesses.Snapshot()) is { } p) ClaudeProcesses.KillTree(p.Pid);
        DelayedRefresh();
    }

    void ConfirmDelete(ProfileItem item)
    {
        var r = item.Record!;
        ShowConfirm($"Delete “{r.Name}”?",
            $"This removes the profile and moves its data, including its sign-in, chats and settings, to the Recycle Bin." +
            (item.IsRunning ? " It's open right now, so it will be closed first." : ""),
            "Delete profile",
            () =>
            {
                if (item.IsRunning) { QuitProfile(item); Thread.Sleep(1200); }
                try
                {
                    Shortcuts.RemoveForProfile(r);
                    ProfileStore.Delete(_records, r);
                    ItemsFor(item.App).Remove(item);
                    UpdateSections();
                    ShowToast($"Deleted {r.Name}");
                }
                catch (Exception ex) { ShowToast("Couldn't delete: " + ex.Message); }
            });
    }

    void ConfirmReset(ProfileItem item)
    {
        var r = item.Record!;
        ShowConfirm($"Start “{r.Name}” fresh?",
            "This signs the profile out so you can sign in with a different account. Its chats, settings and folders " +
            "are moved to the Recycle Bin, and the profile reopens ready to sign in.",
            "Start fresh",
            () =>
            {
                if (item.IsRunning) { QuitProfile(item); Thread.Sleep(1500); }
                try
                {
                    ProfileStore.Reset(r);
                    item.IsSignedIn = false;
                    item.AccountUuid = null;
                    MarkSharedAccounts();
                    Open(item);
                }
                catch (Exception ex) { ShowToast("Couldn't reset: " + ex.Message); }
            });
    }

    static void Reveal(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    void OpenRoot_Click(object sender, RoutedEventArgs e) => Reveal(AppPaths.Root);

    void GetClaude_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://claude.ai/download") { UseShellExecute = true });

    // ---------- New / edit dialog ----------

    // The header button starts a Claude profile; each section's tile starts one for its own app.
    void NewProfile_Click(object sender, RoutedEventArgs e) =>
        ShowEditDialog(null, (sender as FrameworkElement)?.DataContext is ProfileItem { Kind: ItemKind.AddTile } tile ? tile.App : AppKind.Claude);

    void ShowEditDialog(ProfileRecord? editing, AppKind app = AppKind.Claude)
    {
        _editing = editing;
        EditTitle.Text = editing == null ? "New profile" : "Edit profile";
        AppPicker.Visibility = editing == null && _codexInstalled ? Visibility.Visible : Visibility.Collapsed;
        (app == AppKind.Codex ? AppCodex : AppClaude).IsChecked = true;
        _pickedApp = app;
        UpdateEditSubtitle();
        EditOk.Content = editing == null ? "Create and open" : "Save";
        NameBox.Text = editing?.Name ?? "";
        NameError.Visibility = Visibility.Collapsed;
        ShortcutBox.Visibility = editing == null ? Visibility.Visible : Visibility.Collapsed;
        ShortcutBox.IsChecked = true;

        _pickedColor = editing?.Color ?? ProfileStore.SuggestColor(_records);
        Swatches.Children.Clear();
        foreach (var c in ProfileStore.Palette)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush(Icons.ParseColor(c)),
                GroupName = "swatch",
                IsChecked = string.Equals(c, _pickedColor, StringComparison.OrdinalIgnoreCase),
                ToolTip = c,
            };
            var color = c;
            rb.Checked += (_, _) => _pickedColor = color;
            Swatches.Children.Add(rb);
        }

        EditDialog.Visibility = Visibility.Visible;
        ConfirmDialog.Visibility = Visibility.Collapsed;
        ShowOverlay();
        Dispatcher.BeginInvoke(() => { NameBox.Focus(); NameBox.SelectAll(); }, DispatcherPriority.Input);
    }

    void AppPick_Checked(object sender, RoutedEventArgs e)
    {
        _pickedApp = sender == AppCodex ? AppKind.Codex : AppKind.Claude;
        UpdateEditSubtitle();
    }

    void UpdateEditSubtitle() =>
        EditSubtitle.Text = _editing == null
            ? $"A fresh {_pickedApp} with its own sign-in, chats, folders and settings."
            : "Change how this profile looks. Its sign-in and data stay as they are.";

    void NameBox_TextChanged(object sender, TextChangedEventArgs e) => NameError.Visibility = Visibility.Collapsed;

    void EditOk_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var app = _editing?.App ?? _pickedApp;
        string? error = null;
        if (name.Length == 0) error = "Give the profile a name.";
        else if (name.Equals(ProfileItem.MainName(app), StringComparison.OrdinalIgnoreCase) ||
                 _records.Any(r => r != _editing && r.App == app && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            error = $"You already have a {app} profile with that name.";
        if (error != null)
        {
            NameError.Text = error;
            NameError.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        if (_editing == null)
        {
            var record = ProfileStore.Create(_records, name, _pickedColor, app);
            var item = new ProfileItem(ItemKind.Profile, record);
            var items = ItemsFor(app);
            items.Insert(items.IndexOf(AddTileFor(app)), item);
            UpdateSections();
            if (ShortcutBox.IsChecked == true) CreateShortcutQuietly(record);
            HideOverlay();
            Open(item);
        }
        else
        {
            var oldName = _editing.Name;
            var hadShortcut = Shortcuts.HasDesktopShortcut(_editing);
            _editing.Name = name;
            _editing.Color = _pickedColor;
            ProfileStore.Save(_records);
            if (hadShortcut)
            {
                Shortcuts.RemoveForProfile(_editing, oldName);
                CreateShortcutQuietly(_editing);
            }
            Cards.FirstOrDefault(i => i.Record == _editing)?.Refresh();
            MarkSharedAccounts();
            HideOverlay();
        }
    }

    static void CreateShortcutQuietly(ProfileRecord r)
    {
        try { Shortcuts.CreateForProfile(r); } catch (Exception ex) { Log.Write("shortcut failed: " + ex.Message); }
    }

    // ---------- Confirm dialog ----------

    void ShowConfirm(string title, string body, string action, Action onConfirm)
    {
        ConfirmTitle.Text = title;
        ConfirmBody.Text = body;
        ConfirmOk.Content = action;
        _confirmAction = onConfirm;
        EditDialog.Visibility = Visibility.Collapsed;
        ConfirmDialog.Visibility = Visibility.Visible;
        ShowOverlay();
    }

    void ConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        var action = _confirmAction;
        HideOverlay();
        action?.Invoke();
    }

    // ---------- Overlay + toast ----------

    void ShowOverlay()
    {
        Overlay.Opacity = 0;
        Overlay.Visibility = Visibility.Visible;
        Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
    }

    void HideOverlay()
    {
        Overlay.Visibility = Visibility.Collapsed;
        _confirmAction = null;
    }

    void CloseOverlay_Click(object sender, RoutedEventArgs e) => HideOverlay();
    void Overlay_MouseDown(object sender, MouseButtonEventArgs e) => HideOverlay();
    void Dialog_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Overlay.Visibility == Visibility.Visible) { HideOverlay(); e.Handled = true; }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { ShowEditDialog(null); e.Handled = true; }
    }

    void ShowToast(string text)
    {
        ToastText.Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3400))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3700))));
        Toast.BeginAnimation(OpacityProperty, a);
    }

    // ---------- Window chrome ----------

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void OnStateChanged()
    {
        var maximized = WindowState == WindowState.Maximized;
        Frame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        RootGrid.Margin = maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = maximized ? "" : "";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing hides to the tray so sign-ins keep getting routed.
        e.Cancel = true;
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            ((App)Application.Current).Notify("Still running in the tray",
                "Claude Profiles stays in the tray so sign-ins reach the right profile. Right-click the icon to quit.");
        }
    }

    public void CaptureAndExit(string path, bool withDialog)
    {
        ShowInTaskbar = false;
        Left = -10000;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Show();
        if (withDialog) ShowEditDialog(null);
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            var root = (FrameworkElement)Content;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
            Shutdown();
            Application.Current.Shutdown();
        };
        t.Start();
    }

    public void Shutdown()
    {
        _timer.Stop();
        _router.Dispose();
    }
}
