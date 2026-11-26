namespace Unpark.Core.Diagnostics;

/// <summary>Mini-guide affiché sur la ligne « Vitesse (XMP / EXPO) » : comment activer le profil mémoire dans le BIOS.</summary>
/// <param name="ProfileName">Nom du réglage selon la marque : XMP, EXPO, D.O.C.P., A-XMP.</param>
/// <param name="Available">false : PC de marque ou portable, le réglage n'existe souvent pas.</param>
public sealed record BiosGuide(string Title, string Brand, string ProfileName, IReadOnlyList<string> Steps, string Note, bool Available);

public enum BoardBrand { Unknown, Asus, Msi, Gigabyte, AsRock, Biostar, Oem }

/// <summary>
/// Textes du guide XMP / EXPO selon la marque de la carte mère, le processeur (Intel ou AMD) et le type de mémoire.
/// Fonctions pures, testées. Les noms de menus varient d'un BIOS à l'autre : on donne le chemin le plus courant.
/// </summary>
public static class BiosGuides
{
    public const string StabilityWarning =
        "En cas d'instabilité (plantages, écrans bleus, erreurs WHEA dans le diagnostic), retourne dans le BIOS et désactive ce profil. " +
        "Si le PC ne démarre plus, il revient en général tout seul aux réglages d'origine après quelques essais ; sinon, réinitialise le BIOS (« Clear CMOS », voir le manuel de la carte mère).";

    private const string WindowsShortcut =
        "Astuce : Windows peut redémarrer directement dans le BIOS : Paramètres → Récupération (Système → Récupération sur Windows 11) → " +
        "Démarrage avancé → Redémarrer maintenant → Dépannage → Options avancées → Paramètres du microprogramme UEFI.";

    private static readonly (string Match, string Name)[] OemBrands =
    [
        ("DELL", "Dell"), ("ALIENWARE", "Dell"), ("HEWLETT", "HP"), ("HP", "HP"), ("LENOVO", "Lenovo"), ("ACER", "Acer"),
        ("MEDION", "Medion"), ("FUJITSU", "Fujitsu"), ("TOSHIBA", "Toshiba"), ("DYNABOOK", "Dynabook"), ("MICROSOFT", "Microsoft"),
        ("PACKARD BELL", "Packard Bell"), ("SAMSUNG", "Samsung"), ("HUAWEI", "Huawei"), ("APPLE", "Apple"),
    ];

    /// <summary>Marque de la carte mère d'après Win32_BaseBoard (« ASUSTeK COMPUTER INC. » → ASUS…), et nom affichable.</summary>
    public static (BoardBrand Brand, string Name) DetectBrand(BoardData? board)
    {
        if (board is null) return (BoardBrand.Unknown, "");
        var b = board.BoardManufacturer.ToUpperInvariant();
        var system = board.SystemManufacturer.ToUpperInvariant();
        if (b.Contains("ASUS")) return (BoardBrand.Asus, "ASUS");
        if (b.Contains("MICRO-STAR") || b.Contains("MSI")) return (BoardBrand.Msi, "MSI");
        if (b.Contains("GIGABYTE") || b.Contains("AORUS")) return (BoardBrand.Gigabyte, "Gigabyte");
        if (b.Contains("ASROCK")) return (BoardBrand.AsRock, "ASRock");
        if (b.Contains("BIOSTAR")) return (BoardBrand.Biostar, "Biostar");
        foreach (var (match, name) in OemBrands)
        {
            if (IsWord(b, match) || IsWord(system, match)) return (BoardBrand.Oem, name);
        }
        return (BoardBrand.Unknown, "");
    }

    private static bool IsWord(string text, string word)
    {
        var i = text.IndexOf(word, StringComparison.Ordinal);
        if (i < 0) return false;
        var before = i == 0 || !char.IsLetter(text[i - 1]);
        var after = i + word.Length >= text.Length || !char.IsLetter(text[i + word.Length]);
        return before && after;
    }

