using Unpark.Core.Logging;

namespace Unpark.Core.Cleanup;

/// <summary>Parties du nettoyage propres à Windows (corbeille, services). Remplacé par une fausse version dans les tests.</summary>
public interface ICleanupSystem
{
    (long Bytes, long Items) QueryRecycleBin();
    void EmptyRecycleBin();
    bool IsServiceRunning(string name);
    /// <summary>Arrête le service et attend son arrêt. Lève une erreur en cas d'échec.</summary>
    void StopService(string name);
    void StartService(string name);
}

public sealed record CleanupPaths(string UserTemp, string WindowsTemp, string UpdateDownload, string ThumbnailDir)
{
    public static CleanupPaths Default
    {
        get
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return new(
                Path.GetTempPath(),
                Path.Combine(windows, "Temp"),
                Path.Combine(windows, "SoftwareDistribution", "Download"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer"));
        }
    }
}

public enum CleanupKind { Folder, WindowsUpdate, RecycleBin, Thumbnails }

public sealed record CleanupTarget(string Id, string Label, string Description, CleanupKind Kind, string? Folder);

public sealed record CleanupScan(CleanupTarget Target, long Bytes, long Files);

public sealed record CleanupResult(CleanupTarget Target, long FreedBytes, long Deleted, long Skipped, string? Error);

/// <summary>
/// Nettoyage : %TEMP%, C:\Windows\Temp, cache de Windows Update, corbeille, cache des miniatures.
/// Sécurité : fichiers verrouillés ignorés sans planter ; liens et jonctions jamais suivis ; fichiers temporaires de moins
/// de 2 heures épargnés (installation en cours) ; Windows Update seulement arrêté le temps du nettoyage, puis toujours relancé.
/// </summary>
public sealed class CleanupService(ICleanupSystem system, IActionLog log, CleanupPaths paths, Func<DateTime>? utcNow = null)
{
    public static readonly TimeSpan TempMinAge = TimeSpan.FromHours(2);
    /// <summary>Services arrêtés pendant le nettoyage du cache de Windows Update (jamais désactivés).</summary>
    public static readonly string[] UpdateServices = ["wuauserv", "bits"];

    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    public IReadOnlyList<CleanupTarget> Targets =>
    [
        new("temp-user", "Fichiers temporaires (utilisateur)", "Dossier %TEMP% : fichiers laissés par les programmes et les installations.", CleanupKind.Folder, paths.UserTemp),
        new("temp-windows", "Fichiers temporaires de Windows", "Dossier C:\\Windows\\Temp.", CleanupKind.Folder, paths.WindowsTemp),
        new("wu-cache", "Cache de Windows Update", "Mises à jour déjà installées, encore stockées. Windows Update est mis en pause le temps du nettoyage, puis relancé.", CleanupKind.WindowsUpdate, paths.UpdateDownload),
        new("recycle", "Corbeille", "Vide la corbeille de tous les disques.", CleanupKind.RecycleBin, null),
        new("thumbs", "Cache des miniatures", "Aperçus d'images de l'Explorateur, recréés automatiquement.", CleanupKind.Thumbnails, paths.ThumbnailDir),
    ];

    public IReadOnlyList<CleanupScan> Scan() => Targets.Select(ScanOne).ToList();

    private CleanupScan ScanOne(CleanupTarget t)
    {
        try
        {
            if (t.Kind == CleanupKind.RecycleBin)
            {
                var (bytes, items) = system.QueryRecycleBin();
                return new CleanupScan(t, bytes, items);
            }
            long size = 0, count = 0;
            foreach (var file in Files(t))
            {
                size += file.Length;
                count++;
            }
            return new CleanupScan(t, size, count);
        }
        catch (Exception e)
        {
            log.Warn($"Analyse de « {t.Label} » impossible : {e.Message}");
            return new CleanupScan(t, 0, 0);
        }
    }

    public IReadOnlyList<CleanupResult> Clean(IEnumerable<string> targetIds)
    {
        var ids = targetIds.ToHashSet();
        var results = new List<CleanupResult>();
        foreach (var t in Targets.Where(t => ids.Contains(t.Id)))
        {
            log.Info($"Nettoyage : {t.Label}");
            var result = t.Kind switch
            {
                CleanupKind.RecycleBin => CleanRecycleBin(t),
                CleanupKind.WindowsUpdate => CleanWindowsUpdate(t),
                _ => CleanFiles(t),
            };
            log.Info($"Nettoyage « {t.Label} » : {Format.Bytes(result.FreedBytes)} libérés, {result.Deleted} fichier(s) supprimé(s), {result.Skipped} ignoré(s){(result.Error is null ? "" : $" — {result.Error}")}");
            results.Add(result);
        }
        return results;
    }

