using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Unpark.Core.Backup;
using Unpark.Core.Gaming;
using Unpark.Core.Settings;

namespace Unpark.Core.Disks;

/// <summary>État de la tâche Windows « \Microsoft\Windows\Defrag\ScheduledDefrag ».</summary>
/// <param name="Period">Fréquence au format ISO 8601 de l'Entretien automatique (P1D, P7D, P1M…), ou null.</param>
public sealed record ScheduledState(bool Enabled, string? Period);

/// <summary>Tâche planifiée d'optimisation de Windows. Remplacé par une fausse version dans les tests.</summary>
public interface IScheduledOptimizationApi
{
    /// <summary>null si la tâche n'existe pas sur ce PC.</summary>
    ScheduledState? Read();
    void SetEnabled(bool enabled);
}

/// <summary>
/// « Optimisation planifiée » de Windows (celle de l'outil « Optimiser les lecteurs », activée par défaut).
/// Unpark propose seulement de la réactiver si elle est coupée ; « Annuler » / « Tout annuler » remet l'état d'origine.
/// </summary>
public sealed class ScheduledOptimizationSetting(SettingContext ctx, IScheduledOptimizationApi api) : ISetting, IConditionalSetting
{
    private const string EnabledKey = "text:disk.scheduledEnabled";

    public string Id => "disk.scheduled";
    public string Name => "Optimisation planifiée de Windows";
    public string Description => "Windows optimise tes lecteurs automatiquement (TRIM des SSD, défragmentation des disques durs) pendant l'entretien automatique. Activée par défaut sur Windows ; à réactiver si elle a été coupée.";
    public bool RequiresRestart => false;

    public string? UnavailableReason() => api.Read() is null ? "La tâche d'optimisation planifiée de Windows est introuvable sur ce PC." : null;

    public bool IsApplied() => api.Read()?.Enabled == true;

    public void Apply()
    {
        var state = api.Read() ?? throw new InvalidOperationException("Tâche d'optimisation planifiée introuvable.");
        ctx.Backup.RecordOriginal(Id, Name, EnabledKey, BackupValue.OfText(state.Enabled ? "oui" : "non"));
        if (!state.Enabled) api.SetEnabled(true);
        ctx.Log.Info($"[{Name}] activée (avant : {(state.Enabled ? "activée" : "désactivée")})");
    }

    public void Revert()
    {
        if (ctx.Backup.Get(Id) is null)
        {
            ctx.Log.Info($"[{Name}] rien à annuler (aucune sauvegarde)");
            return;
        }
        if (ctx.Backup.GetOriginal(Id, EnabledKey)?.Text == "non")
        {
            api.SetEnabled(false);
            ctx.Log.Info($"[{Name}] désactivée, comme avant");
        }
        ctx.Backup.RemoveEntry(Id);
    }

    /// <summary>Texte affiché : « Activée — chaque semaine », « Désactivée »…</summary>
    public string StatusText()
    {
        var state = api.Read();
        if (state is null) return "Introuvable sur ce PC";
        if (!state.Enabled) return "Désactivée";
        return FrequencyText(state.Period) is { } f ? $"Activée — {f}" : "Activée";
    }

    /// <summary>« P7D » → « chaque semaine ». null si la fréquence est absente ou illisible.</summary>
    public static string? FrequencyText(string? period)
    {
        if (string.IsNullOrWhiteSpace(period)) return null;
        var m = Regex.Match(period.Trim(), @"^P(?:(\d+)M)?(?:(\d+)W)?(?:(\d+)D)?$", RegexOptions.IgnoreCase);
        if (!m.Success || m.Length <= 1) return null;
        int months = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
        int weeks = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        int days = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        if (months == 1 && weeks == 0 && days == 0) return "chaque mois";
        if (months > 1 && weeks == 0 && days == 0) return $"tous les {months} mois";
        if (months > 0) return null;
        var total = weeks * 7 + days;
        return total switch
        {
            1 => "chaque jour",
            7 => "chaque semaine",
            >= 28 and <= 31 => "chaque mois",
            > 1 when total % 7 == 0 => $"toutes les {total / 7} semaines",
            > 1 => $"tous les {total} jours",
            _ => null,
        };
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsScheduledOptimizationApi : IScheduledOptimizationApi
{
    private const string Folder = @"\Microsoft\Windows\Defrag";
    private const string TaskName = "ScheduledDefrag";

    public ScheduledState? Read()
    {
        dynamic service = Connect();
        try
        {
            dynamic task;
            try { task = service.GetFolder(Folder).GetTask(TaskName); }
            catch (Exception e) when (e is COMException or FileNotFoundException) { return null; }
            string? period = null;
            try { period = (string?)task.Definition.Settings.MaintenanceSettings?.Period; }
            catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { } // Windows sans entretien automatique
            return new ScheduledState((bool)task.Enabled, string.IsNullOrWhiteSpace(period) ? null : period);
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }

    public void SetEnabled(bool enabled)
    {
        dynamic service = Connect();
        try { service.GetFolder(Folder).GetTask(TaskName).Enabled = enabled; }
        finally { Marshal.FinalReleaseComObject(service); }
    }

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Planificateur de tâches indisponible.");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }
}
