namespace Unpark.Core.Diagnostics;

// Données brutes lues sur le PC (en lecture seule). Chaque champ peut être absent si la lecture a échoué.

public sealed record RamModule(long CapacityBytes, int RatedMts, int ConfiguredMts, int SmbiosType, string Locator, string Bank, string PartNumber);

public sealed record RamData(IReadOnlyList<RamModule> Modules, long TotalBytes, long AvailableBytes);

public sealed record DisplayData(string Name, int Width, int Height, int CurrentHz, int MaxHz);

public sealed record NvidiaData(int? TemperatureC, int? PcieGenCurrent, int? PcieGenMax, int? PcieWidthCurrent, int? PcieWidthMax, long? Bar1TotalMiB);

public sealed record GpuData(string Name, string DriverVersion, DateTime? DriverDate, NvidiaData? Nvidia, bool NvidiaSmiMissing);

public sealed record StabilityData(int Whea, DateTime? LastWhea, int KernelPower41, DateTime? LastKernelPower41, int BugChecks, DateTime? LastBugCheck);

public sealed record DiskData(string Model, string Media, string Health, IReadOnlyList<string> DriveLetters);

public sealed record VolumeData(string Letter, long TotalBytes, long FreeBytes);

public sealed record GameFolder(string Launcher, string Path);

public sealed record ProcessUsage(string Name, double Percent);

public sealed record WindowsData(
    string ActivePlan,
    bool? GameMode,
    bool? Hags,
    bool? MemoryIntegrity,
    int StartupEnabled,
    double? IdleCpuPercent,
    IReadOnlyList<ProcessUsage> TopProcesses,
    bool IsLaptop,
    int AppsOnHighPerformanceGpu);

public sealed record NetworkData(string Target, int Sent, int Received, double? AverageMs, double? JitterMs, double? GatewayAverageMs, bool IsWifi);

public sealed record DiagnosticData(
    DateTime Now,
    string WindowsVersion,
    RamData? Ram,
    IReadOnlyList<DisplayData> Displays,
    IReadOnlyList<GpuData> Gpus,
    StabilityData? Stability,
    IReadOnlyList<DiskData> Disks,
    IReadOnlyList<VolumeData> Volumes,
    IReadOnlyList<GameFolder> GameFolders,
    WindowsData? Windows,
    NetworkData? Network);

// Résultat de l'analyse.

public enum DiagStatus { Green, Orange, Red, Info }

/// <summary>Page d'Unpark (ou de Windows) qui corrige le problème.</summary>
public enum DiagFix { None, PowerPlan, Gaming, Startup, Cleanup, DisplaySettings, GraphicsSettings, WindowsUpdate }

public sealed record DiagCheck(string Title, DiagStatus Status, string Value, string Explanation, DiagFix Fix = DiagFix.None);

public sealed record DiagSection(string Title, IReadOnlyList<DiagCheck> Checks);