    private CleanupResult CleanRecycleBin(CleanupTarget t)
    {
        try
        {
            var (before, items) = system.QueryRecycleBin();
            if (items == 0) return new(t, 0, 0, 0, null);
            system.EmptyRecycleBin();
            var (after, left) = system.QueryRecycleBin();
            return new(t, Math.Max(0, before - after), items - left, left, null);
        }
        catch (Exception e)
        {
            return new(t, 0, 0, 0, $"Corbeille non vidée : {e.Message}");
        }
    }

    private CleanupResult CleanWindowsUpdate(CleanupTarget t)
    {
        var stopped = new List<string>();
        try
        {
            foreach (var service in UpdateServices)
            {
                if (!system.IsServiceRunning(service)) continue;
                system.StopService(service);
                stopped.Add(service);
                log.Info($"Service {service} arrêté le temps du nettoyage");
            }
        }
        catch (Exception e)
        {
            RestartServices(stopped);
            return new(t, 0, 0, 0, $"Windows Update n'a pas pu être mis en pause ({e.Message}) : cache non nettoyé.");
        }

        try
        {
            return CleanFiles(t);
        }
        finally
        {
            RestartServices(stopped);
        }
    }

    private void RestartServices(List<string> stopped)
    {
        // Ordre inverse : bits puis wuauserv. On ne relance que ce qui tournait avant.
        foreach (var service in Enumerable.Reverse(stopped))
        {
            try
            {
                system.StartService(service);
                log.Info($"Service {service} relancé");
            }
            catch (Exception e)
            {
                log.Error($"Service {service} non relancé (il redémarrera avec Windows)", e);
            }
        }
    }

    private CleanupResult CleanFiles(CleanupTarget t)
    {
        long freed = 0, deleted = 0, skipped = 0;
        foreach (var file in Files(t).ToList())
        {
            try
            {
                var size = file.Length;
                if (file.Attributes.HasFlag(FileAttributes.ReadOnly)) file.Attributes = FileAttributes.Normal;
                file.Delete();
                freed += size;
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                skipped++; // fichier verrouillé ou protégé : ignoré
            }
        }
        if (t.Kind != CleanupKind.Thumbnails && t.Folder is not null) RemoveEmptyDirectories(t.Folder);
        return new(t, freed, deleted, skipped, null);
    }

    /// <summary>Fichiers à supprimer pour une cible (sans suivre les liens, en ignorant les dossiers illisibles).</summary>
    private IEnumerable<FileInfo> Files(CleanupTarget t)
    {
        if (t.Folder is null || !Directory.Exists(t.Folder)) return [];
        if (t.Kind == CleanupKind.Thumbnails)
            return new DirectoryInfo(t.Folder).EnumerateFiles("thumbcache_*.db", SearchOption.TopDirectoryOnly);

        var minAge = t.Kind == CleanupKind.Folder ? TempMinAge : TimeSpan.Zero;
        var limit = _utcNow() - minAge;
        return Walk(new DirectoryInfo(t.Folder)).Where(f => f.LastWriteTimeUtc <= limit);
    }

    private static IEnumerable<FileInfo> Walk(DirectoryInfo dir)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
        };
        IEnumerable<FileSystemInfo> entries;
        try { entries = dir.EnumerateFileSystemInfos("*", options).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }

        foreach (var entry in entries)
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint) || entry.LinkTarget is not null) continue; // lien : jamais suivi
            if (entry is FileInfo file) yield return file;
            else if (entry is DirectoryInfo sub)
                foreach (var f in Walk(sub)) yield return f;
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var dir in SafeSubdirectories(new DirectoryInfo(root)).OrderByDescending(d => d.FullName.Length))
        {
            try
            {
                if (!dir.EnumerateFileSystemInfos().Any()) dir.Delete();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<DirectoryInfo> SafeSubdirectories(DirectoryInfo dir)
    {
        IEnumerable<DirectoryInfo> subs;
        try { subs = dir.EnumerateDirectories().ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var sub in subs)
        {
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint) || sub.LinkTarget is not null) continue;
            yield return sub;
            foreach (var deeper in SafeSubdirectories(sub)) yield return deeper;
        }
    }
}
