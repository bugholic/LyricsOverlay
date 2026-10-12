using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace LyricsOverlay;

public partial class App : Application
{
    Mutex? _singleInstance;
    AppSettings _settings = null!;
    ExtensionBridge _bridge = null!;
    MediaWatcher _media = null!;
    LyricsController _controller = null!;
    OverlayWindow _overlay = null!;
    TrayIcon _tray = null!;
    SettingsWindow? _settingsWindow;
    readonly DispatcherTimer _trayRefresh = new() { Interval = TimeSpan.FromSeconds(1) };

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "LyricsOverlay.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Lyrics Overlay is already running. Look for the ♪ icon in the system tray.",
                "Lyrics Overlay", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();
        _bridge = new ExtensionBridge(Dispatcher);
        _bridge.Start();
        _media = new MediaWatcher(_settings);
        _controller = new LyricsController(_settings, _media, _bridge);
        _overlay = new OverlayWindow(_settings, _controller);
        _overlay.SyncVisibility();

        _tray = new TrayIcon(OpenSettings, ToggleEdit, ToggleVisible, ExitApp);
        _trayRefresh.Tick += (_, _) => RefreshTray();
        _trayRefresh.Start();

        try
        {
            await _media.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't connect to Windows media sessions:\n{ex.Message}", "Lyrics Overlay",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        if (_settings.IsFirstRun)
        {
            _settings.Save();
            OpenSettings(); // first run: let the user decide about external lyrics up front
        }
    }

    void RefreshTray()
    {
        var track = _controller.Track;
        var text = track is null ? "Nothing playing"
            : string.IsNullOrWhiteSpace(track.Artist) ? track.Title : $"{track.Title} — {track.Artist}";
        _tray.Update(text, _settings.OverlayVisible, _overlay.IsEditing);
    }

    void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, _controller, _bridge, _overlay);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    void ToggleEdit() => _overlay.SetEditing(!_overlay.IsEditing);

    void ToggleVisible()
    {
        _settings.OverlayVisible = !_settings.OverlayVisible;
        _settings.NotifyChanged();
        RefreshTray();
    }

    void ExitApp()
    {
        if (_overlay.IsEditing) _overlay.SetEditing(false); // keep the new position
        _tray.Dispose();
        _bridge.Dispose();
        Shutdown();
    }
}
