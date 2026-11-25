using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace Unpark.Core.Disks;

[SupportedOSPlatform("windows")]
public sealed class WindowsDiskSystem : IDiskSystem
{
    private const string Storage = @"root\Microsoft\Windows\Storage";
    /// <summary>« L'optimiseur de stockage a terminé avec succès … sur (C:) ».</summary>
    private const int DefragSucceededEvent = 258;
    private static readonly Regex DriveInText = new(@"\(([A-Za-z]):\)|^([A-Za-z]):\\?$", RegexOptions.Compiled);

    static WindowsDiskSystem()
    {
        // defrag.exe écrit dans la page de code OEM (ex. 850) : accents lisibles.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public IReadOnlyList<VolumeInfo> Volumes()
    {
        var disks = Wmi(Storage, "SELECT DeviceId, MediaType, BusType FROM MSFT_PhysicalDisk")
            .GroupBy(o => Str(o["DeviceId"]))
            .ToDictionary(g => g.Key, g => DiskOptimizer.Classify(Int(g.First()["MediaType"]), Int(g.First()["BusType"])));
        var diskOfLetter = Wmi(Storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition")
            .Where(o => o["DriveLetter"] is char c && char.IsAsciiLetter(c))
            .GroupBy(o => char.ToUpperInvariant((char)o["DriveLetter"]))
            .ToDictionary(g => g.Key, g => Long(g.First()["DiskNumber"]).ToString(CultureInfo.InvariantCulture));

        return Wmi(Storage, "SELECT DriveLetter, FileSystemLabel, FileSystem, DriveType, Size FROM MSFT_Volume")
            .Where(o => o["DriveLetter"] is char c && char.IsAsciiLetter(c) && Int(o["DriveType"]) == 3) // 3 = disque fixe
            .Where(o => Str(o["FileSystem"]) is "NTFS" or "ReFS" or "FAT32" or "exFAT" or "FAT")
            .Select(o =>
            {
                var letter = char.ToUpperInvariant((char)o["DriveLetter"]);
                var (kind, text) = diskOfLetter.TryGetValue(letter, out var disk) && disks.TryGetValue(disk, out var m) ? m : (MediaKind.Unknown, "Type inconnu");
                return new VolumeInfo(letter, Str(o["FileSystemLabel"]), kind, text, Long(o["Size"]));
            })
            .ToList();
    }

    public IReadOnlyDictionary<char, DateTime> LastOptimizations()
    {
        var result = new Dictionary<char, DateTime>();
        var xpath = $"*[System[Provider[@Name='Microsoft-Windows-Defrag'] and (EventID={DefragSucceededEvent})]]";
        var query = new EventLogQuery("Application", PathType.LogName, xpath) { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        for (var i = 0; i < 500 && reader.ReadEvent() is { } e; i++)
        {
            using (e)
            {
                if (e.TimeCreated is not { } when) continue;
                foreach (var p in e.Properties)
                {
                    var m = DriveInText.Match(p.Value?.ToString()?.Trim() ?? "");
                    if (!m.Success) continue;
                    var letter = char.ToUpperInvariant((m.Groups[1].Success ? m.Groups[1] : m.Groups[2]).Value[0]);
                    result.TryAdd(letter, when); // lecture du plus récent au plus ancien : la première date est la bonne
                    break;
                }
            }
        }
        return result;
    }

    public int FragmentationPercent(char letter)
    {
        using var searcher = new ManagementObjectSearcher(@"root\cimv2", $"SELECT * FROM Win32_Volume WHERE DriveLetter = '{char.ToUpperInvariant(letter)}:'");
        using var volume = searcher.Get().Cast<ManagementObject>().FirstOrDefault()
            ?? throw new InvalidOperationException($"Lecteur {letter}: introuvable.");
        using var output = volume.InvokeMethod("DefragAnalysis", null, null);
        var code = Convert.ToUInt32(output["ReturnValue"], CultureInfo.InvariantCulture);
        if (code != 0) throw new InvalidOperationException($"Windows a refusé l'analyse (code {code}).");
        var analysis = (ManagementBaseObject)output["DefragAnalysis"];
        return Convert.ToInt32(analysis["FilePercentFragmentation"], CultureInfo.InvariantCulture);
    }

    public int RunDefrag(string arguments, Action<string> onOutput, CancellationToken cancel)
    {
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "defrag.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de lancer defrag.exe.");
        using var stop = cancel.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });
        var errors = process.StandardError.ReadToEndAsync();

        // L'avancement (/U) réécrit la même ligne avec « \r » : on découpe sur \r et \n.
        var line = new StringBuilder();
        var buffer = new char[256];
        int read;
        while ((read = process.StandardOutput.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var ch = buffer[i];
                if (ch is '\r' or '\n')
                {
                    if (line.Length > 0) onOutput(line.ToString());
                    line.Clear();
                }
                else line.Append(ch);
            }
            // Ligne d'avancement pas encore terminée : on la transmet déjà.
            if (line.Length > 0 && line.ToString().Contains('%')) onOutput(line.ToString());
        }
        if (line.Length > 0) onOutput(line.ToString());
        process.WaitForExit();
        foreach (var err in errors.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) onOutput(err);
        cancel.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static List<ManagementBaseObject> Wmi(string scope, string query)
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        return searcher.Get().Cast<ManagementBaseObject>().ToList();
    }

    private static string Str(object? v) => v?.ToString() ?? "";
    private static int Int(object? v) => v is null ? 0 : (int)Convert.ToInt64(v, CultureInfo.InvariantCulture);
    private static long Long(object? v) => v is null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture);
}
