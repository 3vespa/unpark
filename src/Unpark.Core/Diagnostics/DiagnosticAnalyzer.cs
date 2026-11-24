using System.Text;

namespace Unpark.Core.Diagnostics;

/// <summary>
/// Transforme les données du diagnostic en résultats vert / orange / rouge, avec une explication simple.
/// Fonctions pures : tout est testé sans Windows.
/// </summary>
public static class DiagnosticAnalyzer
{
    public const string CpuTemperatureText =
        "Non disponible : lire la température du processeur demande un pilote noyau (type WinRing0), souvent bloqué par les antivirus. " +
        "Unpark n'en installe aucun. Pour la voir, utilise HWiNFO.";

    public static IReadOnlyList<DiagSection> Analyze(DiagnosticData d)
    {
        var sections = new List<DiagSection>
        {
            new("Mémoire (RAM)", Ram(d.Ram)),
            new("Écran", Displays(d.Displays)),
            new("Carte graphique", Gpus(d.Gpus, d.Windows, d.Now)),
            new("Stabilité (30 derniers jours)", Stability(d.Stability)),
            new("Stockage", Storage(d.Disks, d.Volumes, d.GameFolders)),
            new("Windows", Windows(d.Windows, d.Gpus)),
            new("Réseau", Network(d.Network)),
            new("Processeur", [new DiagCheck("Température du processeur", DiagStatus.Info, "Non disponible", CpuTemperatureText)]),
        };
        return sections.Where(s => s.Checks.Count > 0).ToList();
    }

    // ---------- RAM ----------

    public static IReadOnlyList<DiagCheck> Ram(RamData? ram)
    {
        if (ram is null || ram.Modules.Count == 0 && ram.TotalBytes == 0)
            return [new("Mémoire", DiagStatus.Info, "Illisible", "Windows n'a pas donné d'informations sur la mémoire.")];

        var checks = new List<DiagCheck>();
        var total = ram.TotalBytes > 0 ? ram.TotalBytes : ram.Modules.Sum(m => m.CapacityBytes);
        var gb = total / (1024.0 * 1024 * 1024);
        checks.Add(gb < 7.5
            ? new("Quantité", DiagStatus.Red, Format.Bytes(total), "Moins de 8 Go : la plupart des jeux récents vont saccader. 16 Go est le minimum conseillé.")
            : gb < 15
                ? new("Quantité", DiagStatus.Orange, Format.Bytes(total), "8 Go suffisent pour les jeux légers ; 16 Go sont conseillés pour les jeux récents.")
                : new("Quantité", DiagStatus.Green, Format.Bytes(total), "Quantité suffisante pour le jeu."));

        if (ram.TotalBytes > 0 && ram.AvailableBytes > 0)
        {
            var used = 1 - (double)ram.AvailableBytes / ram.TotalBytes;
            var usedText = $"{used * 100:0} % utilisée";
            checks.Add(used > 0.85
                ? new("Utilisation", DiagStatus.Orange, usedText, "La mémoire est presque pleine avant même de jouer : ferme les programmes inutiles ou réduis ceux du démarrage.", DiagFix.Startup)
                : new("Utilisation", DiagStatus.Green, usedText, "Assez de mémoire libre."));
        }

        var modules = ram.Modules.Where(m => m.CapacityBytes > 0).ToList();
        if (modules.Count > 0) checks.Add(RamSpeed(modules));
        if (modules.Count > 0) checks.Add(DualChannel(modules));
        return checks;
    }

