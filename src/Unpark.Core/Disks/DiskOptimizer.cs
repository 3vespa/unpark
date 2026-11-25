using System.Globalization;
using System.Text.RegularExpressions;
using Unpark.Core.Logging;

namespace Unpark.Core.Disks;

/// <summary>Type de support : il décide du traitement (TRIM ou défragmentation).</summary>
public enum MediaKind { Ssd, Hdd, Unknown }

/// <summary>Traitement fait par « Optimiser », comme l'outil Windows « Optimiser les lecteurs ».</summary>
public enum OptimizeOperation { Retrim, Defrag }

/// <summary>Un lecteur (volume avec une lettre) et le disque physique qui le porte.</summary>
public sealed record VolumeInfo(char Letter, string Label, MediaKind Media, string MediaText, long SizeBytes);

/// <summary>Avancement lu dans la sortie de defrag.exe : pourcentage de l'étape en cours et texte de Windows.</summary>
public sealed record OptimizeProgress(int Percent, string Text);

public sealed record OptimizeResult(bool Success, bool Cancelled, string Message);

/// <summary>Parties propres à Windows (WMI, journal, defrag.exe). Remplacé par une fausse version dans les tests.</summary>
public interface IDiskSystem
{
    /// <summary>Lecteurs fixes avec une lettre.</summary>
    IReadOnlyList<VolumeInfo> Volumes();
    /// <summary>Date (heure locale) de la dernière optimisation réussie de chaque lecteur, d'après le journal de Windows.</summary>
    IReadOnlyDictionary<char, DateTime> LastOptimizations();
    /// <summary>Analyse (disque dur) : pourcentage de fragmentation.</summary>
    int FragmentationPercent(char letter);
    /// <summary>Lance defrag.exe avec ces arguments, transmet chaque ligne (ou mise à jour de ligne) de sa sortie, renvoie le code de sortie.
    /// L'annulation arrête defrag.exe.</summary>
    int RunDefrag(string arguments, Action<string> onOutput, CancellationToken cancel);
}

