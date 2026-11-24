using Unpark.Core.Backup;
using Unpark.Core.Commands;
using Unpark.Core.Settings;

namespace Unpark.Core.Power;

/// <summary>État d'un paramètre du plan Unpark, pour l'affichage.</summary>
public sealed record PowerSettingState(PowerSettingDef Def, uint? CurrentAc, uint? CurrentDc)
{
    public bool Exists => CurrentAc is not null;
    public bool Ok => CurrentAc == Def.Value;
}

public sealed record PowerPlanStatus(
    Guid ActiveScheme,
    string ActiveName,
    bool UnparkInstalled,
    bool UnparkActive,
    string? PreviousName,
    bool IsLaptop,
    IReadOnlyList<PowerSettingState> Settings);

/// <summary>
/// Plan « Unpark Performance » : copie de Performances optimales (ou Performances élevées, ou Équilibré en dernier recours),
/// renommée, puis réglée pour le jeu. Sur portable, les valeurs agressives ne s'appliquent que sur secteur :
/// sur batterie, le plan garde les valeurs du plan Équilibré.
///
/// Sauvegarde (backup.json, réglage « power.unpark ») : le plan actif d'avant, si le plan Unpark existait déjà,
/// et la visibilité d'origine des paramètres cachés qu'on rend visibles.
/// </summary>
public sealed class UnparkPowerPlan(SettingContext ctx, IPowerApi power, ICommandRunner commands) : ISetting
{
    public const string SettingId = "power.unpark";
    private const string PreviousKey = "text:power.previousScheme";
    private const string ExistedKey = "text:power.unparkExisted";
    private const string AttrPrefix = "text:power.hidden:";

    public string Id => SettingId;
    public string Name => "Plan d'alimentation Unpark Performance";
    public string Description => "Cœurs toujours actifs, processeur à 100 %, boost agressif, pas de mise en veille sur secteur.";
    public bool RequiresRestart => false;

    private static Guid U => PowerSettings.Unpark;

    public bool IsInstalled() => power.ListSchemes().Contains(U);

    /// <summary>Le plan Unpark est installé, actif, et tous ses réglages sont en place.</summary>
    public bool IsApplied() =>
        power.GetActiveScheme() == U &&
        PowerSettings.All.All(d => power.ReadAc(U, d.SubGroup, d.Setting) is not { } v || v == d.Value);

    public PowerPlanStatus GetStatus()
    {
        var active = power.GetActiveScheme();
        var installed = IsInstalled();
        var previous = ctx.Backup.GetOriginal(Id, PreviousKey)?.Text is { } p && Guid.TryParse(p, out var g) ? g : (Guid?)null;
        var states = PowerSettings.All
            .Select(d => installed
                ? new PowerSettingState(d, power.ReadAc(U, d.SubGroup, d.Setting), power.ReadDc(U, d.SubGroup, d.Setting))
                : new PowerSettingState(d, null, null))
            .Where(s => !s.Def.Optional || s.Exists)
            .ToList();
        return new PowerPlanStatus(
            active,
            power.GetSchemeName(active) ?? active.ToString(),
            installed,
            active == U,
            previous is { } pg ? power.GetSchemeName(pg) ?? pg.ToString() : null,
            power.HasBattery(),
            states);
    }

