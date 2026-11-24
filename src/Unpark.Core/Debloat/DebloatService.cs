using System.Text.Json;
using Unpark.Core.Logging;

namespace Unpark.Core.Debloat;

/// <summary>Appli installée (paquet Appx/MSIX) pour l'utilisateur courant.</summary>
public sealed record AppxPackageInfo(
    string Name, string FamilyName, string FullName, string DisplayName, bool IsFramework, bool IsResource, bool IsSystem);

/// <summary>Gestion des applis Windows. Implémenté avec PackageManager dans l'appli, remplacé par une fausse version dans les tests.</summary>
public interface IAppxApi
{
    IReadOnlyList<AppxPackageInfo> ListForCurrentUser();
    /// <summary>Retire l'appli pour l'utilisateur courant. Lève une erreur (message Windows) en cas d'échec.</summary>
    void Remove(string fullName);
}

public sealed record DebloatCandidate(AppxPackageInfo Package, string DisplayName, AppPolicy Policy)
{
    public bool DefaultChecked => Policy == AppPolicy.Recommended;
}

/// <summary>Appli retirée par Unpark (réinstallable depuis le Microsoft Store).</summary>
public sealed record RemovedApp(string Name, string FamilyName, string DisplayName, DateTime RemovedUtc)
{
    public string StoreUrl => $"ms-windows-store://pdp/?PFN={FamilyName}";
}

public sealed record DebloatResult(DebloatCandidate App, bool Success, string? Error);

/// <summary>
/// Debloat : retire des applis préinstallées pour l'utilisateur courant seulement.
/// Pas d'« Annuler » possible (une appli retirée doit être réinstallée) : chaque retrait est noté dans
/// %AppData%\Unpark\debloat.json avec le lien Microsoft Store pour la réinstaller.
/// </summary>
public sealed class DebloatService(IAppxApi appx, IActionLog log, string historyFile)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Applis proposées : sans les composants système, frameworks et applis de la liste « jamais ».</summary>
    public IReadOnlyList<DebloatCandidate> Candidates()
    {
        return appx.ListForCurrentUser()
            .Where(p => !p.IsFramework && !p.IsResource && !p.IsSystem)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(p =>
            {
                var entry = AppCatalog.Find(p.Name);
                var name = entry?.FrenchName ?? (string.IsNullOrWhiteSpace(p.DisplayName) || p.DisplayName.StartsWith("ms-resource:") ? p.Name : p.DisplayName);
                return new DebloatCandidate(p, name, entry?.Policy ?? AppPolicy.Optional);
            })
            .Where(c => c.Policy != AppPolicy.Never)
            .OrderBy(c => c.Policy)
            .ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<DebloatResult> Remove(IEnumerable<DebloatCandidate> apps)
    {
        var results = new List<DebloatResult>();
        foreach (var app in apps)
        {
            // Double sécurité : une appli « jamais » ne peut pas être retirée, même si elle arrive ici.
            if (AppCatalog.Find(app.Package.Name)?.Policy == AppPolicy.Never)
            {
                results.Add(new(app, false, "Appli protégée : jamais retirée."));
                continue;
            }
            try
            {
                appx.Remove(app.Package.FullName);
                AddToHistory(new RemovedApp(app.Package.Name, app.Package.FamilyName, app.DisplayName, DateTime.UtcNow));
                log.Info($"Debloat : « {app.DisplayName} » ({app.Package.FullName}) retirée pour l'utilisateur courant");
                results.Add(new(app, true, null));
            }
            catch (Exception e)
            {
                log.Error($"Debloat : « {app.DisplayName} » non retirée", e);
                results.Add(new(app, false, e.Message));
            }
        }
        return results;
    }

    public IReadOnlyList<RemovedApp> History()
    {
        try
        {
            return File.Exists(historyFile)
                ? JsonSerializer.Deserialize<List<RemovedApp>>(File.ReadAllText(historyFile), Json) ?? []
                : [];
        }
        catch (Exception e)
        {
            log.Warn($"Historique du Debloat illisible : {e.Message}");
            return [];
        }
    }

    private void AddToHistory(RemovedApp app)
    {
        var list = History().Where(a => !a.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)).Append(app).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(historyFile)!);
        var tmp = historyFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
        File.Move(tmp, historyFile, overwrite: true);
    }
}
