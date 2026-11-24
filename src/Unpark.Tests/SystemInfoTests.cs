using Unpark.Core.Registry;
using Unpark.Core.SystemInfo;

namespace Unpark.Tests;

public class SystemInfoTests
{
    private const string Key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private static FakeRegistry Reg(string product, string build, string display, uint ubr)
    {
        var reg = new FakeRegistry();
        reg.Values[new RegPath(RegHive.LocalMachine, Key, "ProductName")] = RegValue.String(product);
        reg.Values[new RegPath(RegHive.LocalMachine, Key, "CurrentBuild")] = RegValue.String(build);
        reg.Values[new RegPath(RegHive.LocalMachine, Key, "DisplayVersion")] = RegValue.String(display);
        reg.Values[new RegPath(RegHive.LocalMachine, Key, "UBR")] = RegValue.DWord(ubr);
        return reg;
    }

    [Fact]
    public void Windows_11_reconnu_malgre_ProductName_Windows_10()
    {
        var v = WindowsVersionInfo.Read(Reg("Windows 10 Pro", "22631", "23H2", 4460));
        Assert.Equal("Windows 11 Pro 23H2 (build 22631.4460)", v.ToString());
    }

    [Fact]
    public void Windows_10_22H2()
    {
        var v = WindowsVersionInfo.Read(Reg("Windows 10 Home", "19045", "22H2", 5131));
        Assert.Equal("Windows 10 Home 22H2 (build 19045.5131)", v.ToString());
    }

    [Fact]
    public void Resume_texte_pour_Discord()
    {
        var report = new SystemReport(
            WindowsVersionInfo.Read(Reg("Windows 10 Pro", "22631", "23H2", 4460)),
            new CpuInfo("AMD Ryzen 7 5800X3D 8-Core Processor", 8, 16, 3400),
            [new GpuInfo("NVIDIA GeForce RTX 4070", "32.0.15.6614", 12L * 1024 * 1024 * 1024)],
            new RamInfo(32L * 1024 * 1024 * 1024, 2, 3600),
            [new DiskInfo("Samsung SSD 980 PRO 1TB", "SSD NVMe", 1_000_204_886_016)],
            [new VolumeInfo("C:", "", 999_000_000_000, 400_000_000_000)],
            "Unpark Performance", 5, 12);
        var text = report.ToText();
        Assert.Contains("Windows : Windows 11 Pro 23H2", text);
        Assert.Contains("NVIDIA GeForce RTX 4070, 12 Go", text);
        Assert.Contains("Mémoire : 32 Go (2 barrette(s), 3600 MHz)", text);
        Assert.Contains("SSD NVMe, 932 Go", text);
        Assert.Contains("Programmes au démarrage : 5 activé(s) sur 12", text);
    }
}