    private static DiagCheck RamSpeed(IReadOnlyList<RamModule> modules)
    {
        var configured = modules.Where(m => m.ConfiguredMts > 0).Select(m => m.ConfiguredMts).DefaultIfEmpty(0).Min();
        var reported = modules.Select(m => m.RatedMts).DefaultIfEmpty(0).Max();
        var fromPart = modules.Select(m => DiagnosticParsers.RatedSpeedFromPartNumber(m.PartNumber) ?? 0).DefaultIfEmpty(0).Max();
        var rated = Math.Max(reported, fromPart);
        var ddr = modules.Select(m => m.SmbiosType).FirstOrDefault() switch { 26 => "DDR4", 34 => "DDR5", _ => "" };
        var value = configured > 0 ? $"{ddr} {configured} MT/s".Trim() + (rated > configured ? $" (prévue pour {rated})" : "") : "Vitesse illisible";

        if (configured == 0)
            return new("Vitesse (XMP / EXPO)", DiagStatus.Info, value, "Windows n'indique pas la vitesse actuelle de la mémoire.");
        if (rated > configured + 100)
            return new("Vitesse (XMP / EXPO)", DiagStatus.Orange, value,
                $"Tes barrettes sont prévues pour {rated} MT/s mais tournent à {configured}. Active le profil XMP (Intel) ou EXPO (AMD) dans le BIOS : " +
                "souvent plusieurs % de FPS et de meilleurs 1 % low dans les jeux limités par le processeur.");
        var jedecMax = ddr == "DDR5" ? 4800 : ddr == "DDR4" ? 2666 : 0;
        if (jedecMax > 0 && configured <= jedecMax)
            return new("Vitesse (XMP / EXPO)", DiagStatus.Orange, value,
                $"{configured} MT/s est la vitesse de base du {ddr} : le profil XMP / EXPO est probablement désactivé. Si tes barrettes sont prévues plus " +
                "rapides (regarde leur étiquette ou leur fiche), active XMP / EXPO dans le BIOS.");
        return new("Vitesse (XMP / EXPO)", DiagStatus.Green, value, "La mémoire tourne à sa vitesse prévue (XMP / EXPO actif ou inutile).");
    }

    private static DiagCheck DualChannel(IReadOnlyList<RamModule> modules)
    {
        if (modules.Count == 1)
            return new("Double canal (dual channel)", DiagStatus.Orange, "1 barrette",
                "Une seule barrette = simple canal : la mémoire a deux fois moins de débit. Dans les jeux limités par le processeur, " +
                "ça peut coûter 10 à 20 % de FPS. Ajoute une deuxième barrette identique (sur portable, si un emplacement est libre).");
        var channels = modules.Select(DiagnosticParsers.MemoryChannel).ToList();
        if (channels.All(c => c is not null) && channels.Distinct().Count() == 1)
            return new("Double canal (dual channel)", DiagStatus.Orange, $"{modules.Count} barrettes, même canal",
                "Les barrettes semblent installées sur le même canal. Vérifie les emplacements conseillés dans le manuel de la carte mère (souvent A2 et B2).");
        return new("Double canal (dual channel)", DiagStatus.Green, $"{modules.Count} barrettes", "Les barrettes fonctionnent en double canal.");
    }

    // ---------- Écran ----------

    public static IReadOnlyList<DiagCheck> Displays(IReadOnlyList<DisplayData> displays)
    {
        if (displays.Count == 0) return [new("Fréquence de l'écran", DiagStatus.Info, "Illisible", "Windows n'a pas donné la fréquence de l'écran.")];
        return displays.Select(s =>
        {
            var title = displays.Count > 1 ? $"Fréquence ({s.Name})" : "Fréquence de l'écran";
            var value = $"{s.CurrentHz} Hz (max {s.MaxHz} Hz) en {s.Width}×{s.Height}";
            return s.MaxHz > s.CurrentHz + 1
                ? new DiagCheck(title, DiagStatus.Red, value,
                    $"L'écran est réglé à {s.CurrentHz} Hz alors qu'il supporte {s.MaxHz} Hz : l'erreur la plus courante ! " +
                    "Paramètres → Affichage → Affichage avancé → Choisir une fréquence d'actualisation.", DiagFix.DisplaySettings)
                : new DiagCheck(title, DiagStatus.Green, value, "L'écran est réglé à sa fréquence maximale.");
        }).ToList();
    }

    // ---------- Carte graphique ----------

    public static IReadOnlyList<DiagCheck> Gpus(IReadOnlyList<GpuData> gpus, WindowsData? windows, DateTime now)
    {
        var checks = new List<DiagCheck>();
        foreach (var g in gpus.Where(g => !g.Name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase)))
        {
            var prefix = gpus.Count > 1 ? $"{g.Name} : " : "";
            if (g.DriverDate is { } date)
            {
                var months = (now - date).TotalDays / 30.4;
                var value = $"{g.Name} — pilote {g.DriverVersion} du {date:dd/MM/yyyy}";
                checks.Add(months > 12
                    ? new(prefix + "Pilote graphique", DiagStatus.Red, value, "Pilote de plus d'un an : installe le dernier depuis le site du fabricant (NVIDIA App, AMD Adrenalin, Intel). Gains et corrections pour les jeux récents.")
                    : months > 6
                        ? new(prefix + "Pilote graphique", DiagStatus.Orange, value, "Pilote de plus de 6 mois : une mise à jour peut améliorer les jeux récents.")
                        : new(prefix + "Pilote graphique", DiagStatus.Green, value, "Pilote récent."));
            }
            else
            {
                checks.Add(new(prefix + "Pilote graphique", DiagStatus.Info, $"{g.Name} — pilote {g.DriverVersion}", "Date du pilote illisible."));
            }

            if (g.Nvidia is { } nv) checks.AddRange(Nvidia(prefix, g.Name, nv));
            else if (g.NvidiaSmiMissing)
                checks.Add(new(prefix + "Détails NVIDIA", DiagStatus.Info, "Outil nvidia-smi absent", "Température, PCIe et Resizable BAR non lus : l'outil nvidia-smi du pilote NVIDIA est introuvable."));
        }

