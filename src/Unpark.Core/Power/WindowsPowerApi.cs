using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Unpark.Core.Power;

/// <summary>Lecture via l'API documentée powrprof.dll (Windows Vista et plus, donc Windows 10 et 11).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPowerApi : IPowerApi
{
    private const uint AccessScheme = 16;
    private const uint ErrorNoMoreItems = 259;
    private const uint AttributeHide = 1;

    public Guid GetActiveScheme()
    {
        var error = PowerGetActiveScheme(IntPtr.Zero, out var ptr);
        if (error != 0) throw new InvalidOperationException($"Plan actif illisible (code {error}).");
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    public IReadOnlyList<Guid> ListSchemes()
    {
        var list = new List<Guid>();
        for (uint index = 0; ; index++)
        {
            var buffer = new byte[16];
            uint size = 16;
            var error = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size);
            if (error == ErrorNoMoreItems) break;
            if (error != 0) throw new InvalidOperationException($"Liste des plans illisible (code {error}).");
            list.Add(new Guid(buffer));
        }
        return list;
    }

    public string? GetSchemeName(Guid scheme)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size) != 0 || size == 0) return null;
        var buffer = new byte[size];
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0) return null;
        return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    public uint? ReadAc(Guid scheme, Guid subGroup, Guid setting) =>
        PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, out var v) == 0 ? v : null;

    public uint? ReadDc(Guid scheme, Guid subGroup, Guid setting) =>
        PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, out var v) == 0 ? v : null;

    public bool IsHidden(Guid subGroup, Guid setting) =>
        (PowerReadSettingAttributes(ref subGroup, ref setting) & AttributeHide) != 0;

    public bool HasBattery()
    {
        if (!GetSystemPowerStatus(out var status)) return false;
        // 128 = pas de batterie, 255 = état inconnu.
        return status.BatteryFlag != 128 && status.BatteryFlag != 255;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid,
        uint accessFlags, uint index, byte[]? buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid,
        IntPtr powerSettingGuid, byte[]? buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
        ref Guid powerSettingGuid, out uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
        ref Guid powerSettingGuid, out uint dcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadSettingAttributes(ref Guid subGroupGuid, ref Guid powerSettingGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
