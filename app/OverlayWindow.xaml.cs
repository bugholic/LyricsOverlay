using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LyricsOverlay;

/// <summary>
/// The karaoke strip. Click-through and focus-less, except in edit mode where it can be dragged and resized.
/// </summary>
public partial class OverlayWindow : Window
{
    // Show a line slightly before it's sung, so the eye gets there in time.
    static readonly TimeSpan LeadIn = TimeSpan.FromMilliseconds(250);
    static readonly Brush EditBackground = new SolidColorBrush(Color.FromArgb(0xB0, 0x12, 0x12, 0x1A));
    static readonly Brush EditBorder = new SolidColorBrush(Color.FromRgb(0x7C, 0x4D, 0xFF));

    readonly AppSettings _settings;
    readonly LyricsController _controller;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    LyricsDoc? _shownDoc;
    int _shownIndex = int.MinValue;

    public bool IsEditing { get; private set; }

    public OverlayWindow(AppSettings settings, LyricsController controller)
    {
        InitializeComponent();
        _settings = settings;
        _controller = controller;

        PlaceWindow();
        ApplyAppearance();

        SourceInitialized += (_, _) => Native.SetClickThrough(this, true);
        _settings.Changed += ApplyAppearance;
        _controller.Changed += () => Render(force: true);
        _tick.Tick += (_, _) => Render(force: false);
        _tick.Start();

        MouseLeftButtonDown += (_, e) =>
        {
            if (IsEditing && e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        KeyDown += (_, e) =>
        {
            if (IsEditing && e.Key == Key.Escape) SetEditing(false);
        };
    }

    void PlaceWindow()
    {
        var work = SystemParameters.WorkArea;
        Width = _settings.Width ?? Math.Min(1000, work.Width * 0.6);
        Height = _settings.Height ?? 240;
        Left = _settings.Left ?? work.Left + (work.Width - Width) / 2;
        Top = _settings.Top ?? work.Bottom - Height - 40;

        // A saved position on a monitor that's no longer connected would leave the overlay unreachable.
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screens.IntersectsWith(new Rect(Left, Top, Width, Height)))
        {
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Bottom - Height - 40;
        }
    }

    void ApplyAppearance()
    {
        CurrentLine.FontSize = _settings.FontSize;
        PrevLine.FontSize = NextLine.FontSize = NextLine2.FontSize = Math.Round(_settings.FontSize * 0.62);
        SyncVisibility();
        Render(force: true);
    }

    public void SyncVisibility()
    {
        if (IsEditing) return;
        if (_settings.OverlayVisible && !IsVisible) Show();
        else if (!_settings.OverlayVisible && IsVisible) Hide();
    }

    void Render(bool force)
    {
        var doc = _controller.Lyrics;
        var track = _controller.Track;

        var paused = track is not null && !_controller.IsPlaying;
        LinesPanel.Opacity = _settings.Opacity * (paused ? 0.45 : 1);

        StatusText.Text = doc is null ? _controller.Status : IsEditing ? SourceLabel(doc) : "";

        if (doc is null || doc.Lines.Count == 0)
        {
            if (force || _shownDoc is not null)
            {
                _shownDoc = null;
                _shownIndex = int.MinValue;
                PrevLine.Text = NextLine.Text = NextLine2.Text = "";
                CurrentLine.Text = IsEditing && track is null ? "Lyrics will appear here" : "";
            }
            return;
        }

        var index = IndexAt(doc.Lines, _controller.CurrentPosition() + LeadIn);
        if (!force && index == _shownIndex && ReferenceEquals(doc, _shownDoc)) return;

        var advanced = ReferenceEquals(doc, _shownDoc) && index == _shownIndex + 1;
        _shownDoc = doc;
        _shownIndex = index;

        PrevLine.Text = LineText(doc, index - 1);
        CurrentLine.Text = index < 0 ? "♪" : LineText(doc, index);
        NextLine.Text = LineText(doc, index + 1);
        NextLine2.Text = LineText(doc, index + 2);

        if (advanced) AnimateAdvance();
    }

    static string SourceLabel(LyricsDoc doc) =>
        doc.TimingEstimated ? $"Lyrics from {doc.Source} · plain text, timing estimated"
            : $"Lyrics from {doc.Source} · time-synced";

    static string LineText(LyricsDoc doc, int i)
    {
        if (i < 0 || i >= doc.Lines.Count) return "";
        var text = doc.Lines[i].Text;
        return string.IsNullOrWhiteSpace(text) ? "♪" : text; // blank synced lines are instrumental breaks
    }

    /// <summary>Last line whose timestamp has passed, or -1 before the first line.</summary>
    static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].Time <= position) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    void AnimateAdvance()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(280);
        Slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(PrevLine.ActualHeight * 0.8, 0, duration) { EasingFunction = ease });
        CurrentLine.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, duration) { EasingFunction = ease });
    }

    public void SetEditing(bool on)
    {
        if (IsEditing == on) return;
        IsEditing = on;

        Native.SetClickThrough(this, !on);
        ResizeMode = on ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
        Frame.Background = on ? EditBackground : Brushes.Transparent;
        Frame.BorderBrush = on ? EditBorder : Brushes.Transparent;
        EditBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        if (on)
        {
            if (!IsVisible) Show();
            Activate();
        }
        else
        {
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Width = Width;
            _settings.Height = Height;
            _settings.NotifyChanged(); // also re-hides the overlay if it's switched off
        }
        Render(force: true);
    }

    void ChangeFontSize(double delta)
    {
        _settings.FontSize = Math.Clamp(_settings.FontSize + delta, 16, 96);
        _settings.NotifyChanged();
    }

    void Smaller_Click(object sender, RoutedEventArgs e) => ChangeFontSize(-2);
    void Bigger_Click(object sender, RoutedEventArgs e) => ChangeFontSize(2);
    void Done_Click(object sender, RoutedEventArgs e) => SetEditing(false);
}
