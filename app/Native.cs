using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LyricsOverlay;

static class Native
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// Click-through: mouse input falls to whatever is underneath and the window never takes focus.
    /// Tool-window style keeps it out of Alt+Tab.
    /// </summary>
    public static void SetClickThrough(Window window, bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
        style = enabled
            ? style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
            : style & ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }
}
