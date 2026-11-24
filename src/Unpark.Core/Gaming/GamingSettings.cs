using Unpark.Core.Backup;
using Unpark.Core.Registry;
using Unpark.Core.Settings;

namespace Unpark.Core.Gaming;

/// <summary>Réglages du module Gaming. Les identifiants servent de clés dans backup.json : ne jamais les renommer.</summary>
public static class GamingSettings
{
    public const string GameModeId = "gaming.gamemode";
    public const string HagsId = "gaming.hags";
    public const string GameDvrId = "gaming.gamedvr";
    public const string MouseId = "gaming.mouseaccel";
    public const string VisualFxId = "gaming.visualfx";

    public static IReadOnlyList<ISetting> Create(SettingContext ctx, ISystemUiApi ui) =>
    [
        new RegistrySetting(ctx, GameModeId, "Mode Jeu Windows",
            "Windows donne la priorité au jeu en cours (processeur, carte graphique) et évite les installations de mises à jour pendant la partie.",
            requiresRestart: false,
            [new(new RegPath(RegHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"), RegValue.DWord(1))]),

        new RegistrySetting(ctx, HagsId, "Planification GPU à accélération matérielle (HAGS)",
            "La carte graphique gère elle-même sa mémoire : un peu moins de latence. Nécessite une carte et un pilote compatibles (WDDM 2.7+).",
            requiresRestart: true,
            [new(new RegPath(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"), RegValue.DWord(2))]),

        new RegistrySetting(ctx, GameDvrId, "Captures en arrière-plan (Game DVR)",
            "Désactive l'enregistrement vidéo en continu de la Xbox Game Bar, qui consomme des ressources pendant le jeu.",
            requiresRestart: false,
            [
                new(new RegPath(RegHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"), RegValue.DWord(0)),
                new(new RegPath(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"), RegValue.DWord(0)),
            ]),

        new MouseAccelerationSetting(ctx, ui),
        new VisualEffectsSetting(ctx, ui),
    ];
}

/// <summary>
/// Désactive « Améliorer la précision du pointeur » (accélération de la souris) : visée constante en jeu.
/// Passe par SystemParametersInfo (effet immédiat) ; la sauvegarde contient les 3 valeurs de HKCU\Control Panel\Mouse.
/// </summary>
public sealed class MouseAccelerationSetting(SettingContext ctx, ISystemUiApi ui) : ISetting
{
    private const string MouseKey = @"Control Panel\Mouse";
    private static readonly RegPath Threshold1 = new(RegHive.CurrentUser, MouseKey, "MouseThreshold1");
    private static readonly RegPath Threshold2 = new(RegHive.CurrentUser, MouseKey, "MouseThreshold2");
    private static readonly RegPath Speed = new(RegHive.CurrentUser, MouseKey, "MouseSpeed");

    public string Id => GamingSettings.MouseId;
    public string Name => "Accélération de la souris désactivée";
    public string Description => "Désactive « Améliorer la précision du pointeur » : le curseur bouge toujours de la même distance pour le même geste. Visée plus régulière.";
    public bool RequiresRestart => false;

    public bool IsApplied() => ui.GetMouse().Speed == 0;

    public void Apply()
    {
        foreach (var path in new[] { Threshold1, Threshold2, Speed })
            ctx.Backup.RecordOriginal(Id, Name, path.BackupKey, BackupValue.Of(ctx.Registry.Get(path)));
        var before = ui.GetMouse();
        ui.SetMouse(new MouseParams(0, 0, 0));
        ctx.Log.Info($"[{Name}] souris : {before} → seuils 0/0, vitesse 0");
    }

    public void Revert()
    {
        if (ctx.Backup.Get(Id) is null)
        {
            ctx.Log.Info($"[{Name}] rien à annuler (aucune sauvegarde)");
            return;
        }
        int? Read(RegPath p) => int.TryParse(ctx.Backup.GetOriginal(Id, p.BackupKey)?.Registry?.Text, out var v) ? v : null;
        if (Read(Threshold1) is { } t1 && Read(Threshold2) is { } t2 && Read(Speed) is { } s)
        {
            ui.SetMouse(new MouseParams(t1, t2, s));
            ctx.Log.Info($"[{Name}] souris remise à l'origine : seuils {t1}/{t2}, vitesse {s}");
            ctx.Backup.RemoveEntry(Id);
        }
        else
        {
            // Valeurs d'origine absentes ou inattendues : on remet le registre tel quel (effet à la prochaine ouverture de session).
            RegistrySetting.RevertFromBackup(ctx, Id, Name);
        }
    }
}

/// <summary>
/// Effets visuels réglés sur « performances », effet par effet via SystemParametersInfo.
/// Le lissage des polices (ClearType) et les miniatures sont gardés : l'écran reste lisible.
/// </summary>
public sealed class VisualEffectsSetting(SettingContext ctx, ISystemUiApi ui) : ISetting
{
    private const string FxPrefix = "text:fx:";
    private static readonly RegPath FxMode =
        new(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");

    public static IReadOnlyList<VisualEffect> Effects { get; } =
    [
        new("Animer les fenêtres lors de la réduction et de l'agrandissement", 0x0048, 0x0049, IsAnimationInfo: true),
        new("Animer les contrôles et éléments à l'intérieur des fenêtres", 0x1042, 0x1043),
        new("Fondu ou défilement des menus", 0x1002, 0x1003),
        new("Fondu ou défilement des info-bulles", 0x1016, 0x1017),
        new("Faire disparaître les éléments de menu après clic", 0x1014, 0x1015),
        new("Ouvrir les zones de liste déroulante", 0x1004, 0x1005),
        new("Défilement fluide des zones de liste", 0x1006, 0x1007),
        new("Afficher des ombres sous le pointeur de la souris", 0x101A, 0x101B),
        new("Afficher une ombre sous les fenêtres", 0x1024, 0x1025),
        new("Afficher le contenu des fenêtres pendant leur déplacement", 0x0026, 0x0025, UsesUiParam: true),
    ];

    public string Id => GamingSettings.VisualFxId;
    public string Name => "Effets visuels sur « performances »";
    public string Description => "Coupe les animations et ombres de Windows. Gain surtout sensible sur les PC modestes. Le lissage des polices est conservé.";
    public bool RequiresRestart => false;

    /// <summary>Aperçu : chaque effet et son état actuel (true = activé, sera désactivé).</summary>
    public IReadOnlyList<(VisualEffect Effect, bool On)> Preview() => Effects.Select(e => (e, ui.GetEffect(e))).ToList();

    public bool IsApplied() => Effects.All(e => !ui.GetEffect(e));

    public void Apply()
    {
        ctx.Backup.RecordOriginal(Id, Name, FxMode.BackupKey, BackupValue.Of(ctx.Registry.Get(FxMode)));
        foreach (var e in Effects)
            ctx.Backup.RecordOriginal(Id, Name, FxPrefix + e.SetAction.ToString("X4"), BackupValue.OfText(ui.GetEffect(e) ? "1" : "0"));

        // « Paramètres personnalisés » dans la fenêtre Options de performances de Windows.
        ctx.Registry.Set(FxMode, RegValue.DWord(3));
        foreach (var e in Effects)
        {
            if (!ui.GetEffect(e)) continue;
            ui.SetEffect(e, false);
            ctx.Log.Info($"[{Name}] « {e.Label} » : activé → désactivé");
        }
    }

    public void Revert()
    {
        if (ctx.Backup.Get(Id) is null)
        {
            ctx.Log.Info($"[{Name}] rien à annuler (aucune sauvegarde)");
            return;
        }
        foreach (var e in Effects)
        {
            var original = ctx.Backup.GetOriginal(Id, FxPrefix + e.SetAction.ToString("X4"))?.Text;
            if (original is null) continue;
            var on = original == "1";
            if (ui.GetEffect(e) != on) ui.SetEffect(e, on);
            ctx.Log.Info($"[{Name}] « {e.Label} » remis à l'origine : {(on ? "activé" : "désactivé")}");
        }
        RegistrySetting.RevertFromBackup(ctx, Id, Name); // VisualFXSetting + suppression de la sauvegarde
    }
}
