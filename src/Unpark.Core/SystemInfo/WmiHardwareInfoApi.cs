using System.Management;
using System.Runtime.Versioning;

namespace Unpark.Core.SystemInfo;

/// <summary>Matériel via WMI (classes standard de Windows 10 et 11). Chaque lecture qui échoue renvoie un résultat vide.</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiHardwareInfoApi : IHardwareInfoApi
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public CpuInfo? Cpu() => Query(@"root\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor")
        .Select(o => new CpuInfo(Str(o["Name"]).Trim(), Int(o["NumberOfCores"]), Int(o["NumberOfLogicalProcessors"]), Int(o["MaxClockSpeed"])))
        .FirstOrDefault();

    public IReadOnlyList<GpuInfo> Gpus() => Query(@"root\cimv2", "SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController")
        .Select(o =>
        {
            var name = Str(o["Name"]);
            // AdapterRAM est limité à 4 Go : la vraie valeur est dans le registre du pilote.
            var vram = VramFromRegistry(name) ?? (o["AdapterRAM"] is uint ram && ram > 0 ? ram : (long?)null);
            return new GpuInfo(name, Str(o["DriverVersion"]), vram);
        })
        .Where(g => !g.Name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase))
        .ToList();

    public RamInfo? Ram()
    {
        var total = Query(@"root\cimv2", "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")
            .Select(o => Long(o["TotalPhysicalMemory"])).FirstOrDefault();
        var modules = Query(@"root\cimv2", "SELECT Capacity, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory").ToList();
        if (total == 0 && modules.Count == 0) return null;
        var speed = modules.Select(m => Int(m["ConfiguredClockSpeed"]) is > 0 and var s ? s : Int(m["Speed"])).DefaultIfEmpty(0).Max();
        return new RamInfo(total > 0 ? total : modules.Sum(m => Long(m["Capacity"])), modules.Count, speed);
    }

    public IReadOnlyList<DiskInfo> Disks() =>
        Query(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk")
            .Select(o =>
            {
                var media = Int(o["MediaType"]);
                var bus = Int(o["BusType"]);
                var type = media switch
                {
                    4 => bus == 17 ? "SSD NVMe" : "SSD",
                    3 => "HDD (disque dur)",
                    5 => "SCM",
                    _ => bus == 17 ? "SSD NVMe" : bus == 7 ? "Disque USB" : "Type inconnu",
                };
                return new DiskInfo(Str(o["FriendlyName"]), type, Long(o["Size"]));
            })
            .ToList();

    public IReadOnlyList<VolumeInfo> Volumes() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new VolumeInfo(d.Name.TrimEnd('\\'), d.VolumeLabel, d.TotalSize, d.AvailableFreeSpace))
            .ToList();

    private static long? VramFromRegistry(string gpuName)
    {
        try
        {
            using var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(DisplayClass);
            if (cls is null) return null;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var key = cls.OpenSubKey(sub);
                if (key?.GetValue("DriverDesc") as string != gpuName) continue;
                return key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long q when q > 0 => q,
                    byte[] { Length: 8 } b => BitConverter.ToInt64(b),
                    byte[] { Length: 4 } b => (long)BitConverter.ToUInt32(b),
                    _ => (long?)null,
                };
            }
        }
        catch
        {
            // Registre illisible : on garde la valeur WMI.
        }
        return null;
    }

    private static IEnumerable<ManagementBaseObject> Query(string scope, string wql)
    {
        List<ManagementBaseObject> list;
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            list = searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch
        {
            list = [];
        }
        return list;
    }

    private static string Str(object? v) => v?.ToString() ?? "";
    private static int Int(object? v) => v is null ? 0 : (int)Convert.ToInt64(v);
    private static long Long(object? v) => v is null ? 0 : Convert.ToInt64(v);
}
