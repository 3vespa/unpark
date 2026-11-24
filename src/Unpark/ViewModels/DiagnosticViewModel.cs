using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core.Diagnostics;
using Unpark.Services;

namespace Unpark.ViewModels;

public sealed partial class DiagCheckViewModel(DiagCheck check) : ObservableObject
{
    public string Title => check.Title;
    public string Value => check.Value;
    public string Explanation => check.Explanation;
    public DiagStatus Status => check.Status;
    public bool HasFix => check.Fix != DiagFix.None;
    public string FixText => check.Fix switch
    {
        DiagFix.DisplaySettings or DiagFix.GraphicsSettings or DiagFix.WindowsUpdate => "Ouvrir les paramètres",
        _ => "Corriger dans Unpark",
    };

    [RelayCommand]
    private void Fix()
    {
        var s = AppServices.Current;
        switch (check.Fix)
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

    private static void Open(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
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
                Sections.Add(new DiagSectionViewModel(section.Title, section.Checks.Select(c => new DiagCheckViewModel(c)).ToList()));
            var all = sections.SelectMany(s => s.Checks).ToList();
            RedCount = all.Count(c => c.Status == DiagStatus.Red);
            OrangeCount = all.Count(c => c.Status == DiagStatus.Orange);
            GreenCount = all.Count(c => c.Status == DiagStatus.Green);
            Summary = RedCount > 0 ? "Des problèmes ont été trouvés : commence par les points en rouge."
                : OrangeCount > 0 ? "Rien de grave, mais quelques points peuvent être améliorés."
                : "Tout est bon !";
            HasResult = true;
        }
        catch (Exception e)
        {
            S.Log.Error("Diagnostic impossible", e);
            Dialogs.Error($"Le diagnostic n'a pas pu se terminer : {e.Message}");
        }
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
