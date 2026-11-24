namespace Unpark.Core.Power;

/// <summary>
/// Un paramètre d'alimentation : sous-groupe + paramètre (alias powercfg quand il existe, sinon GUID), valeur Unpark.
/// Les GUID sont ceux de Windows (identiques sur Windows 10 et 11) ; ils servent aussi à lire l'état via powrprof.dll.
/// </summary>
public sealed record PowerSettingDef(
    string Label,
    Guid SubGroup,
    string? SubAlias,
    Guid Setting,
    string? Alias,
    uint Value,
    string ValueText,
    bool HiddenByDefault = false,
    bool Optional = false,
    PowerValueKind Kind = PowerValueKind.Percent)
{
    /// <summary>Valeur lisible (« 100 % », « Jamais », « Agressif »…).</summary>
    public string Format(uint? value) => value is not { } v ? "—" : Kind switch
    {
        PowerValueKind.Percent => $"{v} %",
        PowerValueKind.Seconds => v == 0 ? "Jamais" : v % 60 == 0 ? $"{v / 60} min" : $"{v} s",
        PowerValueKind.Boost => v switch
        {
            0 => "Désactivé", 1 => "Activé", 2 => "Agressif", 3 => "Efficace activé", 4 => "Efficace agressif",
            5 => "Agressif garanti", 6 => "Efficace agressif garanti", _ => v.ToString(),
        },
        PowerValueKind.OnOff => v == 0 ? "Désactivée" : "Activée",
        PowerValueKind.Aspm => v switch { 0 => "Désactivée", 1 => "Économies modérées", 2 => "Économies maximales", _ => v.ToString() },
        _ => v.ToString(),
    };

    /// <summary>Nom passé à powercfg : l'alias s'il existe, sinon le GUID.</summary>
    public string SubArg => SubAlias ?? SubGroup.ToString();
    public string SettingArg => Alias ?? Setting.ToString();
}

public enum PowerValueKind { Percent, Seconds, Boost, OnOff, Aspm }

/// <summary>Plans et paramètres utilisés par le plan « Unpark Performance ».</summary>
public static class PowerSettings
{
    // Plans fournis par Windows.
    public static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");      // Performances optimales
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"); // Performances élevées
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");        // Utilisation normale / Équilibré

    /// <summary>GUID fixe du plan Unpark : on le retrouve sans lire la sortie de powercfg (qui dépend de la langue).</summary>
    public static readonly Guid Unpark = new("3f6a2b8e-9d41-4c7a-b5e2-6d1f0a9c8e47");

    public const string UnparkName = "Unpark Performance";
    public const string UnparkDescription = "Plan d'alimentation gaming créé par Unpark (cœurs toujours actifs, processeur à 100 %).";

    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid SubDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    private static readonly Guid SubUsb = new("2a737441-1930-4402-8d77-b2bebba308a3");
    private static readonly Guid SubPciExpress = new("501a4d13-42af-4429-9fd1-a8139f2c5b1e");
    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");

    public static IReadOnlyList<PowerSettingDef> All { get; } =
    [
        new("Cœurs actifs minimum (core parking désactivé)", SubProcessor, "SUB_PROCESSOR",
            new("0cc5b647-c1df-4637-891a-dec35c318583"), "CPMINCORES", 100, "100 %", HiddenByDefault: true),
        new("Cœurs actifs minimum, cœurs efficaces (Intel hybride)", SubProcessor, "SUB_PROCESSOR",
            new("0cc5b647-c1df-4637-891a-dec35c318584"), null, 100, "100 %", HiddenByDefault: true, Optional: true),
        new("État minimal du processeur", SubProcessor, "SUB_PROCESSOR",
            new("893dee8e-2bef-41e0-89c6-b55d0929964c"), "PROCTHROTTLEMIN", 100, "100 %"),
        new("État minimal du processeur, cœurs efficaces (Intel hybride)", SubProcessor, "SUB_PROCESSOR",
            new("893dee8e-2bef-41e0-89c6-b55d0929964d"), null, 100, "100 %", HiddenByDefault: true, Optional: true),
        new("État maximal du processeur", SubProcessor, "SUB_PROCESSOR",
            new("bc5038f7-23e0-4960-96da-33abaf5935ec"), "PROCTHROTTLEMAX", 100, "100 %"),
        new("État maximal du processeur, cœurs efficaces (Intel hybride)", SubProcessor, "SUB_PROCESSOR",
            new("bc5038f7-23e0-4960-96da-33abaf5935ed"), null, 100, "100 %", HiddenByDefault: true, Optional: true),
        new("Mode boost du processeur", SubProcessor, "SUB_PROCESSOR",
            new("be337238-0d82-4146-a960-4f3749d470c7"), "PERFBOOSTMODE", 2, "Agressif", HiddenByDefault: true, Kind: PowerValueKind.Boost),
        new("Arrêt du disque dur", SubDisk, "SUB_DISK",
            new("6738e2c4-e8a5-4a42-b16a-e040e769756e"), "DISKIDLE", 0, "Jamais", Kind: PowerValueKind.Seconds),
        new("Suspension sélective USB", SubUsb, null,
            new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226"), null, 0, "Désactivée", Kind: PowerValueKind.OnOff),
        new("Gestion d'alimentation PCI Express (ASPM)", SubPciExpress, "SUB_PCIEXPRESS",
            new("ee12f906-d277-404b-b6da-e5fa1a576df5"), "ASPM", 0, "Désactivée", Kind: PowerValueKind.Aspm),
        new("Mise en veille", SubSleep, "SUB_SLEEP",
            new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"), "STANDBYIDLE", 0, "Jamais", Kind: PowerValueKind.Seconds),
    ];
}
