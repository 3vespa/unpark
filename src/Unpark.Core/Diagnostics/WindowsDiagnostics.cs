using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Unpark.Core.Commands;
using Unpark.Core.Logging;
using Unpark.Core.Power;
using Unpark.Core.Registry;
using Unpark.Core.Startup;
using Unpark.Core.SystemInfo;

namespace Unpark.Core.Diagnostics;

/// <summary>
/// Lecture des infos du diagnostic sur un vrai Windows. LECTURE SEULE : rien n'est modifié.
/// Chaque partie est protégée : une lecture qui échoue donne « illisible » sans arrêter le diagnostic.
/// Aucun pilote noyau n'est utilisé (d'où la température du processeur « non disponible »).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiagnostics(
    IRegistry registry, IPowerApi power, StartupService startup, ICommandRunner commands, IActionLog log)
{
    private const string PingTarget = "1.1.1.1";
    private static readonly TimeSpan CpuSample = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetworkSample = TimeSpan.FromSeconds(20);

    /// <summary>Lance le diagnostic complet (≈ 35 s : mesures processeur et réseau en parallèle).</summary>
    public async Task<DiagnosticData> RunAsync(IProgress<string> progress, CancellationToken cancel = default)
    {
        log.Info("Diagnostic : début (lecture seule)");
        // Le réseau se mesure pendant les lectures ; le processeur est mesuré APRÈS, quand Unpark ne lit plus rien.
        var netTask = Task.Run(() => MeasureNetwork(cancel), cancel);

        progress.Report("Lecture de la mémoire, de l'écran et de la carte graphique…");
        var ram = await Task.Run(() => Safe("RAM", ReadRam), cancel);
        var displays = await Task.Run(() => Safe("écran", ReadDisplays) ?? [], cancel);
        var gpus = await Task.Run(() => Safe("carte graphique", ReadGpus) ?? [], cancel);
        progress.Report("Lecture du journal des erreurs (30 jours) et des disques…");
        var stability = await Task.Run(() => Safe("journal", ReadStability), cancel);
        var disks = await Task.Run(() => Safe("disques", ReadDisks) ?? [], cancel);
        var volumes = await Task.Run(() => Safe("lecteurs", ReadVolumes) ?? [], cancel);
        var games = await Task.Run(() => Safe("bibliothèques de jeux", ReadGameFolders) ?? [], cancel);
        var version = Safe("version de Windows", () => WindowsVersionInfo.Read(registry).ToString()) ?? Environment.OSVersion.VersionString;

        progress.Report("Mesure du processeur au repos (30 s)… ne touche à rien pendant ce temps.");
        var (cpu, top) = await Task.Run(() => MeasureIdleCpu(cancel), cancel);
        var network = await netTask;
        var windows = Safe("Windows", () => ReadWindows(cpu, top));

        log.Info("Diagnostic : terminé");
        return new DiagnosticData(DateTime.Now, version, ram, displays, gpus, stability, disks, volumes, games, windows, network);
    }

    private T? Safe<T>(string what, Func<T> read) where T : class
    {
        try { return read(); }
        catch (Exception e)
        {
            log.Warn($"Diagnostic : lecture « {what} » impossible ({e.Message})");
            return null;
        }
    }

    // ---------- RAM ----------

    private RamData ReadRam()
    {
        var modules = Wmi(@"root\cimv2", "SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, DeviceLocator, BankLabel, PartNumber FROM Win32_PhysicalMemory")
            .Select(o => new RamModule(Long(o["Capacity"]), Int(o["Speed"]), Int(o["ConfiguredClockSpeed"]), Int(o["SMBIOSMemoryType"]),
                Str(o["DeviceLocator"]), Str(o["BankLabel"]), Str(o["PartNumber"]).Trim()))
            .ToList();
        var os = Wmi(@"root\cimv2", "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
        return new RamData(modules, Long(os?["TotalVisibleMemorySize"]) * 1024, Long(os?["FreePhysicalMemory"]) * 1024);
    }

    // ---------- Écran ----------

    private static IReadOnlyList<DisplayData> ReadDisplays()
    {
        var list = new List<DisplayData>();
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device.Size = Marshal.SizeOf<DisplayDevice>())
        {
            if ((device.StateFlags & AttachedToDesktop) == 0) continue;
            var current = NewDevMode();
            if (!EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref current)) continue;
            var max = current.DisplayFrequency;
            var mode = NewDevMode();
            for (var m = 0; EnumDisplaySettings(device.DeviceName, m, ref mode); m++)
                if (mode.PelsWidth == current.PelsWidth && mode.PelsHeight == current.PelsHeight && mode.DisplayFrequency > max)
                    max = mode.DisplayFrequency;
            list.Add(new DisplayData($"Écran {list.Count + 1}", current.PelsWidth, current.PelsHeight, current.DisplayFrequency, max));
        }
        return list;
    }

    // ---------- Carte graphique ----------

    private IReadOnlyList<GpuData> ReadGpus()
    {
        var gpus = Wmi(@"root\cimv2", "SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController")
            .Select(o => (Name: Str(o["Name"]), Driver: Str(o["DriverVersion"]),
                Date: o["DriverDate"] is string d && d.Length >= 8 ? ManagementDateTimeConverter.ToDateTime(d) : (DateTime?)null))
            .Where(g => !g.Name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase))
            .ToList();

        NvidiaData? nvidia = null;
        var smi = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
        }.FirstOrDefault(File.Exists);
        var hasNvidia = gpus.Any(g => g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        if (hasNvidia && smi is not null)
        {
            var query = commands.Run(smi, "--query-gpu=temperature.gpu,pcie.link.gen.current,pcie.link.gen.max,pcie.link.width.current,pcie.link.width.max --format=csv,noheader,nounits");
            var memory = commands.Run(smi, "-q -d MEMORY");
            if (query.Ok) nvidia = DiagnosticParsers.ParseNvidiaQuery(query.Output, memory.Ok ? DiagnosticParsers.ParseBar1TotalMiB(memory.Output) : null);
            else log.Warn($"Diagnostic : nvidia-smi a échoué ({query.Text})");
            if (nvidia is { TemperatureC: null })
            {
                // Secours : certaines cartes renvoient « [N/A] » dans la requête courte.
                log.Info($"Diagnostic : température absente de la requête nvidia-smi (« {query.Output} »), seconde méthode");
                var temp = commands.Run(smi, "-q -d TEMPERATURE");
                if (temp.Ok) nvidia = nvidia with { TemperatureC = DiagnosticParsers.ParseNvidiaTemperature(temp.Output) };
            }
        }

        return gpus.Select(g =>
        {
            var isNvidia = g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
            return new GpuData(g.Name, g.Driver, g.Date, isNvidia ? nvidia : null, isNvidia && smi is null);
        }).ToList();
    }

    // ---------- Stabilité ----------

    private static StabilityData ReadStability()
    {
        const string last30Days = "TimeCreated[timediff(@SystemTime) <= 2592000000]";
        var whea = Events($"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and {last30Days}]]");
        var kp41 = Events($"*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 and {last30Days}]]");
        var bsod = Events($"*[System[(Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] or Provider[@Name='BugCheck']) and EventID=1001 and {last30Days}]]");
        return new StabilityData(whea.Count, whea.Last, kp41.Count, kp41.Last, bsod.Count, bsod.Last);
    }

    private static (int Count, DateTime? Last) Events(string xpath)
    {
        var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        var count = 0;
        DateTime? last = null;
        for (var e = reader.ReadEvent(); e is not null && count < 1000; e = reader.ReadEvent())
        {
            using (e)
            {
                count++;
                last ??= e.TimeCreated;
            }
        }
        return (count, last);
    }

    // ---------- Stockage ----------

    private static IReadOnlyList<DiskData> ReadDisks()
    {
        const string storage = @"root\Microsoft\Windows\Storage";
        var letters = Wmi(storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition")
            .Select(o => (Disk: Long(o["DiskNumber"]).ToString(), Letter: o["DriveLetter"] is char c && char.IsLetter(c) ? $"{c}:" : null))
            .Where(p => p.Letter is not null)
            .GroupBy(p => p.Disk)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(p => p.Letter!).OrderBy(l => l).ToList());

        return Wmi(storage, "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk")
            .Select(o =>
            {
                var media = Int(o["MediaType"]);
                var bus = Int(o["BusType"]);
                var type = media switch
                {
                    4 => bus == 17 ? "SSD NVMe" : "SSD",
                    3 => "HDD (disque dur)",
                    _ => bus == 17 ? "SSD NVMe" : bus == 7 ? "Disque USB" : "Type inconnu",
                };
                if (bus == 7 && media is 3 or 4) type += " externe (USB)";
                var health = Int(o["HealthStatus"]) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };
                return new DiskData(Str(o["FriendlyName"]), type, health, letters.GetValueOrDefault(Str(o["DeviceId"])) ?? []);
            })
            .ToList();
    }

    private static IReadOnlyList<VolumeData> ReadVolumes() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new VolumeData(d.Name.TrimEnd('\\'), d.TotalSize, d.AvailableFreeSpace))
            .ToList();

    private IReadOnlyList<GameFolder> ReadGameFolders()
    {
        var list = new List<GameFolder>();
        var steam = registry.Get(new RegPath(RegHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"))?.Text;
        if (steam is not null)
        {
            var vdf = Path.Combine(steam.Replace('/', '\\'), "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                list.AddRange(DiagnosticParsers.ParseSteamLibraries(File.ReadAllText(vdf)).Select(p => new GameFolder("Steam", p)));
        }
        var epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(epic))
        {
            foreach (var item in Directory.EnumerateFiles(epic, "*.item"))
                if (DiagnosticParsers.ParseEpicManifest(File.ReadAllText(item)) is { } location)
                    list.Add(new GameFolder("Epic Games", location));
        }
        return list;
    }

    // ---------- Windows ----------

    private WindowsData ReadWindows(double? cpu, IReadOnlyList<ProcessUsage> top)
    {
        var active = power.GetActiveScheme();
        var plan = power.GetSchemeName(active) ?? active.ToString();

        // Mode Jeu : activé par défaut quand la valeur n'existe pas.
        var gameMode = registry.Get(new RegPath(RegHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled")) is { } gm ? gm.Number != 0 : true;
        var hagsValue = registry.Get(new RegPath(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"));
        bool? hags = hagsValue is null ? null : hagsValue.Number == 2;

        bool? hvci = null;
        try
        {
            var dg = Wmi(@"root\Microsoft\Windows\DeviceGuard", "SELECT SecurityServicesRunning FROM Win32_DeviceGuard").FirstOrDefault();
            if (dg?["SecurityServicesRunning"] is uint[] running) hvci = running.Contains(2u);
        }
        catch { /* classe absente */ }
        hvci ??= registry.Get(new RegPath(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled")) is { } e
            ? e.Number == 1 : null;

        const string prefs = @"Software\Microsoft\DirectX\UserGpuPreferences";
        var highPerf = registry.ValueNames(RegHive.CurrentUser, prefs)
            .Count(n => n != "DirectXUserGlobalSettings" &&
                        (registry.Get(new RegPath(RegHive.CurrentUser, prefs, n))?.Text ?? "").Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase));

        var startupEnabled = startup.List().Count(x => x.Enabled);
        return new WindowsData(plan, gameMode, hags, hvci, startupEnabled, cpu, top, power.HasBattery(), highPerf);
    }

    /// <summary>Utilisation moyenne du processeur sur 30 s (GetSystemTimes) et les programmes qui consomment le plus.</summary>
    private (double? Cpu, IReadOnlyList<ProcessUsage> Top) MeasureIdleCpu(CancellationToken cancel)
    {
        try
        {
            var before = Snapshot();
            if (!GetSystemTimes(out var idle1, out var kernel1, out var user1)) return (null, []);
            var watch = Stopwatch.StartNew();
            Task.Delay(CpuSample, cancel).Wait(cancel);
            if (!GetSystemTimes(out var idle2, out var kernel2, out var user2)) return (null, []);
            var after = Snapshot();

            var total = (kernel2 - kernel1) + (user2 - user1); // le temps « kernel » inclut le temps d'inactivité
            var cpu = total <= 0 ? 0 : 100.0 * (total - (idle2 - idle1)) / total;
            var capacity = watch.Elapsed.TotalMilliseconds * Environment.ProcessorCount;
            var self = Environment.ProcessId;
            double Percent(int id) => after.TryGetValue(id, out var a) && before.TryGetValue(id, out var b)
                ? 100.0 * (a.Cpu - b.Cpu).TotalMilliseconds / capacity : 0;
            var processes = after
                .Where(p => p.Key != 0 && p.Key != self && before.ContainsKey(p.Key))
                .Select(p => new ProcessUsage(p.Value.Name, Percent(p.Key)))
                .GroupBy(p => p.Name)
                .Select(g => new ProcessUsage(g.Key, g.Sum(p => p.Percent)))
                .ToList();
            var corrected = DiagnosticParsers.CorrectIdleCpu(cpu, Percent(self), processes);
            log.Info($"Diagnostic : processeur {cpu:0.#} % mesuré, {corrected.Cpu:0.#} % hors Unpark et outils du diagnostic");
            return (corrected.Cpu, corrected.Top);
        }
        catch (Exception e)
        {
            log.Warn($"Diagnostic : mesure du processeur impossible ({e.Message})");
            return (null, []);
        }
    }

    private static Dictionary<int, (string Name, TimeSpan Cpu)> Snapshot()
    {
        var map = new Dictionary<int, (string, TimeSpan)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try { map[p.Id] = (p.ProcessName, p.TotalProcessorTime); }
                catch { /* processus protégé ou terminé */ }
            }
        }
        return map;
    }

    // ---------- Réseau ----------

    /// <summary>Ping de 1.1.1.1 toutes les 0,5 s pendant 20 s (moyenne, gigue, pertes), et de la box en parallèle.</summary>
    private NetworkData? MeasureNetwork(CancellationToken cancel)
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork));
            var gateway = nic?.GetIPProperties().GatewayAddresses
                .Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var isWifi = nic?.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

            var gatewayTask = gateway is null ? Task.FromResult<List<long>>([]) : Task.Run(() => PingSeries(gateway, 20, cancel), cancel);
            var count = (int)(NetworkSample.TotalMilliseconds / 500);
            var rtts = PingSeries(IPAddress.Parse(PingTarget), count, cancel);
            var gw = gatewayTask.Result;

            double? jitter = rtts.Count > 1 ? rtts.Zip(rtts.Skip(1), (a, b) => (double)Math.Abs(a - b)).Average() : null;
            return new NetworkData(PingTarget, count, rtts.Count, rtts.Count > 0 ? rtts.Average() : null, jitter,
                gw.Count > 0 ? gw.Average() : null, isWifi);
        }
        catch (Exception e)
        {
            log.Warn($"Diagnostic : mesure réseau impossible ({e.Message})");
            return null;
        }
    }

    private static List<long> PingSeries(IPAddress target, int count, CancellationToken cancel)
    {
        using var ping = new Ping();
        var results = new List<long>();
        for (var i = 0; i < count && !cancel.IsCancellationRequested; i++)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var reply = ping.Send(target, 1000);
                if (reply.Status == IPStatus.Success) results.Add(reply.RoundtripTime);
            }
            catch (PingException) { }
            var wait = 500 - (int)watch.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep(wait);
        }
        return results;
    }

    // ---------- Outils ----------

    private static List<ManagementBaseObject> Wmi(string scope, string query)
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        return searcher.Get().Cast<ManagementBaseObject>().ToList();
    }

    private static string Str(object? v) => v?.ToString() ?? "";
    private static int Int(object? v) => v is null ? 0 : (int)Convert.ToInt64(v);
    private static long Long(object? v) => v is null ? 0 : Convert.ToInt64(v);

    // ---------- API Windows (documentées) ----------

    private const int EnumCurrentSettings = -1;
    private const int AttachedToDesktop = 0x1;

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

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
}
