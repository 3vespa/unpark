using System.Globalization;
using Unpark.Core.Backup;
using Unpark.Core.Diagnostics;
using Unpark.Core.Settings;

namespace Unpark.Core.Display;

/// <summary>Écrans et changement de fréquence (remplacé par une fausse version dans les tests).</summary>
public interface IDisplayModeApi
{
    /// <summary>Écrans allumés, avec leur fréquence actuelle et la plus haute disponible à la résolution actuelle.</summary>
    IReadOnlyList<DisplayData> Displays();

    /// <summary>Change la fréquence d'un écran sans toucher à la résolution. Lève une exception claire en cas d'échec.</summary>
    void SetRefreshRate(string deviceName, int width, int height, int hz);
}

/// <summary>Un changement de fréquence en attente de confirmation (« Garder cette fréquence ? »).</summary>
public sealed record RefreshRateTrial(string DeviceName, string DisplayName, int Width, int Height, int PreviousHz, int NewHz, bool CreatedBackup);

/// <summary>
/// Changement de fréquence en un clic depuis le Diagnostic, avec le même filet de sécurité que Windows :
/// Start() change la fréquence, puis l'appli demande « Garder cette fréquence ? » ; sans réponse, Undo() remet la précédente.
/// La fréquence d'avant Unpark est sauvegardée (backup.json) : « Tout annuler » sait la remettre (RefreshRateSetting).
/// </summary>
public sealed class RefreshRateChanger(SettingContext ctx, IDisplayModeApi api)
{
    /// <summary>Change la fréquence. Lève une exception si c'est impossible (rien n'est alors modifié).</summary>
    public RefreshRateTrial Start(string deviceName, string displayName, int targetHz)
    {
        var display = api.Displays().FirstOrDefault(d => d.DeviceName == deviceName)
                      ?? throw new InvalidOperationException($"{displayName} introuvable : est-il toujours branché ? Relance le diagnostic.");
        if (targetHz > display.MaxHz)
            throw new InvalidOperationException($"{displayName} ne propose pas {targetHz} Hz en {display.Width}×{display.Height}. Relance le diagnostic.");

        var id = RefreshRateSetting.IdFor(deviceName);
        var createdBackup = ctx.Backup.Get(id) is null;
        // Valeur d'origine enregistrée AVANT le changement (jamais écrasée : c'est celle d'avant Unpark).
        var name = RefreshRateSetting.NameFor(displayName);
        ctx.Backup.RecordOriginal(id, name, RefreshRateSetting.HzKey, BackupValue.OfText(display.CurrentHz.ToString(CultureInfo.InvariantCulture)));
        ctx.Backup.RecordOriginal(id, name, RefreshRateSetting.ModeKey, BackupValue.OfText(RefreshRateSetting.ModeText(display.Width, display.Height)));
        try
        {
            api.SetRefreshRate(deviceName, display.Width, display.Height, targetHz);
        }
        catch (Exception e)
        {
            ctx.Log.Error($"[{name}] passage à {targetHz} Hz impossible", e);
            if (createdBackup) ctx.Backup.RemoveEntry(id);
            throw;
        }
        ctx.Log.Info($"[{name}] {display.CurrentHz} Hz → {targetHz} Hz en {display.Width}×{display.Height}, en attente de confirmation");
        return new RefreshRateTrial(deviceName, displayName, display.Width, display.Height, display.CurrentHz, targetHz, createdBackup);
    }

    public void Keep(RefreshRateTrial trial) =>
        ctx.Log.Info($"[{RefreshRateSetting.NameFor(trial.DisplayName)}] {trial.NewHz} Hz gardés (annulable avec « Tout annuler »)");

    /// <summary>
    /// Remet la fréquence d'avant CE changement (pas forcément celle d'avant Unpark, si une autre avait déjà été gardée).
    /// Lève une exception si Windows refuse.
    /// </summary>
    public void Undo(RefreshRateTrial trial, string reason)
    {
        var name = RefreshRateSetting.NameFor(trial.DisplayName);
        api.SetRefreshRate(trial.DeviceName, trial.Width, trial.Height, trial.PreviousHz);
        if (trial.CreatedBackup) ctx.Backup.RemoveEntry(RefreshRateSetting.IdFor(trial.DeviceName));
        ctx.Log.Info($"[{name}] retour à {trial.PreviousHz} Hz ({reason})");
    }

    /// <summary>Réglage retrouvé à partir de son identifiant dans backup.json (pour « Tout annuler »), ou null.</summary>
    public ISetting? Resolve(string settingId) =>
        settingId.StartsWith(RefreshRateSetting.IdPrefix, StringComparison.Ordinal)
            ? new RefreshRateSetting(ctx, api, settingId[RefreshRateSetting.IdPrefix.Length..])
            : null;
}

/// <summary>
/// Fréquence d'un écran changée par Unpark. Seul Revert() sert (via « Tout annuler ») : il remet la fréquence d'avant Unpark.
/// Si l'écran a disparu ou si sa résolution a changé depuis, rien n'est forcé : la sauvegarde est simplement retirée.
/// </summary>
public sealed class RefreshRateSetting(SettingContext ctx, IDisplayModeApi api, string deviceName) : ISetting
{
    public const string IdPrefix = "display.refresh:";
    internal const string HzKey = "text:display.hz";
    internal const string ModeKey = "text:display.mode";

    public static string IdFor(string deviceName) => IdPrefix + deviceName;
    public static string NameFor(string displayName) => $"Fréquence de l'écran ({displayName})";
    public static string ModeText(int width, int height) => $"{width}x{height}";

    public string Id => IdFor(deviceName);
    public string Name => ctx.Backup.Get(Id)?.SettingName ?? NameFor(deviceName);
    public string Description => "Fréquence d'actualisation de l'écran.";
    public bool RequiresRestart => false;

    public bool IsApplied() => ctx.Backup.Get(Id) is not null;

    public void Apply() => throw new NotSupportedException("La fréquence se change depuis le Diagnostic.");

    public void Revert()
    {
        if (ctx.Backup.Get(Id) is null)
        {
            ctx.Log.Info($"[{Name}] rien à annuler (aucune sauvegarde)");
            return;
        }
        var originalText = ctx.Backup.GetOriginal(Id, HzKey)?.Text;
        var mode = ctx.Backup.GetOriginal(Id, ModeKey)?.Text;
        if (!int.TryParse(originalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var originalHz))
        {
            ctx.Log.Warn($"[{Name}] fréquence d'origine illisible : rien n'est changé");
            ctx.Backup.RemoveEntry(Id);
            return;
        }

        var display = api.Displays().FirstOrDefault(d => d.DeviceName == deviceName);
        if (display is null)
        {
            ctx.Log.Warn($"[{Name}] écran introuvable (débranché ?) : rien n'est changé");
        }
        else if (ModeText(display.Width, display.Height) != mode)
        {
            ctx.Log.Warn($"[{Name}] la résolution a changé depuis ({mode} → {ModeText(display.Width, display.Height)}) : fréquence laissée telle quelle");
        }
        else if (display.CurrentHz != originalHz)
        {
            api.SetRefreshRate(deviceName, display.Width, display.Height, originalHz); // en cas d'échec, la sauvegarde est gardée
            ctx.Log.Info($"[{Name}] {display.CurrentHz} Hz → {originalHz} Hz, comme avant");
        }
        ctx.Backup.RemoveEntry(Id);
    }
}
