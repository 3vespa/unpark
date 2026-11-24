using Unpark.Core.Diagnostics;

namespace Unpark.Tests;

public class DiagnosticParserTests
{
    [Theory]
    [InlineData("F4-3600C16-8GVKC", 3600)]
    [InlineData("CMK16GX4M2B3200C16", 3200)]
    [InlineData("KF436C17BBK2/16", 3600)]
    [InlineData("KF560C36BBEK2-32", 6000)]
    [InlineData("F5-6000J3038F16G", 6000)]
    [InlineData("BL2K8G32C16U4B", null)]
    [InlineData("M378A1K43CB2-CTD", null)]
    public void Vitesse_prevue_d_apres_la_reference(string part, int? expected) =>
        Assert.Equal(expected, DiagnosticParsers.RatedSpeedFromPartNumber(part));

    [Theory]
    [InlineData("ChannelA", "DIMM 0", "A")]
    [InlineData("BANK 1", "DIMM_B1", "B")]
    [InlineData("P0 CHANNEL A", "DIMM 1", "A")]
    [InlineData("BANK 0", "Controller0-ChannelB-DIMM0", "B")]
    [InlineData("BANK 0", "DIMM0", null)]
    public void Canal_memoire(string bank, string locator, string? expected) =>
        Assert.Equal(expected, DiagnosticParsers.MemoryChannel(new RamModule(8L << 30, 3200, 3200, 26, locator, bank, "")));

    [Fact]
    public void Sortie_nvidia_smi()
    {
        var nv = DiagnosticParsers.ParseNvidiaQuery("45, 1, 4, 16, 16\n", 16384);
        Assert.Equal(new NvidiaData(45, 1, 4, 16, 16, 16384), nv);
        Assert.Equal(new NvidiaData(null, 4, 4, 8, 16, null), DiagnosticParsers.ParseNvidiaQuery("[N/A], 4, 4, 8, 16", null));
        Assert.Null(DiagnosticParsers.ParseNvidiaQuery("", null));

        const string memory = "    BAR1 Memory Usage\r\n        Total                             : 16384 MiB\r\n        Used                              : 1 MiB";
        Assert.Equal(16384, DiagnosticParsers.ParseBar1TotalMiB(memory));
        Assert.Null(DiagnosticParsers.ParseBar1TotalMiB("rien"));
    }

    [Fact]
    public void Temperature_nvidia_en_secours()
    {
        const string q = "==============NVSMI LOG==============\r\n    Temperature\r\n        GPU Current Temp                  : 41 C\r\n        GPU T.Limit Temp                  : N/A";
        Assert.Equal(41, DiagnosticParsers.ParseNvidiaTemperature(q));
        Assert.Null(DiagnosticParsers.ParseNvidiaTemperature("GPU Current Temp : N/A"));
    }

    [Fact]
    public void Processeur_au_repos_sans_Unpark_ni_WMI()
    {
        // Cas réel : 15 % mesurés pendant que le diagnostic interrogeait WMI.
        var (cpu, top) = DiagnosticParsers.CorrectIdleCpu(15, 2.5,
            [new("brave", 6), new("WmiPrvSE", 3.5), new("dwm", 2), new("svchost", 1), new("nvidia-smi", 0.4)]);
        Assert.Equal(8.6, cpu, 1);
        Assert.Equal(["brave", "dwm", "svchost"], top.Select(p => p.Name));
        Assert.Equal(0, DiagnosticParsers.CorrectIdleCpu(1, 3, []).Cpu);
    }

