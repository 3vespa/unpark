using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core.Diagnostics;
using Unpark.Core.Display;
using Unpark.Services;

namespace Unpark.ViewModels;

public sealed partial class DiagCheckViewModel : ObservableObject
{
    private readonly DiagCheck _check;
    private readonly Action _changed;

    public DiagCheckViewModel(DiagCheck check, Action changed)
    {
        _check = check;
        _changed = changed;
        _value = check.Value;
        _explanation = check.Explanation;
        _status = check.Status;
        _action = check.Action;
        Guide = (check.Action as BiosGuideAction)?.Guide;
    }

    public string Title => _check.Title;
    [ObservableProperty] private string _value;
    [ObservableProperty] private string _explanation;
    [ObservableProperty] private DiagStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFix), nameof(FixText), nameof(IsAccentFix))]
    private DiagAction? _action;

    /// <summary>Bouton sur la ligne : correction directe (fréquence, pilote) ou page qui corrige.</summary>
    public bool HasFix => Action is RefreshRateAction or DriverPageAction || (Action is null && _check.Fix != DiagFix.None);
    public bool IsAccentFix => Action is RefreshRateAction;
    public string FixText => Action switch
    {
        RefreshRateAction r => $"Passer à {r.TargetHz} Hz",
        DriverPageAction => "Mettre à jour",
        _ => _check.Fix switch
        {
            DiagFix.DisplaySettings or DiagFix.GraphicsSettings or DiagFix.WindowsUpdate => "Ouvrir les paramètres",
            _ => "Corriger dans Unpark",
        },
    };

    // ---------- Guide XMP / EXPO (dépliable) ----------

    public BiosGuide? Guide { get; }
    public bool HasGuide => Guide is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuideButtonText))]
    private bool _isGuideOpen;
    public string GuideButtonText => IsGuideOpen ? "Masquer le guide" : "Comment l'activer ?";

    [RelayCommand]
    private void ToggleGuide() => IsGuideOpen = !IsGuideOpen;

    [RelayCommand]
    private void Fix()
    {
        switch (Action)
        {
            case RefreshRateAction r: ChangeRefreshRate(r); return;
            case DriverPageAction d: OpenWebPage(d.Url); return;
        }
        var s = AppServices.Current;
        switch (_check.Fix)
        {
            case DiagFix.PowerPlan: s.Navigate(typeof(PowerPlanViewModel)); break;
            case DiagFix.Gaming: s.Navigate(typeof(GamingViewModel)); break;
            case DiagFix.Startup: s.Navigate(typeof(StartupViewModel)); break;
            case DiagFix.Cleanup: s.Navigate(typeof(CleanupViewModel)); break;
            case DiagFix.DisplaySettings: Open("ms-settings:display-advanced"); break;
            case DiagFix.GraphicsSettings: Open("ms-settings:display-advancedgraphics"); break;
            case DiagFix.WindowsUpdate: Open("ms-settings:windowsupdate"); break;
        }
    }

    /// <summary>
    /// Change la fréquence, puis « Garder cette fréquence ? » pendant 15 s. Sans réponse (écran noir…), retour automatique.
    /// Tout se passe sur la ligne : on ne change pas de page.
    /// </summary>
    private void ChangeRefreshRate(RefreshRateAction r)
    {
        var s = AppServices.Current;
        RefreshRateTrial trial;
        try
        {
            trial = s.RefreshRate.Start(r.DeviceName, r.DisplayName, r.TargetHz);
        }
        catch (Exception e)
        {
            Dialogs.Error($"Impossible de passer {r.DisplayName} à {r.TargetHz} Hz : {e.Message}\n\nRien n'a été changé.");
            return;
        }

        if (Dialogs.KeepDisplayChange(r.DisplayName, r.TargetHz, trial.PreviousHz))
        {
            s.RefreshRate.Keep(trial);
            Status = DiagStatus.Green;
            Value = $"{r.TargetHz} Hz (max {r.TargetHz} Hz) en {r.Width}×{r.Height}";
            Explanation = $"Réglé à {r.TargetHz} Hz par Unpark. Pour revenir à {trial.PreviousHz} Hz : Accueil → Tout annuler.";
            Action = null;
            s.NotifyChanged();
            _changed();
            return;
        }

        try
        {
            s.RefreshRate.Undo(trial, "pas gardée");
        }
        catch (Exception e)
        {
            s.Log.Error("Retour à l'ancienne fréquence impossible", e);
            Dialogs.Error($"Le retour à {trial.PreviousHz} Hz a échoué : {e.Message}\n\nChange-la à la main : Paramètres → Affichage → Affichage avancé. « Tout annuler » sait aussi la remettre.");
        }
        s.NotifyChanged();
    }

    private static void Open(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    /// <summary>
    /// Ouvre une page web dans le navigateur habituel. Passer par l'Explorateur évite de lancer le navigateur
    /// avec les droits administrateur d'Unpark. Rien n'est téléchargé par Unpark.
    /// </summary>
    private static void OpenWebPage(string url)
    {
        if (!url.StartsWith("https://", StringComparison.Ordinal)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = false });
        }
        catch
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        AppServices.Current.Log.Info($"Diagnostic : page officielle des pilotes ouverte ({url})");
    }
}

public sealed record DiagSectionViewModel(string Title, IReadOnlyList<DiagCheckViewModel> Checks);

/// <summary>Page « Diagnostic » : lecture seule, résultat vert / orange / rouge, rapport à copier pour Discord.</summary>
public sealed partial class DiagnosticViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;
    private string? _report;

    public DiagnosticViewModel() : base("Diagnostic", "") { }

    public ObservableCollection<DiagSectionViewModel> Sections { get; } = [];
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private int _redCount;
    [ObservableProperty] private int _orangeCount;
    [ObservableProperty] private int _greenCount;

    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            var data = await S.RunBusyAsync("Diagnostic en cours… (environ 40 secondes, rien n'est modifié)",
                progress => S.Diagnostics.RunAsync(progress));
            var sections = DiagnosticAnalyzer.Analyze(data);
            _report = DiagnosticAnalyzer.Report(data, sections);
            Sections.Clear();
            foreach (var section in sections)
                Sections.Add(new DiagSectionViewModel(section.Title, section.Checks.Select(c => new DiagCheckViewModel(c, Recount)).ToList()));
            Recount();
            HasResult = true;
        }
        catch (Exception e)
        {
            S.Log.Error("Diagnostic impossible", e);
            Dialogs.Error($"Le diagnostic n'a pas pu se terminer : {e.Message}");
        }
    }

    /// <summary>Compteurs et résumé (recalculés après une correction faite sur une ligne).</summary>
    private void Recount()
    {
        var all = Sections.SelectMany(s => s.Checks).ToList();
        RedCount = all.Count(c => c.Status == DiagStatus.Red);
        OrangeCount = all.Count(c => c.Status == DiagStatus.Orange);
        GreenCount = all.Count(c => c.Status == DiagStatus.Green);
        Summary = RedCount > 0 ? "Des problèmes ont été trouvés : commence par les points en rouge."
            : OrangeCount > 0 ? "Rien de grave, mais quelques points peuvent être améliorés."
            : "Tout est bon !";
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (_report is null) return;
        try
        {
            Clipboard.SetText(_report);
            Dialogs.Info("Rapport copié : colle-le sur Discord (Ctrl+V) pour demander de l'aide.");
        }
        catch (Exception e)
        {
            Dialogs.Warn($"Copie impossible pour l'instant ({e.Message}). Réessaie dans un instant.");
        }
    }
}
