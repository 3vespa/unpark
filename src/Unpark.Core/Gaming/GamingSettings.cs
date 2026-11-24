using Unpark.Core.Backup;
using Unpark.Core.Registry;
using Unpark.Core.Settings;

namespace Unpark.Core.Gaming;

/// <summary>
/// Réglages du module Gaming, retenus après l'audit : seulement ceux qui ont un effet reconnu.
/// Les identifiants servent de clés dans backup.json : ne jamais les renommer.
/// </summary>
public static class GamingSettings
{
    public const string GameModeId = "gaming.gamemode";
    public const string HagsId = "gaming.hags";
    public const string GameDvrId = "gaming.gamedvr";
    public const string WindowedGamesId = "gaming.windowedgames";
    public const string MouseId = "gaming.mouseaccel";
    public const string MemoryIntegrityId = "gaming.memoryintegrity";
    /// <summary>Retiré après l'audit (aucun effet en jeu) : gardé seulement pour annuler les sauvegardes existantes.</summary>
    public const string VisualFxId = "gaming.visualfx";

    /// <summary>Réglages proposés dans l'appli.</summary>
    /// <param name="windowsBuild">Numéro de build de Windows (certains réglages n'existent que sur Windows 11).</param>
    public static IReadOnlyList<ISetting> Create(SettingContext ctx, ISystemUiApi ui, int windowsBuild) =>
    [
        new RegistrySetting(ctx, GameModeId, "Mode Jeu",
            "Windows donne la priorité au jeu et n'installe pas de mises à jour pendant la partie. Effet faible, mais réel quand Windows travaille en fond.",
            requiresRestart: false,
            [new(new RegPath(RegHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"), RegValue.DWord(1))]),

        new RegistrySetting(ctx, GameDvrId, "Enregistrement en arrière-plan (Game Bar) désactivé",
            "Coupe l'enregistrement vidéo en continu de la Xbox Game Bar : quelques % de FPS récupérés s'il était actif, rien sinon. " +
            "Tu perds « Enregistrer ce qui vient de se passer ».",
            requiresRestart: false,
            [
                new(new RegPath(RegHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"), RegValue.DWord(0)),
                new(new RegPath(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"), RegValue.DWord(0)),
            ]),

        new WindowedGamesSetting(ctx, windowsBuild),

        new RegistrySetting(ctx, HagsId, "Planification GPU à accélération matérielle (HAGS)",
            "Nécessaire pour DLSS Frame Generation (RTX 40/50). Sur les autres cartes, effet quasi nul en FPS.",
            requiresRestart: true,
            [new(new RegPath(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"), RegValue.DWord(2))]),

        new MouseAccelerationSetting(ctx, ui),
        new MemoryIntegritySetting(ctx),
    ];

    /// <summary>Réglages retirés de l'interface, mais encore annulables (« Tout annuler ») pour ceux qui les avaient appliqués.</summary>
    public static IReadOnlyList<ISetting> Legacy(SettingContext ctx, ISystemUiApi ui) => [new VisualEffectsSetting(ctx, ui)];
}

/// <summary>Réglage qui peut ne pas exister sur ce PC (version de Windows, verrou de sécurité…).</summary>
public interface IConditionalSetting
{
    /// <summary>null si le réglage est disponible, sinon la raison (affichée à l'utilisateur).</summary>
    string? UnavailableReason();
}

/// <summary>Réglage qui demande une confirmation avec un avertissement avant d'être appliqué.</summary>
public interface IWarnedSetting
{
    string Warning { get; }
}

/// <summary>
/// « Optimisations pour les jeux en fenêtre » (Windows 11 22H2+, Paramètres → Affichage → Graphiques) :
/// les jeux DirectX 10/11 en fenêtre sans bordure passent au « flip model » (latence plus basse, Auto HDR et VRR en fenêtré).
/// La valeur DirectXUserGlobalSettings contient d'autres réglages (Auto HDR, VRR…) : on ne change que SwapEffectUpgradeEnable.
/// </summary>
public sealed class WindowedGamesSetting(SettingContext ctx, int windowsBuild) : ISetting, IConditionalSetting
{
    public static readonly RegPath ValuePath = new(RegHive.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings");
    private const string Key = "SwapEffectUpgradeEnable";
    /// <summary>Windows 11 22H2.</summary>
    public const int MinBuild = 22621;

    public string Id => GamingSettings.WindowedGamesId;
    public string Name => "Optimisations pour les jeux en fenêtre";
    public string Description => "Jeux DirectX 10/11 en fenêtre sans bordure : latence plus basse, Auto HDR et VRR en fenêtré. Aucun effet en plein écran exclusif.";
    public bool RequiresRestart => false;

    public string? UnavailableReason() => windowsBuild >= MinBuild ? null : "Disponible seulement sur Windows 11 (22H2 ou plus récent).";

    public bool IsApplied() => Parse(ctx.Registry.Get(ValuePath)?.Text).TryGetValue(Key, out var v) && v == "1";

    public void Apply()
    {
        if (UnavailableReason() is { } reason) throw new InvalidOperationException(reason);
        var current = ctx.Registry.Get(ValuePath);
        ctx.Backup.RecordOriginal(Id, Name, ValuePath.BackupKey, BackupValue.Of(current));
        var pairs = Parse(current?.Text);
        pairs[Key] = "1";
        var value = RegValue.String(Join(pairs));
        ctx.Registry.Set(ValuePath, value);
        ctx.Log.Info($"[{Name}] {ValuePath} : {current?.ToString() ?? "(absente)"} → {value}");
    }

    public void Revert() => RegistrySetting.RevertFromBackup(ctx, Id, Name);

    /// <summary>« A=1;B=0; » → dictionnaire, ordre conservé.</summary>
    internal static Dictionary<string, string> Parse(string? text)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) dict[part[..eq]] = part[(eq + 1)..];
        }
        return dict;
    }

    internal static string Join(Dictionary<string, string> pairs) => string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));
}

/// <summary>
/// Désactive l'Intégrité de la mémoire (HVCI, isolation du noyau). Microsoft documente son coût en jeu,
/// mais c'est une vraie protection : décochée partout, avertissement obligatoire, redémarrage requis.
/// Refusé si le réglage est verrouillé (UEFI) ou imposé par une stratégie de groupe.
/// </summary>
public sealed class MemoryIntegritySetting(SettingContext ctx) : RegistrySetting(
    ctx,
    GamingSettings.MemoryIntegrityId,
    "Intégrité de la mémoire désactivée",
    "Peut faire gagner de 0 à environ 10 % selon le processeur et le jeu (moins sur les processeurs récents). " +
    "Réduit la protection de Windows contre les pilotes malveillants.",
    requiresRestart: true,
    [new(EnabledPath, RegValue.DWord(0))]), IConditionalSetting, IWarnedSetting
{
    private const string Scenario = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    public static readonly RegPath EnabledPath = new(RegHive.LocalMachine, Scenario, "Enabled");
    public static readonly RegPath LockedPath = new(RegHive.LocalMachine, Scenario, "Locked");
    public static readonly RegPath PolicyPath = new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "HypervisorEnforcedCodeIntegrity");

    private readonly SettingContext _ctx = ctx;

    public string Warning =>
        "Désactiver l'Intégrité de la mémoire réduit la sécurité de Windows : elle empêche des pilotes malveillants de prendre le contrôle du système.\n\n" +
        "Gain possible : de 0 à environ 10 % de FPS selon le processeur et le jeu (souvent moins sur les processeurs récents).\n\n" +
        "Certains anti-triche peuvent exiger qu'elle reste activée : si un jeu refuse de se lancer, réactive-la (bouton Annuler).\n\n" +
        "Un redémarrage est nécessaire. Continuer ?";

    public string? UnavailableReason()
    {
        if (_ctx.Registry.Get(LockedPath) is { Number: 1 })
            return "Verrouillée par le fabricant ou l'administrateur (UEFI) : Unpark n'y touche pas.";
        if (_ctx.Registry.Get(PolicyPath) is { Number: > 0 })
            return "Imposée par une stratégie de groupe (PC d'entreprise ou d'école) : Unpark n'y touche pas.";
        return null;
    }

    public override bool IsApplied() => _ctx.Registry.Get(EnabledPath) is not { Number: not 0 };

    public override void Apply()
    {
        if (UnavailableReason() is { } reason) throw new InvalidOperationException(reason);
        base.Apply();
    }
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

/// <summary>Conseil HAGS selon la carte graphique (jamais activé automatiquement).</summary>
public sealed record HagsAdvice(bool Recommended, string Note)
{
    public const string NeutralNote =
        "Effet quasi nul en FPS, utile seulement pour la génération d'images ; peut réserver de la mémoire vidéo sur les cartes de 6-8 Go.";

    /// <summary>RTX 40 ou 50 : recommandé (nécessaire pour DLSS Frame Generation). Autres cartes : laisser tel quel.</summary>
    public static HagsAdvice For(IEnumerable<string> gpuNames)
    {
        var rtx = gpuNames.FirstOrDefault(Unpark.Core.Diagnostics.DiagnosticParsers.IsRtx40Or50);
        return rtx is not null
            ? new HagsAdvice(true, $"Recommandé sur ta {rtx} : nécessaire pour DLSS Frame Generation.")
            : new HagsAdvice(false, NeutralNote);
    }
}
