using System.Text;
using Unpark.Core.Registry;

namespace Unpark.Core.SystemInfo;

public sealed record CpuInfo(string Name, int Cores, int Threads, int MaxMhz);
public sealed record GpuInfo(string Name, string Driver, long? VramBytes);
public sealed record RamInfo(long TotalBytes, int Modules, int SpeedMhz);
public sealed record DiskInfo(string Model, string Type, long SizeBytes);
public sealed record VolumeInfo(string Letter, string Label, long TotalBytes, long FreeBytes)
{
    public double UsedRatio => TotalBytes == 0 ? 0 : 1 - (double)FreeBytes / TotalBytes;
}

/// <summary>Matériel (WMI). Remplacé par une fausse version dans les tests.</summary>
public interface IHardwareInfoApi
{
    CpuInfo? Cpu();
    IReadOnlyList<GpuInfo> Gpus();
    RamInfo? Ram();
    IReadOnlyList<DiskInfo> Disks();
    IReadOnlyList<VolumeInfo> Volumes();
}

public sealed record WindowsVersionInfo(string Name, string Edition, string DisplayVersion, int Build, int Ubr)
{
    public override string ToString() =>
        $"{Name} {Edition} {DisplayVersion}".Replace("  ", " ").Trim() + $" (build {Build}{(Ubr > 0 ? $".{Ubr}" : "")})";

    /// <summary>
    /// Lit la version dans le registre. Piège connu : ProductName indique « Windows 10 » même sur Windows 11,
    /// on se fie donc au numéro de build (22000 et plus = Windows 11).
    /// </summary>
    public static WindowsVersionInfo Read(IRegistry registry)
    {
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        string? Text(string name) => registry.Get(new RegPath(RegHive.LocalMachine, key, name))?.Text;
        var build = int.TryParse(Text("CurrentBuild") ?? Text("CurrentBuildNumber"), out var b) ? b : Environment.OSVersion.Version.Build;
        var ubr = (int)(registry.Get(new RegPath(RegHive.LocalMachine, key, "UBR"))?.Number ?? 0);
        var name = build >= 22000 ? "Windows 11" : "Windows 10";
        // « Windows 10 Pro » → « Pro » : l'édition sans le nom (faux sur Windows 11).
        var product = Text("ProductName") ?? "";
        var edition = product.Replace("Windows 10", "").Replace("Windows 11", "").Trim();
        return new WindowsVersionInfo(name, edition, Text("DisplayVersion") ?? Text("ReleaseId") ?? "", build, ubr);
    }
}

public sealed record SystemReport(
    WindowsVersionInfo Windows,
    CpuInfo? Cpu,
    IReadOnlyList<GpuInfo> Gpus,
    RamInfo? Ram,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<VolumeInfo> Volumes,
    string ActivePowerPlan,
    int StartupEnabled,
    int StartupTotal)
{
    /// <summary>Résumé texte (à coller sur Discord pour demander de l'aide).</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppInfo.Name} {AppInfo.DisplayVersion} — infos système");
        sb.AppendLine($"Windows : {Windows}");
        if (Cpu is { } c) sb.AppendLine($"Processeur : {c.Name} ({c.Cores} cœurs / {c.Threads} threads, {c.MaxMhz} MHz)");
        foreach (var g in Gpus)
            sb.AppendLine($"Carte graphique : {g.Name}{(g.VramBytes is { } v ? $", {Format.Bytes(v)}" : "")} (pilote {g.Driver})");
        if (Ram is { } r) sb.AppendLine($"Mémoire : {Format.Bytes(r.TotalBytes)} ({r.Modules} barrette(s){(r.SpeedMhz > 0 ? $", {r.SpeedMhz} MHz" : "")})");
        foreach (var d in Disks) sb.AppendLine($"Disque : {d.Model} — {d.Type}, {Format.Bytes(d.SizeBytes)}");
        foreach (var v in Volumes) sb.AppendLine($"Lecteur {v.Letter} : {Format.Bytes(v.FreeBytes)} libres sur {Format.Bytes(v.TotalBytes)}");
        sb.AppendLine($"Plan d'alimentation actif : {ActivePowerPlan}");
        sb.AppendLine($"Programmes au démarrage : {StartupEnabled} activé(s) sur {StartupTotal}");
        return sb.ToString().TrimEnd();
    }
}
