using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace Unpark.Core.Cleanup;

[SupportedOSPlatform("windows")]
public sealed class WindowsCleanupSystem : ICleanupSystem
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);
    private const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;
    /// <summary>Renvoyé par SHEmptyRecycleBin quand la corbeille est déjà vide.</summary>
    private const int EUnexpected = unchecked((int)0x8000FFFF);

    public (long Bytes, long Items) QueryRecycleBin()
    {
        var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
        var hr = SHQueryRecycleBin(null, ref info);
        if (hr != 0) throw new InvalidOperationException($"Corbeille illisible (0x{hr:X8}).");
        return (info.TotalBytes, info.NumItems);
    }

    public void EmptyRecycleBin()
    {
        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        if (hr != 0 && hr != EUnexpected) throw new InvalidOperationException($"Windows a refusé de vider la corbeille (0x{hr:X8}).");
    }

    public bool IsServiceRunning(string name)
    {
        using var sc = new ServiceController(name);
        return sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
    }

    public void StopService(string name)
    {
        using var sc = new ServiceController(name);
        if (sc.Status == ServiceControllerStatus.Stopped) return;
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
    }

    public void StartService(string name)
    {
        using var sc = new ServiceController(name);
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long TotalBytes;
        public long NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}