    /// <summary>« Installer et activer ».</summary>
    public void Apply()
    {
        var active = power.GetActiveScheme();
        // 1. Sauvegardes AVANT toute modification (jamais écrasées si déjà présentes).
        if (active != U)
            ctx.Backup.RecordOriginal(Id, Name, PreviousKey, BackupValue.OfText(active.ToString()));
        var installed = IsInstalled();
        ctx.Backup.RecordOriginal(Id, Name, ExistedKey, BackupValue.OfText(installed ? "oui" : "non"));

        // 2. Création du plan.
        if (!installed)
        {
            var source = Duplicate();
            Powercfg($"/changename {U} \"{PowerSettings.UnparkName}\" \"{PowerSettings.UnparkDescription}\"");
            ctx.Log.Info($"[{Name}] plan créé à partir de « {power.GetSchemeName(source) ?? source.ToString()} »");
        }

        // 3. Réglages.
        var laptop = power.HasBattery();
        if (laptop) ctx.Log.Info($"[{Name}] portable détecté : réglages agressifs sur secteur seulement, valeurs du plan Équilibré sur batterie");
        foreach (var def in PowerSettings.All)
        {
            if (power.ReadAc(U, def.SubGroup, def.Setting) is null)
            {
                if (def.Optional) ctx.Log.Info($"[{Name}] « {def.Label} » absent de ce PC : ignoré");
                else ctx.Log.Warn($"[{Name}] « {def.Label} » introuvable sur ce PC : ignoré");
                continue;
            }

            if (def.HiddenByDefault && power.IsHidden(def.SubGroup, def.Setting))
            {
                ctx.Backup.RecordOriginal(Id, Name, AttrPrefix + $"{def.SubGroup}/{def.Setting}", BackupValue.OfText("caché"));
                Powercfg($"-attributes {def.SubArg} {def.SettingArg} -ATTRIB_HIDE");
            }

            Powercfg($"/setacvalueindex {U} {def.SubArg} {def.SettingArg} {def.Value}");
            var dc = laptop ? power.ReadDc(PowerSettings.Balanced, def.SubGroup, def.Setting) : def.Value;
            if (dc is { } dcValue) Powercfg($"/setdcvalueindex {U} {def.SubArg} {def.SettingArg} {dcValue}");
        }

        // 4. Activation (nécessaire aussi pour que les nouvelles valeurs du plan actif soient prises en compte).
        Powercfg($"/setactive {U}");
        if (power.GetActiveScheme() != U) throw new InvalidOperationException("Windows n'a pas activé le plan Unpark Performance.");
    }

    /// <summary>« Revenir à mon ancien plan » : réactive le plan d'avant, en gardant le plan Unpark installé.</summary>
    public string SwitchToPrevious()
    {
        var target = PreviousOrBalanced(out var fallback);
        Powercfg($"/setactive {target}");
        var name = power.GetSchemeName(target) ?? target.ToString();
        ctx.Log.Info($"[{Name}] retour au plan « {name} »{(fallback ? " (ancien plan introuvable : Équilibré)" : "")}");
        return name;
    }

    /// <summary>« Supprimer le plan Unpark » : revient à l'ancien plan, supprime le plan Unpark, recache les paramètres.</summary>
    public void Revert()
    {
        if (power.GetActiveScheme() == U) SwitchToPrevious();

        var existed = ctx.Backup.GetOriginal(Id, ExistedKey)?.Text == "oui";
        if (IsInstalled() && !existed)
        {
            Powercfg($"/delete {U}");
            ctx.Log.Info($"[{Name}] plan Unpark supprimé");
        }

        foreach (var def in PowerSettings.All)
        {
            if (ctx.Backup.GetOriginal(Id, AttrPrefix + $"{def.SubGroup}/{def.Setting}")?.Text == "caché")
                Powercfg($"-attributes {def.SubArg} {def.SettingArg} +ATTRIB_HIDE");
        }
        ctx.Backup.RemoveEntry(Id);
    }

    /// <summary>Plan actif d'avant Unpark s'il existe encore, sinon Équilibré.</summary>
    private Guid PreviousOrBalanced(out bool fallback)
    {
        var text = ctx.Backup.GetOriginal(Id, PreviousKey)?.Text;
        if (Guid.TryParse(text, out var previous) && previous != U && power.ListSchemes().Contains(previous))
        {
            fallback = false;
            return previous;
        }
        fallback = true;
        return PowerSettings.Balanced;
    }

    /// <summary>Copie Performances optimales, sinon Performances élevées, sinon Équilibré (PC en veille moderne).</summary>
    private Guid Duplicate()
    {
        foreach (var source in new[] { PowerSettings.Ultimate, PowerSettings.HighPerformance, PowerSettings.Balanced })
        {
            var result = Run($"/duplicatescheme {source} {U}");
            if (result.Ok && IsInstalled()) return source;
            ctx.Log.Info($"[{Name}] copie de {source} impossible : {result.Text}");
        }
        throw new InvalidOperationException("Impossible de créer le plan : Windows a refusé de copier les plans existants.");
    }

    private CommandResult Run(string args)
    {
        var result = commands.Run("powercfg.exe", args);
        ctx.Log.Info($"[{Name}] powercfg {args} → {(result.Ok ? "OK" : $"code {result.ExitCode} {result.Text}")}");
        return result;
    }

    private void Powercfg(string args)
    {
        var result = Run(args);
        if (!result.Ok) throw new InvalidOperationException($"powercfg {args} a échoué : {result.Text}");
    }
}