/// <summary>
/// « Optimiser les lecteurs » de Windows dans Unpark : TRIM pour un SSD, défragmentation pour un disque dur,
/// jamais de défragmentation sur un SSD. Entretien du disque : temps de chargement et longévité, aucun effet direct sur les FPS.
/// </summary>
public sealed class DiskOptimizer(IDiskSystem system, IActionLog log, Func<DateTime>? now = null)
{
    /// <summary>Au-delà, Windows conseille de défragmenter un disque dur.</summary>
    public const int FragmentationThreshold = 10;
    /// <summary>Un TRIM plus ancien que ça est conseillé (Windows le fait chaque semaine par défaut).</summary>
    public static readonly TimeSpan RetrimMaxAge = TimeSpan.FromDays(31);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.Now);

    public IReadOnlyList<VolumeInfo> Volumes() => system.Volumes().OrderBy(v => v.Letter).ToList();

    public IReadOnlyDictionary<char, DateTime> LastOptimizations()
    {
        try { return system.LastOptimizations(); }
        catch (Exception e)
        {
            log.Warn($"Dates de dernière optimisation illisibles : {e.Message}");
            return new Dictionary<char, DateTime>();
        }
    }

    /// <summary>Traitement pour ce type de support, ou null si le type est inconnu (on ne devine pas).</summary>
    public static OptimizeOperation? OperationFor(MediaKind media) => media switch
    {
        MediaKind.Ssd => OptimizeOperation.Retrim,
        MediaKind.Hdd => OptimizeOperation.Defrag,
        _ => null,
    };

    /// <summary>
    /// Arguments de defrag.exe : /L = TRIM, /D = défragmentation classique, /U = affiche l'avancement, /V = détails, /H = priorité normale.
    /// Un SSD ne reçoit JAMAIS /D.
    /// </summary>
    public static string DefragArguments(char letter, MediaKind media)
    {
        if (!char.IsAsciiLetter(letter)) throw new ArgumentException($"Lettre de lecteur invalide : {letter}", nameof(letter));
        var op = OperationFor(media) ?? throw new InvalidOperationException("Type de disque inconnu : Unpark ne choisit pas le traitement à l'aveugle.");
        var drive = $"{char.ToUpperInvariant(letter)}:";
        return op == OptimizeOperation.Retrim ? $"{drive} /L /U /V /H" : $"{drive} /D /U /V /H";
    }

    public static string OperationName(OptimizeOperation op) => op == OptimizeOperation.Retrim ? "TRIM" : "défragmentation";

    private static readonly Regex Percent = new(@"(\d{1,3})(?:[.,]\d+)?\s*%", RegexOptions.Compiled);

    /// <summary>Avancement dans une ligne de defrag.exe /U (« Retrim: 12% complete... », « Réajustement : 12 % effectué... »).</summary>
    public static OptimizeProgress? ParseProgress(string line)
    {
        var text = line.Trim();
        var matches = Percent.Matches(text);
        if (matches.Count == 0) return null;
        var value = int.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture);
        return value is < 0 or > 100 ? null : new OptimizeProgress(value, text);
    }

    public int Analyze(VolumeInfo volume)
    {
        if (volume.Media != MediaKind.Hdd) throw new InvalidOperationException("L'analyse ne concerne que les disques durs (comme dans l'outil Windows).");
        log.Info($"Analyse de la fragmentation de {volume.Letter}:");
        var percent = system.FragmentationPercent(volume.Letter);
        log.Info($"{volume.Letter}: {percent} % fragmenté");
        return percent;
    }

    public OptimizeResult Optimize(VolumeInfo volume, IProgress<OptimizeProgress>? progress, CancellationToken cancel)
    {
        if (OperationFor(volume.Media) is not { } op)
            return new(false, false, $"{volume.Letter}: type de disque inconnu : pas d'optimisation automatique (utilise l'outil Windows « Optimiser les lecteurs »).");

        var args = DefragArguments(volume.Letter, volume.Media);
        log.Info($"Optimisation de {volume.Letter}: ({volume.MediaText}) : {OperationName(op)} — defrag.exe {args}");
        var lastLine = "";
        int code;
        try
        {
            code = system.RunDefrag(args, line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                lastLine = line.Trim();
                if (ParseProgress(line) is { } p) progress?.Report(p);
            }, cancel);
        }
        catch (OperationCanceledException)
        {
            log.Info($"Optimisation de {volume.Letter}: arrêtée par l'utilisateur");
            return new(false, true, $"{volume.Letter}: optimisation arrêtée. Rien n'est abîmé : Windows reprendra lors d'une prochaine optimisation.");
        }
        catch (Exception e)
        {
            log.Error($"Optimisation de {volume.Letter}: impossible", e);
            return new(false, false, $"{volume.Letter}: optimisation impossible ({e.Message}).");
        }

        if (cancel.IsCancellationRequested)
        {
            log.Info($"Optimisation de {volume.Letter}: arrêtée par l'utilisateur");
            return new(false, true, $"{volume.Letter}: optimisation arrêtée. Rien n'est abîmé : Windows reprendra lors d'une prochaine optimisation.");
        }
        if (code != 0)
        {
            log.Warn($"Optimisation de {volume.Letter}: defrag.exe a renvoyé 0x{code:X8} ({lastLine})");
            return new(false, false, $"{volume.Letter}: Windows a refusé l'optimisation (code 0x{code:X8}).{(lastLine.Length > 0 ? $" {lastLine}" : "")}");
        }
        log.Info($"Optimisation de {volume.Letter}: terminée ({OperationName(op)})");
        return new(true, false, $"{volume.Letter}: {OperationName(op)} terminé.");
    }

    /// <summary>Texte « Dernière optimisation ».</summary>
    public static string LastText(DateTime? last, DateTime now)
    {
        if (last is not { } d) return "Inconnue";
        var days = (now.Date - d.Date).Days;
        return days switch
        {
            <= 0 => $"Aujourd'hui à {d:HH:mm}",
            1 => "Hier",
            _ => $"{d:dd/MM/yyyy} (il y a {days} jours)",
        };
    }

    /// <summary>Texte « État actuel », comme dans l'outil Windows.</summary>
    public static string StateText(MediaKind media, DateTime? last, int? fragmentation, DateTime now) => media switch
    {
        MediaKind.Unknown => "Type inconnu : non optimisé par Unpark",
        MediaKind.Ssd => last is { } d && now - d <= RetrimMaxAge ? "OK" : "Optimisation recommandée",
        _ => fragmentation switch
        {
            { } f when f >= FragmentationThreshold => $"{f} % fragmenté — optimisation recommandée",
            { } f => $"OK ({f} % fragmenté)",
            null => last is { } d && now - d <= RetrimMaxAge ? "OK" : "Analyse conseillée",
        },
    };

    /// <summary>L'état demande-t-il une optimisation (texte en orange) ?</summary>
    public static bool NeedsAttention(MediaKind media, DateTime? last, int? fragmentation, DateTime now) =>
        media != MediaKind.Unknown && !StateText(media, last, fragmentation, now).StartsWith("OK", StringComparison.Ordinal);

    public DateTime Now => _now();

    /// <summary>Type de support à partir de MSFT_PhysicalDisk : MediaType 3 = HDD, 4 = SSD ; non précisé mais NVMe (BusType 17) = SSD.</summary>
    public static (MediaKind Kind, string Text) Classify(int mediaType, int busType) => mediaType switch
    {
        4 => (MediaKind.Ssd, busType == 17 ? "SSD NVMe" : "SSD"),
        3 => (MediaKind.Hdd, "Disque dur"),
        _ when busType == 17 => (MediaKind.Ssd, "SSD NVMe"),
        _ => (MediaKind.Unknown, "Type inconnu"),
    };
}
