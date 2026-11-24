using Unpark.Core.Backup;
using Unpark.Core.Registry;

namespace Unpark.Core.Settings;

/// <summary>Une valeur de registre à mettre dans un état voulu.</summary>
public sealed record RegistryChange(RegPath Path, RegValue Desired);

/// <summary>Réglage fait d'une ou plusieurs valeurs de registre. Sauvegarde chaque valeur (ou son absence) avant de l'écrire.</summary>
public class RegistrySetting(
    SettingContext ctx,
    string id,
    string name,
    string description,
    bool requiresRestart,
    IReadOnlyList<RegistryChange> changes) : ISetting
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public bool RequiresRestart { get; } = requiresRestart;
    public IReadOnlyList<RegistryChange> Changes { get; } = changes;

    public virtual bool IsApplied() => Changes.All(c => c.Desired.SameAs(ctx.Registry.Get(c.Path)));

    public virtual void Apply()
    {
        foreach (var change in Changes)
        {
            var current = ctx.Registry.Get(change.Path);
            // 1. Sauvegarde (écrite sur disque) AVANT toute modification.
            ctx.Backup.RecordOriginal(Id, Name, change.Path.BackupKey, BackupValue.Of(current));
            if (change.Desired.SameAs(current))
            {
                ctx.Log.Info($"[{Name}] {change.Path} déjà à {change.Desired}");
                continue;
            }
            // 2. Modification.
            ctx.Registry.Set(change.Path, change.Desired);
            ctx.Log.Info($"[{Name}] {change.Path} : {(current is null ? "(absente)" : current.ToString())} → {change.Desired}");
        }
    }

    public virtual void Revert() => RevertFromBackup(ctx, Id, Name);

    /// <summary>
    /// Remet toutes les valeurs de registre sauvegardées pour ce réglage, puis efface sa sauvegarde.
    /// Utilisable aussi pour un réglage inconnu de cette version (la sauvegarde contient le chemin complet).
    /// </summary>
    internal static void RevertFromBackup(SettingContext ctx, string id, string name)
    {
        var entry = ctx.Backup.Get(id);
        if (entry is null)
        {
            ctx.Log.Info($"[{name}] rien à annuler (aucune sauvegarde)");
            return;
        }
        foreach (var (key, original) in entry.Originals)
        {
            var path = RegPath.FromBackupKey(key);
            if (path is null) continue; // valeur non registre : gérée par le réglage lui-même
            var current = ctx.Registry.Get(path);
            if (!original.Exists)
            {
                if (current is not null) ctx.Registry.Delete(path);
                ctx.Log.Info($"[{name}] {path} : {current?.ToString() ?? "(absente)"} → (supprimée, comme avant)");
            }
            else if (original.Registry is { } value)
            {
                if (!value.SameAs(current)) ctx.Registry.Set(path, value);
                ctx.Log.Info($"[{name}] {path} : {current?.ToString() ?? "(absente)"} → {value} (valeur d'origine)");
            }
        }
        ctx.Backup.RemoveEntry(id);
    }
}
