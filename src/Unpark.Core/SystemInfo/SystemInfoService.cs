using Unpark.Core.Power;
using Unpark.Core.Registry;
using Unpark.Core.Startup;

namespace Unpark.Core.SystemInfo;

/// <summary>Rassemble les infos système affichées par la page « Infos système ».</summary>
public sealed class SystemInfoService(IHardwareInfoApi hardware, IRegistry registry, IPowerApi power, StartupService startup)
{
    public SystemReport Collect()
    {
        string plan;
        try
        {
            var active = power.GetActiveScheme();
            plan = power.GetSchemeName(active) ?? active.ToString();
        }
        catch
        {
            plan = "illisible";
        }
        var entries = startup.List();
        return new SystemReport(
            WindowsVersionInfo.Read(registry),
            hardware.Cpu(),
            hardware.Gpus(),
            hardware.Ram(),
            hardware.Disks(),
            hardware.Volumes(),
            plan,
            entries.Count(e => e.Enabled),
            entries.Count);
    }
}
