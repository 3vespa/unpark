using System.Text.Json;
using System.Text.RegularExpressions;
using Unpark.Core.Logging;

namespace Unpark.Core.Updates;

/// <summary>Version plus récente trouvée sur GitHub.</summary>
public sealed record UpdateInfo(string Version, string Tag, Uri ExeUrl, Uri SignatureUrl, long ExeSize);

/// <summary>Mise à jour téléchargée et vérifiée, prête à être installée (dans %AppData%\Unpark\updates).</summary>
public sealed record StagedUpdate(UpdateManifest Manifest, string ExePath, string SignaturePath)
{
    public string Version => Manifest.Version;
}

/// <summary>
/// Recherche et téléchargement des mises à jour, depuis les releases de 3vespa/unpark uniquement.
/// Rien n'est installé ici : voir UpdateInstaller.
/// </summary>
public sealed partial class UpdateService(IUpdateHttp http, IActionLog log, string updatesDir, string currentVersion, byte[]? publicKey = null)
{
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);
    private const long MaxSignatureBytes = 4 * 1024;
    private const long MaxExeBytes = 400L * 1024 * 1024;
    private const long MaxApiBytes = 1024 * 1024;

    private readonly byte[] _publicKey = publicKey ?? UpdateKeys.PublicKey;

    public string UpdatesDir => updatesDir;

    /// <summary>
    /// Lit la dernière release publique (5 s maximum). null si l'appli est à jour, sans Internet,
    /// ou si la release ne contient pas les fichiers attendus : l'échec est silencieux (simplement noté dans le journal).
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        try
        {
            var json = await http.GetStringAsync(UpdateKeys.LatestReleaseApi, CheckTimeout, MaxApiBytes, cancel);
            var info = ParseLatestRelease(json, currentVersion);
            log.Info(info is null ? $"Mises à jour : aucune version plus récente que {currentVersion}" : $"Mises à jour : v{info.Version} disponible");
            return info;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            log.Info($"Mises à jour : vérification impossible ({e.GetType().Name} : {e.Message})");
            return null;
        }
    }

    /// <summary>Réponse de l'API GitHub « releases/latest » → version à télécharger, ou null.</summary>
    public static UpdateInfo? ParseLatestRelease(string json, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (tag is null || !TagRegex().IsMatch(tag)) return null;
        var version = tag[1..];
        if (!UpdateVersion.IsNewer(version, currentVersion)) return null;

        var exeName = UpdateManifest.ExpectedFileName(version);
        var sigName = exeName + ".sig";
        long exeSize = 0;
        bool hasExe = false, hasSig = false;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name == exeName)
                {
                    hasExe = true;
                    exeSize = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0;
                }
                else if (name == sigName) hasSig = true;
            }
        }
        // Une release sans signature (ancienne, ou publiée à la main) n'est jamais installée automatiquement.
        if (!hasExe || !hasSig) return null;
        return new UpdateInfo(version, tag, UpdateKeys.AssetUrl(tag, exeName), UpdateKeys.AssetUrl(tag, sigName), exeSize);
    }

    /// <summary>
    /// Télécharge la signature puis l'exe, vérifie tout, et ne garde le fichier que s'il est valide.
    /// Lève UpdateRejectedException (message clair) si la signature, la version ou l'empreinte ne correspondent pas.
    /// </summary>
    public async Task<StagedUpdate> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(updatesDir);
        var sigText = await http.GetStringAsync(info.SignatureUrl, TimeSpan.FromSeconds(30), MaxSignatureBytes, cancel);
        var manifest = UpdateManifest.ParseAndVerify(sigText, _publicKey);
        if (manifest.Version != info.Version)
            throw new UpdateRejectedException($"La signature concerne la version {manifest.Version}, pas la {info.Version} : mise à jour refusée.");
        if (!UpdateVersion.IsNewer(manifest.Version, currentVersion))
            throw new UpdateRejectedException("Cette mise à jour n'est pas plus récente que la version installée : refusée.");

        var final = Path.Combine(updatesDir, manifest.FileName);
        var part = final + ".part";
        try
        {
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new Progress<long>(b => progress?.Report(Math.Clamp((double)b / manifest.Size, 0, 1)));
                await http.DownloadAsync(info.ExeUrl, file, Math.Min(manifest.Size, MaxExeBytes), bytes, cancel);
            }
            manifest.VerifyFile(part);
            DeleteStaged(keep: part); // une seule mise à jour prête à la fois
            Directory.CreateDirectory(updatesDir);
            File.Move(part, final, overwrite: true);
            var sigPath = final + ".sig";
            File.WriteAllText(sigPath, sigText);
            log.Info($"Mises à jour : v{manifest.Version} téléchargée et vérifiée (signature Ed25519 et SHA-256 {manifest.Sha256})");
            return new StagedUpdate(manifest, final, sigPath);
        }
        finally
        {
            TryDelete(part);
        }
    }

    /// <summary>
    /// Mise à jour déjà téléchargée, vérifiée À NOUVEAU (signature + empreinte) : le dossier %AppData% n'est pas une source de confiance.
    /// Supprime ce qui est périmé ou invalide. null s'il n'y a rien à installer.
    /// </summary>
    public StagedUpdate? FindStaged()
    {
        if (!Directory.Exists(updatesDir)) return null;
        foreach (var sig in Directory.EnumerateFiles(updatesDir, "Unpark-v*.exe.sig"))
        {
            var exe = sig[..^4];
            try
            {
                var manifest = UpdateManifest.ParseAndVerify(File.ReadAllText(sig), _publicKey);
                if (Path.GetFileName(exe) != manifest.FileName) throw new UpdateRejectedException("Nom de fichier inattendu.");
                if (!UpdateVersion.IsNewer(manifest.Version, currentVersion))
                {
                    log.Info($"Mises à jour : v{manifest.Version} téléchargée mais pas plus récente que {currentVersion}, supprimée");
                    DeleteStaged();
                    return null;
                }
                manifest.VerifyFile(exe);
                return new StagedUpdate(manifest, exe, sig);
            }
            catch (Exception e)
            {
                log.Warn($"Mises à jour : fichier en attente refusé et supprimé ({e.Message})");
                DeleteStaged();
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Prépare une mise à jour depuis un dossier local (Unpark-vX.Y.Z.exe + .sig), pour tester avant de publier.
    /// Exactement les mêmes vérifications qu'un téléchargement : signature, version plus récente, taille et SHA-256.
    /// </summary>
    public StagedUpdate StageFromFolder(string folder)
    {
        var sig = Directory.EnumerateFiles(folder, "Unpark-v*.exe.sig").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault()
                  ?? throw new UpdateRejectedException($"Aucun fichier Unpark-vX.Y.Z.exe.sig dans {folder}.");
        var sigText = File.ReadAllText(sig);
        var manifest = UpdateManifest.ParseAndVerify(sigText, _publicKey);
        if (!UpdateVersion.IsNewer(manifest.Version, currentVersion))
            throw new UpdateRejectedException($"La version {manifest.Version} n'est pas plus récente que la version installée ({currentVersion}).");
        var source = Path.Combine(folder, manifest.FileName);
        manifest.VerifyFile(source);

        DeleteStaged();
        Directory.CreateDirectory(updatesDir);
        var final = Path.Combine(updatesDir, manifest.FileName);
        File.Copy(source, final + ".part", overwrite: true);
        try
        {
            manifest.VerifyFile(final + ".part");
            File.Move(final + ".part", final, overwrite: true);
        }
        finally { TryDelete(final + ".part"); }
        File.WriteAllText(final + ".sig", sigText);
        log.Info($"Mises à jour : v{manifest.Version} préparée depuis le dossier {folder}");
        return new StagedUpdate(manifest, final, final + ".sig");
    }

    /// <summary>Supprime les mises à jour téléchargées (rien d'autre dans %AppData%\Unpark n'est touché).</summary>
    public void DeleteStaged(string? keep = null)
    {
        if (!Directory.Exists(updatesDir)) return;
        foreach (var f in Directory.EnumerateFiles(updatesDir))
            if (keep is null || !string.Equals(Path.GetFullPath(f), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) TryDelete(f);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* fichier occupé : supprimé une prochaine fois */ }
    }

    [GeneratedRegex(@"^v\d{1,5}(\.\d{1,5}){1,3}$")]
    private static partial Regex TagRegex();
}
