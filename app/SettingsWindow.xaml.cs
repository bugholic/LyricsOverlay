using System;
using System.Windows;
using System.Windows.Threading;

namespace LyricsOverlay;

public partial class SettingsWindow : Window
{
    readonly AppSettings _settings;
    readonly LyricsController _controller;
    readonly ExtensionBridge _bridge;
    readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _loading = true;

    public SettingsWindow(AppSettings settings, LyricsController controller, ExtensionBridge bridge, OverlayWindow overlay)
    {
        InitializeComponent();
        _settings = settings;
        _controller = controller;
        _bridge = bridge;

        AllowExternalBox.IsChecked = settings.AllowExternalLyrics;
        PreferSyncedBox.IsChecked = settings.PreferSyncedExternal;
        BrowsersOnlyBox.IsChecked = settings.BrowsersOnly;
        ShowOverlayBox.IsChecked = settings.OverlayVisible;
        FontSizeSlider.Value = settings.FontSize;
        OpacitySlider.Value = settings.Opacity;
        StartupBox.IsChecked = StartupRegistration.IsEnabled;
        _loading = false;

        AllowExternalBox.Click += (_, _) => Update(s => s.AllowExternalLyrics = AllowExternalBox.IsChecked == true);
        PreferSyncedBox.Click += (_, _) => Update(s => s.PreferSyncedExternal = PreferSyncedBox.IsChecked == true);
        BrowsersOnlyBox.Click += (_, _) => Update(s => s.BrowsersOnly = BrowsersOnlyBox.IsChecked == true);
        ShowOverlayBox.Click += (_, _) => Update(s => s.OverlayVisible = ShowOverlayBox.IsChecked == true);
        FontSizeSlider.ValueChanged += (_, e) => Update(s => s.FontSize = Math.Round(e.NewValue));
        OpacitySlider.ValueChanged += (_, e) => Update(s => s.Opacity = Math.Round(e.NewValue, 2));
        StartupBox.Click += (_, _) => StartupRegistration.Set(StartupBox.IsChecked == true);
        EditOverlayButton.Click += (_, _) => overlay.SetEditing(true);
        CloseButton.Click += (_, _) => Close();

        _controller.Changed += RefreshStatus;
        _refresh.Tick += (_, _) => RefreshStatus();
        _refresh.Start();
        Closed += (_, _) =>
        {
            _refresh.Stop();
            _controller.Changed -= RefreshStatus;
        };
        RefreshStatus();
    }

    void Update(Action<AppSettings> change)
    {
        if (_loading) return;
        change(_settings);
        _settings.NotifyChanged();
        RefreshStatus();
    }

    void RefreshStatus()
    {
        PreferSyncedBox.IsEnabled = AllowExternalBox.IsChecked == true;
        FontSizeValue.Text = $"{FontSizeSlider.Value:0}";
        OpacityValue.Text = $"{OpacitySlider.Value * 100:0}%";

        var track = _controller.Track;
        NowPlayingText.Text = track is null
            ? "Nothing playing in a browser right now."
            : string.IsNullOrWhiteSpace(track.Artist) ? track.Title : $"{track.Title} — {track.Artist}";

        var doc = _controller.Lyrics;
        LyricsSourceText.Text = track is null ? ""
            : doc is null ? _controller.Status
            : doc.TimingEstimated ? $"Lyrics from {doc.Source} (plain text, timing estimated)"
            : $"Lyrics from {doc.Source} (time-synced)";

        ExtensionStatusText.Text =
            !_bridge.IsListening ? $"⚠ Couldn't open localhost:{ExtensionBridge.Port}. Another program may be using that port."
            : _bridge.IsConnected ? $"✔ Browser extension connected ({_bridge.LastSource})"
            : "Browser extension not detected. Install it and open music.youtube.com (see README).";
    }
}
