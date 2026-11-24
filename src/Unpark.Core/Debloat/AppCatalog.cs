namespace Unpark.Core.Debloat;

public enum AppPolicy
{
    /// <summary>Sûre à retirer : cochée par défaut.</summary>
    Recommended,
    /// <summary>Proposée mais décochée (peut servir, ou contient des données perso).</summary>
    Optional,
    /// <summary>Jamais proposée (Store, Calculatrice, Photos, Terminal, Xbox, pilotes, codecs, composants système).</summary>
    Never,
}

public sealed record AppCatalogEntry(string Name, string FrenchName, AppPolicy Policy, bool IsPrefix = false)
{
    public bool Matches(string packageName) => IsPrefix
        ? packageName.StartsWith(Name, StringComparison.OrdinalIgnoreCase)
        : packageName.Equals(Name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Catalogue des applis préinstallées connues. Une appli absente du catalogue est proposée décochée.</summary>
public static class AppCatalog
{
    public static IReadOnlyList<AppCatalogEntry> Entries { get; } =
    [
        // Sûres à retirer (cochées).
        new("Clipchamp.Clipchamp", "Clipchamp (montage vidéo)", AppPolicy.Recommended),
        new("Microsoft.BingNews", "Actualités", AppPolicy.Recommended),
        new("Microsoft.BingWeather", "Météo", AppPolicy.Recommended),
        new("Microsoft.BingSearch", "Recherche Bing", AppPolicy.Recommended),
        new("Microsoft.GetHelp", "Obtenir de l'aide", AppPolicy.Recommended),
        new("Microsoft.Getstarted", "Astuces", AppPolicy.Recommended),
        new("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", AppPolicy.Recommended),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office)", AppPolicy.Recommended),
        new("Microsoft.WindowsFeedbackHub", "Hub de commentaires", AppPolicy.Recommended),
        new("Microsoft.WindowsMaps", "Cartes", AppPolicy.Recommended),
        new("Microsoft.549981C3F5F10", "Cortana", AppPolicy.Recommended),
        new("MicrosoftTeams", "Teams (personnel, ancienne version)", AppPolicy.Recommended),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", AppPolicy.Recommended),
        new("Microsoft.People", "Contacts", AppPolicy.Recommended),
        new("Microsoft.MixedReality.Portal", "Portail de réalité mixte", AppPolicy.Recommended),
        new("Microsoft.Microsoft3DViewer", "Visionneuse 3D", AppPolicy.Recommended),
        new("Microsoft.SkypeApp", "Skype", AppPolicy.Recommended),
        new("Microsoft.Messaging", "Messagerie", AppPolicy.Recommended),
        new("king.com.", "Jeux King (Candy Crush…)", AppPolicy.Recommended, IsPrefix: true),

        // Proposées, décochées.
        new("Microsoft.XboxGamingOverlay", "Xbox Game Bar", AppPolicy.Optional),
        new("Microsoft.YourPhone", "Lien avec Windows (téléphone)", AppPolicy.Optional),
        new("Microsoft.ZuneMusic", "Lecteur multimédia / Groove Musique", AppPolicy.Optional),
        new("Microsoft.ZuneVideo", "Films et TV", AppPolicy.Optional),
        new("Microsoft.WindowsSoundRecorder", "Enregistreur vocal", AppPolicy.Optional),
        new("Microsoft.WindowsAlarms", "Horloge et alarmes", AppPolicy.Optional),
        new("microsoft.windowscommunicationsapps", "Courrier et Calendrier", AppPolicy.Optional),
        new("Microsoft.OutlookForWindows", "Outlook (nouveau)", AppPolicy.Optional),
        new("Microsoft.Todos", "Microsoft To Do", AppPolicy.Optional),
        new("Microsoft.MicrosoftStickyNotes", "Pense-bêtes", AppPolicy.Optional),
        new("Microsoft.Office.OneNote", "OneNote pour Windows 10", AppPolicy.Optional),
        new("MicrosoftWindows.Client.WebExperience", "Widgets", AppPolicy.Optional),
        new("Microsoft.Windows.DevHome", "Dev Home", AppPolicy.Optional),
        new("MSTeams", "Microsoft Teams", AppPolicy.Optional),
        new("Microsoft.Copilot", "Copilot", AppPolicy.Optional),
        new("Microsoft.ScreenSketch", "Outil Capture d'écran", AppPolicy.Optional),
        new("Microsoft.Paint", "Paint", AppPolicy.Optional),
        new("Microsoft.MSPaint", "Paint 3D", AppPolicy.Optional),
        new("Microsoft.WindowsCamera", "Caméra", AppPolicy.Optional),
        new("SpotifyAB.SpotifyMusic", "Spotify", AppPolicy.Optional),
        new("Disney.37853FC22B2CE", "Disney+", AppPolicy.Optional),
        new("BytedancePte.Ltd.TikTok", "TikTok", AppPolicy.Optional),
        new("AmazonVideo.PrimeVideo", "Prime Video", AppPolicy.Optional),
        new("Facebook.", "Facebook / Instagram", AppPolicy.Optional, IsPrefix: true),

        // Jamais proposées.
        new("Microsoft.WindowsStore", "Microsoft Store", AppPolicy.Never),
        new("Microsoft.StorePurchaseApp", "Achats du Store", AppPolicy.Never),
        new("Microsoft.WindowsCalculator", "Calculatrice", AppPolicy.Never),
        new("Microsoft.Windows.Photos", "Photos", AppPolicy.Never),
        new("Microsoft.WindowsTerminal", "Terminal", AppPolicy.Never),
        new("Microsoft.WindowsNotepad", "Bloc-notes", AppPolicy.Never),
        new("Microsoft.DesktopAppInstaller", "Programme d'installation d'application (winget)", AppPolicy.Never),
        new("Microsoft.SecHealthUI", "Sécurité Windows", AppPolicy.Never),
        new("Microsoft.Xbox", "Composants Xbox (nécessaires à certains jeux)", AppPolicy.Never, IsPrefix: true),
        new("Microsoft.GamingApp", "Xbox", AppPolicy.Never),
        new("Microsoft.GamingServices", "Gaming Services", AppPolicy.Never),
        new("Microsoft.MicrosoftEdge", "Microsoft Edge", AppPolicy.Never, IsPrefix: true),
        new("Microsoft.Edge", "Microsoft Edge", AppPolicy.Never, IsPrefix: true),
        new("Microsoft.OneDriveSync", "OneDrive", AppPolicy.Never),
        new("Microsoft.LanguageExperiencePack", "Module de langue", AppPolicy.Never, IsPrefix: true),
        new("Microsoft.HEIFImageExtension", "Codec HEIF", AppPolicy.Never),
        new("Microsoft.HEVCVideoExtension", "Codec HEVC", AppPolicy.Never),
        new("Microsoft.VP9VideoExtensions", "Codec VP9", AppPolicy.Never),
        new("Microsoft.AV1VideoExtension", "Codec AV1", AppPolicy.Never),
        new("Microsoft.MPEG2VideoExtension", "Codec MPEG-2", AppPolicy.Never),
        new("Microsoft.AVCEncoderVideoExtension", "Codec AVC", AppPolicy.Never),
        new("Microsoft.WebMediaExtensions", "Extensions multimédias web", AppPolicy.Never),
        new("Microsoft.WebpImageExtension", "Codec WebP", AppPolicy.Never),
        new("Microsoft.RawImageExtension", "Codec RAW", AppPolicy.Never),
        new("Microsoft.ApplicationCompatibilityEnhancements", "Compatibilité des applications", AppPolicy.Never),
        new("Microsoft.WindowsAppRuntime", "Windows App Runtime", AppPolicy.Never, IsPrefix: true),
        new("Microsoft.VCLibs", "Bibliothèques Visual C++", AppPolicy.Never, IsPrefix: true),
        new("Microsoft.NET", "Bibliothèques .NET", AppPolicy.Never, IsPrefix: true),
        new("Microsoft.UI.Xaml", "Bibliothèques XAML", AppPolicy.Never, IsPrefix: true),
        new("Microsoft.Services.Store.Engagement", "Services du Store", AppPolicy.Never),
        new("Microsoft.Winget", "winget", AppPolicy.Never, IsPrefix: true),
        new("Microsoft.Windows.", "Composant Windows", AppPolicy.Never, IsPrefix: true),
        new("MicrosoftWindows.", "Composant Windows", AppPolicy.Never, IsPrefix: true),
        new("windows.", "Composant Windows", AppPolicy.Never, IsPrefix: true),
        new("NVIDIACorp.", "Panneau NVIDIA", AppPolicy.Never, IsPrefix: true),
        new("AdvancedMicroDevicesInc", "Logiciel AMD", AppPolicy.Never, IsPrefix: true),
        new("AppUp.Intel", "Logiciel Intel", AppPolicy.Never, IsPrefix: true),
        new("RealtekSemiconductorCorp.", "Audio Realtek", AppPolicy.Never, IsPrefix: true),
        new("DolbyLaboratories.", "Audio Dolby", AppPolicy.Never, IsPrefix: true),
        new("WavesAudio.", "Audio Waves", AppPolicy.Never, IsPrefix: true),
    ];

    /// <summary>Entrée du catalogue : nom exact d'abord, puis préfixe.</summary>
    public static AppCatalogEntry? Find(string packageName) =>
        Entries.FirstOrDefault(e => !e.IsPrefix && e.Matches(packageName)) ??
        Entries.FirstOrDefault(e => e.IsPrefix && e.Matches(packageName));
}