    public static bool IsAmdCpu(BoardData? board) =>
        board?.CpuManufacturer.Contains("AMD", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Nom du profil mémoire dans le BIOS : XMP (Intel), EXPO (AMD + DDR5), D.O.C.P. (ASUS + AMD + DDR4), A-XMP (MSI + AMD + DDR4).</summary>
    public static string ProfileName(BoardBrand brand, bool amd, string ddr)
    {
        if (!amd) return "XMP";
        if (ddr == "DDR5") return "EXPO";
        return brand switch
        {
            BoardBrand.Asus => "D.O.C.P.",
            BoardBrand.Msi => "A-XMP",
            _ => "XMP",
        };
    }

    public static BiosGuide For(BoardData? board, string ddr, bool isLaptop)
    {
        var (brand, brandName) = DetectBrand(board);
        var amd = IsAmdCpu(board);
        var profile = ProfileName(brand, amd, ddr);
        var alt = profile == "EXPO" ? " (ou « XMP » si ta mémoire n'a pas de profil EXPO)" : "";

        if (isLaptop)
        {
            return new BiosGuide("Sur un PC portable", brandName, profile,
                ["Sur un PC portable, ce réglage n'existe presque jamais : la mémoire tourne à la vitesse choisie par le fabricant, et ce n'est pas un défaut de ton côté.",
                 "Quelques portables gaming le proposent : regarde dans le BIOS (souvent F2 au démarrage) un réglage nommé XMP ou « Memory Profile »."],
                StabilityWarning, Available: false);
        }

        if (brand == BoardBrand.Oem)
        {
            var key = brandName switch { "Dell" => "F2", "HP" => "F10 (ou Échap puis F10)", "Lenovo" => "F1 ou F2", _ => "F2 ou Suppr" };
            return new BiosGuide($"PC de marque ({brandName})", brandName, profile,
                [$"Les PC de marque ({brandName}, comme Dell, HP ou Lenovo) ne proposent souvent pas ce réglage : leur BIOS est simplifié et la mémoire reste à la vitesse de base.",
                 $"Certains modèles gaming (Alienware, OMEN, Legion…) l'ont : entre dans le BIOS en appuyant plusieurs fois sur {key} dès l'allumage, et cherche « XMP » ou « Memory Profile », souvent dans « Performance » ou « Overclocking ».",
                 "Si tu ne le trouves pas, il n'y a rien à faire : ce n'est pas un réglage que Windows peut changer."],
                StabilityWarning, Available: false);
        }

        var (enter, where) = brand switch
        {
            BoardBrand.Asus => ("Suppr (ou F2)",
                $"En mode EZ (l'écran d'accueil), le réglage est directement affiché : « {Label(brand, profile)} ». Sinon, appuie sur F7 (mode avancé) → onglet « Ai Tweaker » (« Extreme Tweaker » sur les cartes ROG) → « Ai Overclock Tuner » → choisis « {profile} I »{alt}."),
            BoardBrand.Msi => ("Suppr",
                $"En mode EZ, clique sur le bouton « {profile} » en haut à gauche (sous « Extreme Memory Profile »)."
                + $" Sinon, F7 (mode avancé) → « OC » → « {(profile == "EXPO" ? "AMD EXPO" : "Extreme Memory Profile (XMP)")} » → « Profile 1 »{alt}."),
            BoardBrand.Gigabyte => ("Suppr",
                $"En mode Easy, le réglage s'appelle « X.M.P. » ou « EXPO ». En mode avancé (touche F2 pour changer de mode) → onglet « Tweaker » → " +
                $"« Extreme Memory Profile (X.M.P.) » → choisis « Profile 1 »{(profile == "EXPO" ? " (les profils EXPO y sont listés aussi)" : "")}."),
            BoardBrand.AsRock => ("F2 (ou Suppr)",
                $"Onglet « OC Tweaker » → « DRAM Configuration » (ou « DRAM Profile Configuration ») → « {(profile == "EXPO" ? "Load EXPO Setting" : "Load XMP Setting")} » " +
                $"(parfois « DRAM Profile Setting ») → choisis le profil 1{alt}."),
            BoardBrand.Biostar => ("Suppr",
                $"Cherche dans l'onglet « O.N.E. » ou « Tweaker » le réglage « {profile} » (ou « Memory Profile ») → choisis le profil 1{alt}."),
            _ => ("Suppr ou F2 (la touche est souvent indiquée à l'allumage)",
                "Cherche dans les menus d'overclocking ou de mémoire (« Tweaker », « OC », « Advanced », « Memory ») un réglage nommé XMP, EXPO, DOCP, A-XMP ou « Memory Profile » → choisis le profil 1."),
        };

        var title = brandName.Length > 0 ? $"Activer {profile} sur ta carte mère {brandName}" : $"Activer {profile} dans le BIOS";
        return new BiosGuide(title, brandName, profile,
        [
            $"1. Redémarre le PC et appuie plusieurs fois sur {enter} dès l'allumage, avant le logo de Windows. {WindowsShortcut}",
            $"2. {where}",
            "3. Enregistre et quitte avec F10, puis confirme. Le premier démarrage peut être un peu plus long (la carte mère teste la mémoire) : c'est normal.",
            "4. Relance le diagnostic d'Unpark : la vitesse affichée doit être celle prévue pour tes barrettes.",
        ], StabilityWarning, Available: true);
    }

    // Nom affiché en mode EZ sur les cartes ASUS.
    private static string Label(BoardBrand brand, string profile) => brand == BoardBrand.Asus && profile == "XMP" ? "X.M.P." : profile;
}

/// <summary>Page officielle des pilotes graphiques selon le fabricant de la carte (jamais de téléchargement direct).</summary>
public static class DriverPages
{
    public const string Nvidia = "https://www.nvidia.com/fr-fr/drivers/";
    public const string Amd = "https://www.amd.com/fr/support/download/drivers.html";
    public const string Intel = "https://www.intel.com/content/www/fr/fr/support/detect.html";

    /// <summary>(fabricant, adresse) d'après le nom de la carte, ou null si inconnu.</summary>
    public static (string Vendor, string Url)? For(string gpuName)
    {
        var n = gpuName.ToUpperInvariant();
        if (n.Contains("NVIDIA") || n.Contains("GEFORCE") || n.Contains("QUADRO")) return ("NVIDIA", Nvidia);
        if (n.Contains("AMD") || n.Contains("RADEON") || n.Contains("ATI ")) return ("AMD", Amd);
        if (n.Contains("INTEL")) return ("Intel", Intel);
        return null;
    }
}
