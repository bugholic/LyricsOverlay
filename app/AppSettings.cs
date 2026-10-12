using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace LyricsOverlay;

public sealed class AppSettings
{
    /// <summary>Opt-in: send title/artist/album/length to LRCLIB when the player has no lyrics.</summary>
    public bool AllowExternalLyrics { get; set; }

    /// <summary>When both exist, use LRCLIB's time-synced lyrics instead of the player's plain ones.</summary>
    public bool PreferSyncedExternal { get; set; }

    public bool BrowsersOnly { get; set; } = true;
    public bool OverlayVisible { get; set; } = true;
    public double FontSize { get; set; } = 34;
    public double Opacity { get; set; } = 0.95;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    [JsonIgnore] public bool IsFirstRun { get; private set; }

    public event Action? Changed;

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricsOverlay");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Corrupt settings file: fall back to defaults rather than refusing to start.
        }
        return new AppSettings { IsFirstRun = true };
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public void NotifyChanged()
    {
        Save();
        Changed?.Invoke();
    }
}

/// <summary>"Start with Windows" via the per-user Run key (no admin needed).</summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "LyricsOverlay";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
