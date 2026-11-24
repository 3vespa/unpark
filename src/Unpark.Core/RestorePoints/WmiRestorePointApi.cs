using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Unpark.Core.RestorePoints;

/// <summary>
/// Restauration système de Windows 10 et 11.
/// Création : WMI (classe SystemRestore de root\default) avec des paramètres typés exactement comme Windows les attend ;
/// en secours, l'API documentée SRSetRestorePointW (srclient.dll), début puis fin de modification.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiRestorePointApi : IRestorePointApi
{
    private const string Scope = @"\\.\root\default";
    /// <summary>MODIFY_SETTINGS : modification de réglages système.</summary>
    private const uint ModifySettings = 12;
    private const uint BeginSystemChange = 100;
    private const uint EndSystemChange = 101;

    public DateTime? GetLatestUtc()
    {
        using var searcher = new ManagementObjectSearcher(Scope, "SELECT CreationTime FROM SystemRestore");
        DateTime? latest = null;
        foreach (var obj in searcher.Get())
        {
            using (obj)
            {
                if (obj["CreationTime"] is string wmiDate)
                {
                    var utc = ManagementDateTimeConverter.ToDateTime(wmiDate).ToUniversalTime();
                    if (latest is null || utc > latest) latest = utc;
                }
            }
        }
        return latest;
    }

    public uint Create(string description)
    {
        using var cls = new ManagementClass(new ManagementScope(Scope), new ManagementPath("SystemRestore"), null);
        using var input = cls.GetMethodParameters("CreateRestorePoint");
        input["Description"] = description;
        input["RestorePointType"] = ModifySettings;
        input["EventType"] = BeginSystemChange;
        using var output = cls.InvokeMethod("CreateRestorePoint", input, null);
        return Convert.ToUInt32(output["ReturnValue"]);
    }

    public uint CreateFallback(string description)
    {
        var begin = new RestorePointInfo
        {
            EventType = (int)BeginSystemChange,
            RestorePointType = (int)ModifySettings,
            SequenceNumber = 0,
            Description = description.Length > 255 ? description[..255] : description,
        };
        if (!SRSetRestorePointW(ref begin, out var status))
            return status.Status != 0 ? (uint)status.Status : (uint)Marshal.GetLastWin32Error();

        // Fin de la « modification » : le point de restauration est marqué comme terminé.
        var end = new RestorePointInfo
        {
            EventType = (int)EndSystemChange,
            RestorePointType = (int)ModifySettings,
            SequenceNumber = status.SequenceNumber,
            Description = "",
        };
        SRSetRestorePointW(ref end, out _);
        return 0;
    }

    // Structures de srrestoreptapi.h (alignées sur 1 octet).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct RestorePointInfo
    {
        public int EventType;
        public int RestorePointType;
        public long SequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct StateMgrStatus
    {
        public int Status;
        public long SequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(ref RestorePointInfo info, out StateMgrStatus status);
}