        var hybrid = gpus.Any(g => DiagnosticParsers.IsIntegratedGpu(g.Name)) && gpus.Any(g => !DiagnosticParsers.IsIntegratedGpu(g.Name) && !g.Name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase));
        if (windows?.IsLaptop == true && hybrid)
        {
            checks.Add(new("Carte utilisée par les jeux (portable)", DiagStatus.Orange,
                windows.AppsOnHighPerformanceGpu > 0 ? $"{windows.AppsOnHighPerformanceGpu} appli(s) réglée(s) sur « Hautes performances »" : "Aucune appli réglée",
                "Ton portable a deux cartes graphiques. Vérifie que chaque jeu utilise la carte dédiée : Paramètres → Affichage → Graphiques → " +
                "choisis le jeu → « Hautes performances ». Sinon le jeu peut tourner sur la puce intégrée, beaucoup moins puissante.", DiagFix.GraphicsSettings));
        }
        return checks;
    }

    private static IEnumerable<DiagCheck> Nvidia(string prefix, string name, NvidiaData nv)
    {
        if (nv.TemperatureC is { } t)
            yield return t >= 85
                ? new(prefix + "Température GPU", DiagStatus.Red, $"{t} °C", "Très chaud pendant le diagnostic : vérifie la ventilation et la poussière.")
                : t >= 75
                    ? new(prefix + "Température GPU", DiagStatus.Orange, $"{t} °C", "Chaud pour une carte peu sollicitée : vérifie la ventilation.")
                    : new(prefix + "Température GPU", DiagStatus.Green, $"{t} °C", "Température normale.");

        if (nv.PcieWidthCurrent is { } w && nv.PcieWidthMax is { } wMax && w < wMax)
            yield return new(prefix + "Liaison PCIe", DiagStatus.Orange, $"x{w} au lieu de x{wMax}",
                "La carte graphique a moins de lignes PCIe que prévu : vérifie qu'elle est dans le port PCIe principal (le plus haut) et bien enfoncée.");
        else if (nv.PcieGenCurrent is { } gen && nv.PcieGenMax is { } genMax)
            yield return new(prefix + "Liaison PCIe", gen < genMax ? DiagStatus.Info : DiagStatus.Green,
                $"PCIe {gen}.0 (max {genMax}.0){(nv.PcieWidthCurrent is { } cw ? $" x{cw}" : "")}",
                gen < genMax ? "Normal au repos : la carte baisse la vitesse PCIe pour économiser l'énergie, et remonte en jeu." : "Liaison PCIe à pleine vitesse.");

        if (nv.Bar1TotalMiB is { } bar && DiagnosticParsers.SupportsResizableBar(name))
            yield return bar > 256
                ? new(prefix + "Resizable BAR", DiagStatus.Green, "Activé", "Le processeur accède à toute la mémoire vidéo : petit gain dans certains jeux.")
                : new(prefix + "Resizable BAR", DiagStatus.Orange, "Désactivé",
                    "Peut apporter quelques % dans certains jeux. À activer dans le BIOS : « Above 4G Decoding » puis « Resizable BAR » (et CSM désactivé).");
    }

    // ---------- Stabilité ----------

    public static IReadOnlyList<DiagCheck> Stability(StabilityData? s)
    {
        if (s is null) return [new("Journal des erreurs", DiagStatus.Info, "Illisible", "L'Observateur d'événements n'a pas pu être lu.")];
        static string Last(DateTime? d) => d is { } x ? $", dernier le {x:dd/MM/yyyy}" : "";
        return
        [
            s.BugChecks > 0
                ? new("Écrans bleus", DiagStatus.Red, $"{s.BugChecks}{Last(s.LastBugCheck)}", "Windows a planté (écran bleu). Causes fréquentes : overclock instable, pilote défectueux, RAM. Envoie le rapport sur Discord.")
                : new("Écrans bleus", DiagStatus.Green, "Aucun", "Aucun écran bleu."),
            s.Whea > 0
                ? new("Erreurs matérielles (WHEA)", s.Whea >= 10 ? DiagStatus.Red : DiagStatus.Orange, $"{s.Whea}{Last(s.LastWhea)}",
                    "Le processeur ou la mémoire a signalé des erreurs matérielles. Souvent un overclock RAM (XMP trop poussé) ou CPU instable, ou un undervolt trop fort : " +
                    "reviens aux réglages par défaut du BIOS pour tester.")
                : new("Erreurs matérielles (WHEA)", DiagStatus.Green, "Aucune", "Aucune erreur matérielle signalée."),
            s.KernelPower41 > 0
                ? new("Redémarrages brutaux (Kernel-Power 41)", DiagStatus.Orange, $"{s.KernelPower41}{Last(s.LastKernelPower41)}",
                    "Le PC s'est éteint sans arrêt propre. Normal après une coupure de courant ou un arrêt forcé (bouton maintenu) ; sinon, suspect : alimentation, surchauffe ou overclock.")
                : new("Redémarrages brutaux (Kernel-Power 41)", DiagStatus.Green, "Aucun", "Aucun arrêt brutal."),
        ];
    }

    // ---------- Stockage ----------

    public static IReadOnlyList<DiagCheck> Storage(IReadOnlyList<DiskData> disks, IReadOnlyList<VolumeData> volumes, IReadOnlyList<GameFolder> games)
    {
        var checks = new List<DiagCheck>();
        foreach (var d in disks)
        {
            var value = $"{d.Media}{(d.DriveLetters.Count > 0 ? $" ({string.Join(", ", d.DriveLetters)})" : "")}";
            checks.Add(d.Health switch
            {
                "Unhealthy" => new($"Disque {d.Model}", DiagStatus.Red, value + " — en mauvais état", "Windows signale ce disque comme défaillant : sauvegarde tes fichiers importants dès maintenant."),
                "Warning" => new($"Disque {d.Model}", DiagStatus.Orange, value + " — avertissement", "Windows signale un problème sur ce disque : surveille-le et sauvegarde tes fichiers."),
                "Healthy" => new($"Disque {d.Model}", DiagStatus.Green, value + " — en bon état", "Aucun problème signalé par le disque."),
                _ => new($"Disque {d.Model}", DiagStatus.Info, value, "État de santé non disponible."),
            });
        }
        if (disks.Count > 0 && !disks.Any(d => d.Media.StartsWith("SSD", StringComparison.OrdinalIgnoreCase)) && disks.Any(d => d.Media.StartsWith("HDD", StringComparison.OrdinalIgnoreCase)))
            checks.Add(new("SSD", DiagStatus.Red, "Aucun SSD", "Pas de SSD : Windows et les jeux chargent lentement et peuvent saccader. Un SSD est l'amélioration la plus rentable."));

        foreach (var v in volumes)
        {
            var free = v.TotalBytes == 0 ? 1 : (double)v.FreeBytes / v.TotalBytes;
            var value = $"{Format.Bytes(v.FreeBytes)} libres sur {Format.Bytes(v.TotalBytes)}";
            checks.Add(free < 0.10
                ? new($"Espace libre {v.Letter}", DiagStatus.Red, value, "Moins de 10 % libres : Windows et les jeux ralentissent, les mises à jour peuvent échouer. Fais de la place.", DiagFix.Cleanup)
                : free < 0.20
                    ? new($"Espace libre {v.Letter}", DiagStatus.Orange, value, "Moins de 20 % libres : pense à faire de la place.", DiagFix.Cleanup)
                    : new($"Espace libre {v.Letter}", DiagStatus.Green, value, "Assez d'espace libre."));
        }

        var onHdd = games
            .Select(g => (Game: g, Disk: disks.FirstOrDefault(d => d.DriveLetters.Any(l => g.Path.StartsWith(l, StringComparison.OrdinalIgnoreCase)))))
            .Where(x => x.Disk is not null && x.Disk.Media.StartsWith("HDD", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Game.Launcher} ({x.Game.Path})")
            .Distinct()
            .ToList();
        if (onHdd.Count > 0)
            checks.Add(new("Jeux sur disque dur", DiagStatus.Orange, string.Join(", ", onHdd),
                "Des jeux sont installés sur un disque dur : chargements longs et saccades quand le jeu charge des textures. Déplace-les sur le SSD (Steam : Paramètres → Stockage)."));
        else if (games.Count > 0)
            checks.Add(new("Emplacement des jeux", DiagStatus.Green, $"{games.Count} bibliothèque(s) de jeux", "Aucune bibliothèque de jeux sur un disque dur."));
        return checks;
    }

    // ---------- Windows ----------

    public static IReadOnlyList<DiagCheck> Windows(WindowsData? w, IReadOnlyList<GpuData> gpus)
    {
        if (w is null) return [];
        var checks = new List<DiagCheck>();
        var plan = w.ActivePlan;
        var fast = plan.Contains("Unpark", StringComparison.OrdinalIgnoreCase) || plan.Contains("Bitsum", StringComparison.OrdinalIgnoreCase)
                   || plan.Contains("élevées", StringComparison.OrdinalIgnoreCase) || plan.Contains("optimales", StringComparison.OrdinalIgnoreCase)
                   || plan.Contains("High performance", StringComparison.OrdinalIgnoreCase) || plan.Contains("Ultimate", StringComparison.OrdinalIgnoreCase);
        checks.Add(fast
            ? new("Plan d'alimentation", DiagStatus.Green, plan, "Plan orienté performances.")
            : new("Plan d'alimentation", DiagStatus.Orange, plan,
                "Plan économe : le processeur peut mettre plus de temps à monter en fréquence. Gain faible à modéré (surtout sur les 1 % low) avec Unpark Performance.", DiagFix.PowerPlan));

        checks.Add(w.GameMode == false
            ? new("Mode Jeu", DiagStatus.Orange, "Désactivé", "Le Mode Jeu évite que Windows Update et les notifications dérangent pendant la partie.", DiagFix.Gaming)
            : new("Mode Jeu", DiagStatus.Green, "Activé", "Windows donne la priorité au jeu en cours."));

        var rtx = gpus.FirstOrDefault(g => DiagnosticParsers.IsRtx40Or50(g.Name));
        var hagsValue = w.Hags switch { true => "Activée", false => "Désactivée", _ => "Non configurée" };
        checks.Add(rtx is not null && w.Hags != true
            ? new("Planification GPU (HAGS)", DiagStatus.Orange, hagsValue, $"Recommandée sur ta {rtx.Name} : nécessaire pour DLSS Frame Generation.", DiagFix.Gaming)
            : new("Planification GPU (HAGS)", DiagStatus.Info, hagsValue,
                rtx is not null ? "Active : DLSS Frame Generation disponible." : "Effet quasi nul en FPS, utile seulement pour la génération d'images ; peut réserver de la mémoire vidéo sur les cartes de 6-8 Go."));

        if (w.MemoryIntegrity is { } hvci)
            checks.Add(new("Intégrité de la mémoire", DiagStatus.Info, hvci ? "Activée" : "Désactivée",
                hvci ? "Protection de sécurité active. Elle peut coûter quelques % de performances dans certains jeux ; à toi de choisir entre sécurité et FPS."
                     : "Désactivée : un peu plus de performances, mais moins de protection contre les logiciels malveillants.",
                hvci ? DiagFix.Gaming : DiagFix.None));

        checks.Add(w.StartupEnabled > 20
            ? new("Programmes au démarrage", DiagStatus.Red, $"{w.StartupEnabled} activés", "Beaucoup de programmes se lancent avec Windows et tournent en fond : désactive ceux dont tu n'as pas besoin.", DiagFix.Startup)
            : w.StartupEnabled > 10
                ? new("Programmes au démarrage", DiagStatus.Orange, $"{w.StartupEnabled} activés", "Pas mal de programmes au démarrage : regarde si certains sont inutiles.", DiagFix.Startup)
                : new("Programmes au démarrage", DiagStatus.Green, $"{w.StartupEnabled} activés", "Peu de programmes au démarrage."));

        if (w.IdleCpuPercent is { } cpu)
        {
            var top = w.TopProcesses.Where(p => p.Percent >= 1).Take(3).Select(p => $"{p.Name} {p.Percent:0} %").ToList();
            var value = $"{cpu:0} % en moyenne sur 30 s (hors Unpark)" + (top.Count > 0 ? $" — {string.Join(", ", top)}" : "");
            checks.Add(cpu > 25
                ? new("Processeur au repos", DiagStatus.Red, value, "Le processeur travaille beaucoup alors que rien ne tourne : un programme consomme en fond (voir la liste). Vérifie-le dans le Gestionnaire des tâches.")
                : cpu > 10
                    ? new("Processeur au repos", DiagStatus.Orange, value, "Un peu d'activité en fond : regarde les programmes listés.")
                    : new("Processeur au repos", DiagStatus.Green, value, "Le processeur est calme au repos."));
        }
        return checks;
    }

    // ---------- Réseau ----------

    public static IReadOnlyList<DiagCheck> Network(NetworkData? n)
    {
        if (n is null) return [];
        if (n.Received == 0)
            return [new("Connexion", DiagStatus.Red, $"Aucune réponse de {n.Target}", "Pas de réponse : pas de connexion Internet, ou le ping est bloqué par un pare-feu.")];

        var checks = new List<DiagCheck>();
        var loss = 100.0 * (n.Sent - n.Received) / n.Sent;
        checks.Add(loss > 2
            ? new("Perte de paquets", DiagStatus.Red, $"{loss:0.#} %", "Des paquets se perdent : téléportations et coupures en jeu. Essaie en câble Ethernet ; si ça continue, contacte ton fournisseur.")
            : loss > 0
                ? new("Perte de paquets", DiagStatus.Orange, $"{loss:0.#} %", "Quelques pertes : surveille, surtout en Wi-Fi.")
                : new("Perte de paquets", DiagStatus.Green, "0 %", "Aucune perte."));

        if (n.AverageMs is { } ping)
            checks.Add(ping > 80
                ? new($"Ping ({n.Target})", DiagStatus.Red, $"{ping:0} ms", "Latence élevée vers Internet : en jeu, ça dépend surtout du serveur choisi. Vérifie qu'aucun téléchargement ne tourne.")
                : ping > 40
                    ? new($"Ping ({n.Target})", DiagStatus.Orange, $"{ping:0} ms", "Latence moyenne.")
                    : new($"Ping ({n.Target})", DiagStatus.Green, $"{ping:0} ms", "Bonne latence."));

        if (n.JitterMs is { } jitter)
            checks.Add(jitter > 15
                ? new("Gigue (stabilité du ping)", DiagStatus.Red, $"{jitter:0} ms", "Le ping varie beaucoup : sensation de lag irrégulier. Souvent le Wi-Fi ou un téléchargement en cours.")
                : jitter > 5
                    ? new("Gigue (stabilité du ping)", DiagStatus.Orange, $"{jitter:0} ms", "Ping un peu irrégulier.")
                    : new("Gigue (stabilité du ping)", DiagStatus.Green, $"{jitter:0} ms", "Ping stable."));

        if (n.IsWifi)
            checks.Add(n.GatewayAverageMs is > 10
                ? new("Wi-Fi", DiagStatus.Orange, $"{n.GatewayAverageMs:0} ms jusqu'à la box", "Le Wi-Fi ajoute de la latence avant même Internet. Un câble Ethernet donne un ping plus bas et plus stable.")
                : new("Wi-Fi", DiagStatus.Info, n.GatewayAverageMs is { } g ? $"{g:0} ms jusqu'à la box" : "Connecté en Wi-Fi", "En Wi-Fi : un câble Ethernet reste plus stable pour jouer."));
        return checks;
    }

    // ---------- Rapport ----------

    public static string Report(DiagnosticData data, IReadOnlyList<DiagSection> sections)
    {
        static string Mark(DiagStatus s) => s switch { DiagStatus.Green => "[OK]", DiagStatus.Orange => "[!]", DiagStatus.Red => "[X]", _ => "[i]" };
        var all = sections.SelectMany(s => s.Checks).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"**Diagnostic {AppInfo.Name} {AppInfo.DisplayVersion}** — {data.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"{data.WindowsVersion}");
        sb.AppendLine($"Résumé : {all.Count(c => c.Status == DiagStatus.Red)} problème(s), {all.Count(c => c.Status == DiagStatus.Orange)} point(s) à vérifier, {all.Count(c => c.Status == DiagStatus.Green)} OK");
        sb.AppendLine("```");
        foreach (var section in sections)
        {
            sb.AppendLine($"== {section.Title} ==");
            foreach (var c in section.Checks) sb.AppendLine($"{Mark(c.Status)} {c.Title} : {c.Value}");
        }
        sb.AppendLine("```");
        return sb.ToString().TrimEnd();
    }
}
