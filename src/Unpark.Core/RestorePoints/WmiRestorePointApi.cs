using System.Management;
using System.Runtime.Versioning;

namespace Unpark.Core.RestorePoints;

/// <summary>Restauration système via WMI (classe SystemRestore de root\default), disponible sur Windows 10 et 11.</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiRestorePointApi : IRestorePointApi
{
    private const string Scope = @"\\.\root\default";
    /// <summary>MODIFY_SETTINGS : modification de réglages système.</summary>
    private const int RestorePointType = 12;
    /// <summary>BEGIN_SYSTEM_CHANGE.</summary>
    private const int EventType = 100;

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
        using var cls = new ManagementClass(Scope, "SystemRestore", null);
        var result = cls.InvokeMethod("CreateRestorePoint", [description, RestorePointType, EventType]);
        return result is uint code ? code : Convert.ToUInt32(result);
    }
}
