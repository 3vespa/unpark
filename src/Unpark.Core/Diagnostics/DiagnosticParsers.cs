using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Unpark.Core.Diagnostics;

/// <summary>Lecture des sorties d'outils et fichiers de lanceurs (fonctions pures, testées).</summary>
public static partial class DiagnosticParsers
{
    /// <summary>
    /// Sortie de : nvidia-smi --query-gpu=temperature.gpu,pcie.link.gen.current,pcie.link.gen.max,pcie.link.width.current,pcie.link.width.max
    ///             --format=csv,noheader,nounits
    /// Une ligne par carte ; « [N/A] » ou « [Not Supported] » donnent null.
    /// </summary>
    public static NvidiaData? ParseNvidiaQuery(string output, long? bar1TotalMiB)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (line is null) return null;
        var f = line.Split(',').Select(s => s.Trim()).ToArray();
        if (f.Length < 5) return null;
        int? N(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        return new NvidiaData(N(f[0]), N(f[1]), N(f[2]), N(f[3]), N(f[4]), bar1TotalMiB);
    }

    /// <summary>Taille totale de la zone BAR1 dans « nvidia-smi -q -d MEMORY » (256 Mio = Resizable BAR désactivé).</summary>
    public static long? ParseBar1TotalMiB(string output)
    {
        var m = Bar1Regex().Match(output);
        return m.Success && long.TryParse(m.Groups[1].Value, out var v) ? v : null;
    }

    [GeneratedRegex(@"BAR1 Memory Usage\s*\r?\n\s*Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase)]
    private static partial Regex Bar1Regex();

    /// <summary>Dossiers de bibliothèque Steam dans steamapps\libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> ParseSteamLibraries(string vdf) =>
        SteamPathRegex().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamPathRegex();

    /// <summary>Dossier d'installation dans un manifeste Epic Games (.item, JSON).</summary>
    public static string? ParseEpicManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("InstallLocation", out var p) ? p.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Vitesse prévue des barrettes d'après leur référence (ex. F4-3600C16… → 3600, CMK16GX4M2B3200C16 → 3200,
    /// KF436C17… → 3600, F5-6000J3038… → 6000). null si la référence ne l'indique pas.
    /// </summary>
    public static int? RatedSpeedFromPartNumber(string partNumber)
    {
        var p = partNumber.Trim().ToUpperInvariant();
        var kingston = KingstonRegex().Match(p);
        if (kingston.Success) return int.Parse(kingston.Groups[1].Value) * 100;
        var m = SpeedRegex().Matches(p).Select(x => int.Parse(x.Value)).DefaultIfEmpty(0).Max();
        return m > 0 ? m : null;
    }

    // Kingston Fury : KF4 + 2 chiffres (centaines de MT/s), ex. KF436C17 = DDR4-3600, KF560C36 = DDR5-6000.
    [GeneratedRegex(@"^KF[45](\d{2})C")]
    private static partial Regex KingstonRegex();

    [GeneratedRegex(@"(?<!\d)(2133|2400|2666|2800|3000|3200|3333|3466|3600|3733|3800|4000|4133|4266|4400|4600|4800|5200|5600|6000|6200|6400|6600|6800|7200|7600|8000)(?!\d)")]
    private static partial Regex SpeedRegex();

    /// <summary>Canal mémoire d'une barrette (« ChannelA », « DIMM_B1 », « P0 CHANNEL A »…), ou null.</summary>
    public static string? MemoryChannel(RamModule module)
    {
        foreach (var text in new[] { module.Bank, module.Locator })
        {
            var m = ChannelRegex().Match(text ?? "");
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        }
        return null;
    }

    [GeneratedRegex(@"(?:CHANNEL|DIMM)[\s_-]*([A-H])(?![A-Z])", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelRegex();

    /// <summary>RTX 40xx ou 50xx (bureau ou portable) : HAGS nécessaire pour DLSS Frame Generation.</summary>
    public static bool IsRtx40Or50(string gpuName) => Rtx4050Regex().IsMatch(gpuName);

    [GeneratedRegex(@"RTX\s*(40|50)\d{2}", RegexOptions.IgnoreCase)]
    private static partial Regex Rtx4050Regex();

    /// <summary>RTX 30 ou plus récente (compatible Resizable BAR côté carte).</summary>
    public static bool SupportsResizableBar(string gpuName) => RebarRegex().IsMatch(gpuName);

    [GeneratedRegex(@"RTX\s*(30|40|50)\d{2}", RegexOptions.IgnoreCase)]
    private static partial Regex RebarRegex();

    /// <summary>Carte graphique intégrée au processeur (Intel UHD/Iris, AMD Radeon Graphics sans « RX »).</summary>
    public static bool IsIntegratedGpu(string name)
    {
        var n = name.ToUpperInvariant();
        if (n.Contains("NVIDIA") || n.Contains(" ARC")) return false;
        if (n.Contains("INTEL")) return true;
        return n.Contains("RADEON") && !n.Contains("RX ") && (n.Contains("GRAPHICS") || n.Contains("VEGA"));
    }
}
