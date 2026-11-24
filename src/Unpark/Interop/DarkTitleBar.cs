using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Unpark.Interop;

/// <summary>Barre de titre en mode sombre via l'API DWM documentée (DWMWA_USE_IMMERSIVE_DARK_MODE).</summary>
internal static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    /// <summary>Ancienne valeur, utilisée par Windows 10 avant la build 18985.</summary>
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var on = 1;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
    }
}
