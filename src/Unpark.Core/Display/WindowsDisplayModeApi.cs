using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Unpark.Core.Diagnostics;

namespace Unpark.Core.Display;

/// <summary>Écrans et fréquences via les API Windows documentées (EnumDisplaySettings / ChangeDisplaySettingsEx).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDisplayModeApi : IDisplayModeApi
{
    public IReadOnlyList<DisplayData> Displays()
    {
        var list = new List<DisplayData>();
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device.Size = Marshal.SizeOf<DisplayDevice>())
        {
            if ((device.StateFlags & AttachedToDesktop) == 0) continue;
            var current = NewDevMode();
            if (!EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref current)) continue;
            var max = MaxHz(device.DeviceName, current);
            list.Add(new DisplayData($"Écran {list.Count + 1}", current.PelsWidth, current.PelsHeight, current.DisplayFrequency, max, device.DeviceName));
        }
        return list;
    }

    /// <summary>Fréquence la plus haute à la résolution et à la profondeur de couleur actuelles (modes entrelacés exclus).</summary>
    private static int MaxHz(string deviceName, DevMode current)
    {
        var max = current.DisplayFrequency;
        var mode = NewDevMode();
        for (var m = 0; EnumDisplaySettings(deviceName, m, ref mode); m++)
        {
            if (mode.PelsWidth == current.PelsWidth && mode.PelsHeight == current.PelsHeight && mode.BitsPerPel == current.BitsPerPel
                && (mode.DisplayFlags & DmInterlaced) == 0 && mode.DisplayFrequency > max)
                max = mode.DisplayFrequency;
        }
        return max;
    }

    public void SetRefreshRate(string deviceName, int width, int height, int hz)
    {
        var mode = NewDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
            throw new InvalidOperationException("Écran introuvable : est-il toujours branché ?");
        if (mode.PelsWidth != width || mode.PelsHeight != height)
            throw new InvalidOperationException($"La résolution de l'écran a changé ({mode.PelsWidth}×{mode.PelsHeight}) : relance le diagnostic.");
        if (mode.DisplayFrequency == hz) return;
        if (!ModeExists(deviceName, mode, hz))
            throw new InvalidOperationException($"{hz} Hz n'est pas proposé par cet écran en {width}×{height}.");

        mode.DisplayFrequency = hz;
        mode.Fields = DmPelsWidth | DmPelsHeight | DmBitsPerPel | DmDisplayFrequency;
        // Essai d'abord (rien n'est changé), puis changement enregistré pour la session Windows.
        var test = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (test != DispChangeSuccessful) throw new InvalidOperationException(Explain(test));
        var result = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
        if (result != DispChangeSuccessful) throw new InvalidOperationException(Explain(result));
    }

    private static bool ModeExists(string deviceName, DevMode current, int hz)
    {
        var mode = NewDevMode();
        for (var m = 0; EnumDisplaySettings(deviceName, m, ref mode); m++)
            if (mode.PelsWidth == current.PelsWidth && mode.PelsHeight == current.PelsHeight && mode.BitsPerPel == current.BitsPerPel
                && (mode.DisplayFlags & DmInterlaced) == 0 && mode.DisplayFrequency == hz) return true;
        return false;
    }

    private static string Explain(int code) => code switch
    {
        DispChangeRestart => "Windows demande un redémarrage pour changer la fréquence : rien n'a été changé.",
        -2 => "Windows refuse cette fréquence avec la résolution actuelle (mode non pris en charge).",
        -3 => "Windows n'a pas pu enregistrer le réglage.",
        _ => $"Windows a refusé le changement (code {code}).",
    };

    // ---------- API Windows (documentées) ----------

    private const int EnumCurrentSettings = -1;
    private const int AttachedToDesktop = 0x1;
    private const int DmInterlaced = 0x2;
    private const int DmBitsPerPel = 0x40000;
    private const int DmPelsWidth = 0x80000;
    private const int DmPelsHeight = 0x100000;
    private const int DmDisplayFrequency = 0x400000;
    private const uint CdsUpdateRegistry = 0x1;
    private const uint CdsTest = 0x2;
    private const int DispChangeSuccessful = 0;
    private const int DispChangeRestart = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields;
        public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    private static DevMode NewDevMode() => new() { Size = (short)Marshal.SizeOf<DevMode>() };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string deviceName, ref DevMode devMode, IntPtr hwnd, uint flags, IntPtr param);
}
