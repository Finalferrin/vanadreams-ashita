using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Vanadreams.Services
{
    /// <summary>The primary screen's size in real pixels, whatever Windows' display scaling is.</summary>
    public static class ScreenSize
    {
        private const int DESKTOPVERTRES = 117;
        private const int DESKTOPHORZRES = 118;

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);

        public static Size PrimaryPixels()
        {
            var dc = GetDC(IntPtr.Zero);
            try
            {
                int w = GetDeviceCaps(dc, DESKTOPHORZRES), h = GetDeviceCaps(dc, DESKTOPVERTRES);
                return w > 0 && h > 0 ? new Size(w, h) : new Size(1920, 1080);
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }
    }
}