    [Fact]
    public void Bibliotheques_Steam_et_Epic()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0" { "path"  "C:\\Program Files (x86)\\Steam" "label" "" }
                "1" { "path"  "D:\\SteamLibrary" }
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], DiagnosticParsers.ParseSteamLibraries(vdf));
        Assert.Equal(@"E:\Epic\Fortnite", DiagnosticParsers.ParseEpicManifest("""{ "DisplayName": "Fortnite", "InstallLocation": "E:\\Epic\\Fortnite" }"""));
        Assert.Null(DiagnosticParsers.ParseEpicManifest("pas du json"));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", true)]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", true)]
    [InlineData("NVIDIA GeForce RTX 5080", true)]
    [InlineData("NVIDIA GeForce RTX 3080", false)]
    [InlineData("AMD Radeon RX 7800 XT", false)]
    public void RTX_40_ou_50(string name, bool expected) => Assert.Equal(expected, DiagnosticParsers.IsRtx40Or50(name));

    [Theory]
    [InlineData("Intel(R) UHD Graphics 630", true)]
    [InlineData("Intel(R) Iris(R) Xe Graphics", true)]
    [InlineData("AMD Radeon(TM) Graphics", true)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", false)]
    [InlineData("AMD Radeon RX 6700 XT", false)]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", false)]
    public void Carte_integree(string name, bool expected) => Assert.Equal(expected, DiagnosticParsers.IsIntegratedGpu(name));
}

public class DiagnosticAnalyzerTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 18, 0, 0);
    private const long Gb = 1L << 30;

    private static RamModule Stick(int rated, int configured, string bank, string part = "", int type = 26) =>
        new(8 * Gb, rated, configured, type, "DIMM", bank, part);

    [Fact]
    public void RAM_XMP_desactive_detecte_par_la_reference()
    {
        var ram = new RamData([Stick(2133, 2133, "ChannelA", "F4-3600C16-8GVKC"), Stick(2133, 2133, "ChannelB", "F4-3600C16-8GVKC")], 16 * Gb, 10 * Gb);
        var speed = DiagnosticAnalyzer.Ram(ram).Single(c => c.Title.StartsWith("Vitesse"));
        Assert.Equal(DiagStatus.Orange, speed.Status);
        Assert.Contains("prévues pour 3600", speed.Explanation);
        Assert.Equal(DiagStatus.Green, DiagnosticAnalyzer.Ram(ram).Single(c => c.Title.StartsWith("Double")).Status);
    }

    [Fact]
    public void RAM_DDR5_a_4800_signalee_DDR5_6000_ok()
    {
        var slow = new RamData([Stick(4800, 4800, "A", type: 34), Stick(4800, 4800, "B", type: 34)], 32 * Gb, 20 * Gb);
        Assert.Equal(DiagStatus.Orange, DiagnosticAnalyzer.Ram(slow).Single(c => c.Title.StartsWith("Vitesse")).Status);
        var fast = new RamData([Stick(6000, 6000, "A", type: 34), Stick(6000, 6000, "B", type: 34)], 32 * Gb, 20 * Gb);
        Assert.Equal(DiagStatus.Green, DiagnosticAnalyzer.Ram(fast).Single(c => c.Title.StartsWith("Vitesse")).Status);
    }

    [Fact]
    public void RAM_une_barrette_ou_meme_canal_et_quantite()
    {
        var single = new RamData([new RamModule(8 * Gb, 3200, 3200, 26, "DIMM_A1", "BANK 0", "")], 8 * Gb, 1 * Gb);
        var checks = DiagnosticAnalyzer.Ram(single);
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title.StartsWith("Double")).Status);
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title == "Quantité").Status);
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title == "Utilisation").Status);   // 87 % utilisée

        var sameChannel = new RamData([Stick(3200, 3200, "ChannelA"), Stick(3200, 3200, "ChannelA")], 16 * Gb, 8 * Gb);
        Assert.Contains("même canal", DiagnosticAnalyzer.Ram(sameChannel).Single(c => c.Title.StartsWith("Double")).Value);
    }

    [Fact]
    public void Ecran_a_60_Hz_sur_un_144_Hz_en_rouge()
    {
        var checks = DiagnosticAnalyzer.Displays([new DisplayData("\\\\.\\DISPLAY1", 2560, 1440, 60, 144)]);
        Assert.Equal(DiagStatus.Red, checks[0].Status);
        Assert.Equal(DiagFix.DisplaySettings, checks[0].Fix);
        Assert.Equal(DiagStatus.Green, DiagnosticAnalyzer.Displays([new DisplayData("x", 1920, 1080, 144, 144)])[0].Status);
        Assert.Equal(DiagStatus.Green, DiagnosticAnalyzer.Displays([new DisplayData("x", 1920, 1080, 59, 60)])[0].Status); // 59/60 Hz
    }

    [Fact]
    public void Pilote_ancien_PCIe_et_Resizable_BAR()
    {
        var gpu = new GpuData("NVIDIA GeForce RTX 3070", "32.0.15.5585", Now.AddMonths(-14),
            new NvidiaData(48, 1, 4, 8, 16, 256), false);
        var checks = DiagnosticAnalyzer.Gpus([gpu], null, Now);
        Assert.Equal(DiagStatus.Red, checks.Single(c => c.Title == "Pilote graphique").Status);
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title == "Liaison PCIe").Status);       // x8 au lieu de x16
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title == "Resizable BAR").Status);
        Assert.Equal(DiagStatus.Green, checks.Single(c => c.Title == "Température GPU").Status);

        var idle = gpu with { DriverDate = Now.AddMonths(-1), Nvidia = new NvidiaData(40, 1, 4, 16, 16, 8192) };
        var ok = DiagnosticAnalyzer.Gpus([idle], null, Now);
        Assert.Equal(DiagStatus.Info, ok.Single(c => c.Title == "Liaison PCIe").Status);   // gen 1 au repos : normal
        Assert.Equal(DiagStatus.Green, ok.Single(c => c.Title == "Resizable BAR").Status);
    }

    [Fact]
    public void Portable_hybride_rappel_carte_dediee()
    {
        var w = new WindowsData("Utilisation normale", true, null, null, 3, 2, [], IsLaptop: true, AppsOnHighPerformanceGpu: 0);
        var checks = DiagnosticAnalyzer.Gpus(
            [new GpuData("Intel(R) Iris(R) Xe Graphics", "31", Now, null, false), new GpuData("NVIDIA GeForce RTX 4060 Laptop GPU", "32", Now, null, true)], w, Now);
        var check = checks.Single(c => c.Title.StartsWith("Carte utilisée"));
        Assert.Equal(DiagFix.GraphicsSettings, check.Fix);
    }

    [Fact]
    public void Stabilite_WHEA_ecrans_bleus_et_Kernel_Power()
    {
        var clean = DiagnosticAnalyzer.Stability(new StabilityData(0, null, 0, null, 0, null));
        Assert.All(clean, c => Assert.Equal(DiagStatus.Green, c.Status));

        var bad = DiagnosticAnalyzer.Stability(new StabilityData(12, Now, 2, Now, 1, Now));
        Assert.Equal(DiagStatus.Red, bad.Single(c => c.Title == "Écrans bleus").Status);
        Assert.Equal(DiagStatus.Red, bad.Single(c => c.Title.StartsWith("Erreurs matérielles")).Status);
        Assert.Contains("overclock", bad.Single(c => c.Title.StartsWith("Erreurs matérielles")).Explanation);
        Assert.Equal(DiagStatus.Orange, bad.Single(c => c.Title.StartsWith("Redémarrages")).Status);
    }

    [Fact]
    public void Stockage_jeux_sur_disque_dur_espace_et_sante()
    {
        var disks = new[]
        {
            new DiskData("Samsung 980", "SSD NVMe", "Healthy", ["C:"]),
            new DiskData("WD Blue", "HDD (disque dur)", "Warning", ["D:"]),
        };
        var volumes = new[] { new VolumeData("C:", 500 * Gb, 30 * Gb), new VolumeData("D:", 1000 * Gb, 500 * Gb) };
        var checks = DiagnosticAnalyzer.Storage(disks, volumes, [new GameFolder("Steam", @"D:\SteamLibrary")]);
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title == "Disque WD Blue").Status);
        Assert.Equal(DiagStatus.Red, checks.Single(c => c.Title == "Espace libre C:").Status);     // 6 % libres
        Assert.Equal(DiagFix.Cleanup, checks.Single(c => c.Title == "Espace libre C:").Fix);
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title == "Jeux sur disque dur").Status);

        var hddOnly = DiagnosticAnalyzer.Storage([new DiskData("Seagate", "HDD (disque dur)", "Healthy", ["C:"])], [], []);
        Assert.Equal(DiagStatus.Red, hddOnly.Single(c => c.Title == "SSD").Status);
    }

    [Fact]
    public void Windows_plan_HAGS_selon_la_carte_et_processeur_au_repos()
    {
        var w = new WindowsData("Utilisation normale", false, false, true, 14, 31, [new("Updater.exe", 22), new("Discord", 4)], false, 0);
        var rtx40 = new[] { new GpuData("NVIDIA GeForce RTX 4070", "32", Now, null, false) };
        var checks = DiagnosticAnalyzer.Windows(w, rtx40);
        Assert.Equal(DiagFix.PowerPlan, checks.Single(c => c.Title == "Plan d'alimentation").Fix);
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title == "Mode Jeu").Status);
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title.StartsWith("Planification GPU")).Status);   // RTX 40 sans HAGS
        Assert.Equal(DiagStatus.Info, checks.Single(c => c.Title == "Intégrité de la mémoire").Status);
        Assert.Equal(DiagStatus.Orange, checks.Single(c => c.Title == "Programmes au démarrage").Status);
        var cpu = checks.Single(c => c.Title == "Processeur au repos");
        Assert.Equal(DiagStatus.Red, cpu.Status);
        Assert.Contains("Updater.exe 22 %", cpu.Value);

        var gtx = new[] { new GpuData("NVIDIA GeForce GTX 1660", "32", Now, null, false) };
        var hags = DiagnosticAnalyzer.Windows(w with { ActivePlan = "Unpark Performance" }, gtx);
        Assert.Equal(DiagStatus.Info, hags.Single(c => c.Title.StartsWith("Planification GPU")).Status);
        Assert.Contains("Effet quasi nul", hags.Single(c => c.Title.StartsWith("Planification GPU")).Explanation);
        Assert.Equal(DiagStatus.Green, hags.Single(c => c.Title == "Plan d'alimentation").Status);
    }

    [Fact]
    public void Reseau_perte_gigue_ping_et_WiFi()
    {
        var good = DiagnosticAnalyzer.Network(new NetworkData("1.1.1.1", 40, 40, 14, 1.2, 2, false));
        Assert.All(good, c => Assert.Equal(DiagStatus.Green, c.Status));

        var bad = DiagnosticAnalyzer.Network(new NetworkData("1.1.1.1", 40, 37, 95, 22, 25, true));
        Assert.Equal(DiagStatus.Red, bad.Single(c => c.Title == "Perte de paquets").Status);    // 7,5 %
        Assert.Equal(DiagStatus.Red, bad.Single(c => c.Title.StartsWith("Ping")).Status);
        Assert.Equal(DiagStatus.Red, bad.Single(c => c.Title.StartsWith("Gigue")).Status);
        Assert.Equal(DiagStatus.Orange, bad.Single(c => c.Title == "Wi-Fi").Status);

        Assert.Equal(DiagStatus.Red, DiagnosticAnalyzer.Network(new NetworkData("1.1.1.1", 40, 0, null, null, null, false)).Single().Status);
    }

    [Fact]
    public void Temperature_CPU_non_disponible_et_rapport_Discord()
    {
        var data = new DiagnosticData(Now, "Windows 11 Pro 23H2 (build 22631.4460)",
            new RamData([Stick(3200, 3200, "A"), Stick(3200, 3200, "B")], 16 * Gb, 8 * Gb),
            [new DisplayData("x", 1920, 1080, 60, 165)], [], new StabilityData(0, null, 0, null, 0, null), [], [], [], null, null);
        var sections = DiagnosticAnalyzer.Analyze(data);
        Assert.Equal("Non disponible", sections.Single(s => s.Title == "Processeur").Checks.Single().Value);

        var report = DiagnosticAnalyzer.Report(data, sections);
        Assert.Contains("Windows 11 Pro 23H2", report);
        Assert.Contains("[X] Fréquence de l'écran : 60 Hz (max 165 Hz)", report);
        Assert.Contains("1 problème(s)", report);
        Assert.StartsWith("**Diagnostic Unpark", report);
    }
}
