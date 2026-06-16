using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Aoe.Alpha;

/// <summary>Generates small solid-color dot icons so the tray icon is easy to spot and reflects state.</summary>
public static class TrayGraphics
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon MakeDotIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 14, 14);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            // Clone into a self-contained managed Icon so we can free the native handle now.
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
