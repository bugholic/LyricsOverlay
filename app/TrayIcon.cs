using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using WinForms = System.Windows.Forms;

namespace LyricsOverlay;

sealed class TrayIcon : IDisposable
{
    readonly WinForms.NotifyIcon _icon;
    readonly WinForms.ToolStripMenuItem _nowPlaying;
    readonly WinForms.ToolStripMenuItem _show;
    readonly WinForms.ToolStripMenuItem _edit;

    public TrayIcon(Action openSettings, Action toggleEdit, Action toggleVisible, Action exit)
    {
        _nowPlaying = new WinForms.ToolStripMenuItem("Nothing playing") { Enabled = false };
        _show = new WinForms.ToolStripMenuItem("Show lyrics", null, (_, _) => toggleVisible());
        _edit = new WinForms.ToolStripMenuItem("Move / resize overlay", null, (_, _) => toggleEdit());

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _nowPlaying,
            new WinForms.ToolStripSeparator(),
            _show,
            _edit,
            new WinForms.ToolStripMenuItem("Settings…", null, (_, _) => openSettings()),
            new WinForms.ToolStripSeparator(),
            new WinForms.ToolStripMenuItem("Exit", null, (_, _) => exit()),
        });

        _icon = new WinForms.NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Lyrics Overlay",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
    }

    public void Update(string nowPlaying, bool visible, bool editing)
    {
        _nowPlaying.Text = nowPlaying;
        _show.Checked = visible;
        _edit.Text = editing ? "Finish moving overlay" : "Move / resize overlay";

        var tip = $"Lyrics Overlay — {nowPlaying}";
        _icon.Text = tip.Length > 63 ? tip[..62] + "…" : tip; // NotifyIcon tooltip limit
    }

    static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var background = new SolidBrush(Color.FromArgb(0x7C, 0x4D, 0xFF));
            g.FillEllipse(background, 1, 1, 30, 30);
            using var font = new Font("Segoe UI Symbol", 18, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("♪", font, Brushes.White, new RectangleF(0, 1, 32, 32), format);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
