using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core;
using Unpark.Core.Disks;
using Unpark.Services;

namespace Unpark.ViewModels;

/// <summary>Une ligne de « Optimisation des lecteurs » : un lecteur, son type, sa dernière optimisation, son état.</summary>
public sealed partial class DriveRowViewModel : ObservableObject
{
    private readonly DriveOptimizationViewModel _parent;

    public DriveRowViewModel(VolumeInfo volume, DateTime? last, DriveOptimizationViewModel parent)
    {
        Volume = volume;
        _parent = parent;
        _lastOptimized = last;
        Refresh();
    }

    public VolumeInfo Volume { get; }
    public string Name => string.IsNullOrWhiteSpace(Volume.Label) ? $"{Volume.Letter}:" : $"{Volume.Letter}:  {Volume.Label}";
    public string MediaText => Volume.MediaText;
    public string SizeText => Volume.SizeBytes > 0 ? Format.Bytes(Volume.SizeBytes) : "";
    public bool CanAnalyze => Volume.Media == MediaKind.Hdd;
    public bool CanOptimize => DiskOptimizer.OperationFor(Volume.Media) is not null;
    public string OptimizeTip => Volume.Media switch
    {
        MediaKind.Ssd => "SSD : TRIM (jamais de défragmentation)",
        MediaKind.Hdd => "Disque dur : défragmentation",
        _ => "Type de disque inconnu : Unpark ne choisit pas le traitement à l'aveugle (utilise l'outil Windows « Optimiser les lecteurs »).",
    };
    public string AnalyzeTip => CanAnalyze ? "Mesure la fragmentation (sans rien modifier)" : "Pas d'analyse pour un SSD (comme dans l'outil Windows) : il n'a pas besoin d'être défragmenté.";

    [ObservableProperty] private DateTime? _lastOptimized;
    [ObservableProperty] private int? _fragmentation;
    [ObservableProperty] private string _lastText = "";
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private bool _needsAttention;

    // Opération en cours sur ce lecteur.
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";

    public bool AnalyzeEnabled => CanAnalyze && !_parent.IsRunning;
    public bool OptimizeEnabled => CanOptimize && !_parent.IsRunning;

    partial void OnLastOptimizedChanged(DateTime? value) => Refresh();
    partial void OnFragmentationChanged(int? value) => Refresh();

    public void Refresh()
    {
        var now = DateTime.Now;
        LastText = DiskOptimizer.LastText(LastOptimized, now);
        StateText = DiskOptimizer.StateText(Volume.Media, LastOptimized, Fragmentation, now);
        NeedsAttention = DiskOptimizer.NeedsAttention(Volume.Media, LastOptimized, Fragmentation, now);
    }

    public void NotifyParentBusy()
    {
        OnPropertyChanged(nameof(AnalyzeEnabled));
        OnPropertyChanged(nameof(OptimizeEnabled));
    }

    [RelayCommand]
    private Task AnalyzeAsync() => _parent.AnalyzeAsync(this);

    [RelayCommand]
    private Task OptimizeAsync() => _parent.OptimizeAsync([this]);
}

/// <summary>
/// Section « Optimisation des lecteurs » de la page Nettoyage : l'équivalent de l'outil Windows (dfrgui).
/// Une seule opération à la fois ; l'avancement s'affiche dans la ligne du lecteur, l'appli reste utilisable.
/// </summary>
public sealed partial class DriveOptimizationViewModel : ObservableObject
{
    private static AppServices S => AppServices.Current;
    private CancellationTokenSource? _cts;

    /// <summary>Optimisation en cours (pour prévenir avant de fermer l'appli), null sinon.</summary>
    public static DriveOptimizationViewModel? Running { get; private set; }

    public DriveOptimizationViewModel()
    {
        Scheduled = new SettingItemViewModel(S.ScheduledOptimization);
        S.StateChanged += RefreshScheduled;
        RefreshScheduled();
        _ = LoadAsync();
    }

    public ObservableCollection<DriveRowViewModel> Rows { get; } = [];
    public SettingItemViewModel Scheduled { get; }

    [ObservableProperty] private string _scheduledText = "";
    [ObservableProperty] private bool _scheduledEnabled;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private string? _resultText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;
    public bool IsIdle => !IsRunning;
    partial void OnIsRunningChanged(bool value)
    {
        foreach (var row in Rows) row.NotifyParentBusy();
        Running = value ? this : null;
    }

    private void RefreshScheduled()
    {
        Scheduled.Refresh();
        try
        {
            ScheduledText = S.ScheduledOptimization.StatusText();
            ScheduledEnabled = S.ScheduledOptimization.IsApplied();
        }
        catch (Exception e)
        {
            S.Log.Error("Lecture de l'optimisation planifiée impossible", e);
            ScheduledText = "État illisible";
            ScheduledEnabled = false;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsRunning || IsLoading) return;
        IsLoading = true;
        LoadError = null;
        try
        {
            var (volumes, last) = await Task.Run(() => (S.Disks.Volumes(), S.Disks.LastOptimizations()));
            Rows.Clear();
            foreach (var v in volumes)
                Rows.Add(new DriveRowViewModel(v, last.TryGetValue(v.Letter, out var d) ? d : null, this));
            if (Rows.Count == 0) LoadError = "Aucun lecteur fixe trouvé.";
        }
        catch (Exception e)
        {
            S.Log.Error("Liste des lecteurs illisible", e);
            LoadError = $"Liste des lecteurs illisible : {e.Message}";
        }
        finally { IsLoading = false; }
    }

    public async Task AnalyzeAsync(DriveRowViewModel row)
    {
        if (IsRunning || !row.CanAnalyze) return;
        IsRunning = true;
        ResultText = null;
        row.IsWorking = true;
        row.IsIndeterminate = true;
        row.ProgressText = "Analyse en cours… (quelques secondes à quelques minutes)";
        try
        {
            row.Fragmentation = await Task.Run(() => S.Disks.Analyze(row.Volume));
            ResultText = $"{row.Volume.Letter}: {row.Fragmentation} % fragmenté.";
        }
        catch (Exception e)
        {
            S.Log.Error($"Analyse de {row.Volume.Letter}: impossible", e);
            Dialogs.Error($"Analyse de {row.Volume.Letter}: impossible : {e.Message}");
        }
        finally
        {
            row.IsWorking = false;
            IsRunning = false;
        }
    }

    [RelayCommand]
    private Task OptimizeAllAsync()
    {
        var todo = Rows.Where(r => r.CanOptimize).ToList();
        if (todo.Count == 0)
        {
            Dialogs.Info("Aucun lecteur à optimiser : le type des lecteurs trouvés est inconnu.");
            return Task.CompletedTask;
        }
        return OptimizeAsync(todo);
    }

    public async Task OptimizeAsync(IReadOnlyList<DriveRowViewModel> rows)
    {
        if (IsRunning || rows.Count == 0) return;
        IsRunning = true;
        ResultText = null;
        _cts = new CancellationTokenSource();
        var messages = new List<string>();
        var failed = new List<string>();
        try
        {
            foreach (var row in rows)
            {
                if (_cts.IsCancellationRequested) break;
                row.IsWorking = true;
                row.IsIndeterminate = true;
                row.Progress = 0;
                row.ProgressText = $"Préparation : {DiskOptimizer.OperationName(DiskOptimizer.OperationFor(row.Volume.Media)!.Value)}…";
                // Progress<T> créé ici (fil de l'interface) : les mises à jour reviennent sur ce fil.
                var progress = new Progress<OptimizeProgress>(p =>
                {
                    row.IsIndeterminate = false;
                    row.Progress = p.Percent;
                    row.ProgressText = p.Text;
                });
                var token = _cts.Token;
                var result = await Task.Run(() => S.Disks.Optimize(row.Volume, progress, token));
                row.IsWorking = false;
                if (result.Success)
                {
                    row.Fragmentation = null;
                    row.LastOptimized = DateTime.Now;
                }
                messages.Add(result.Message);
                if (!result.Success && !result.Cancelled) failed.Add(result.Message);
                if (result.Cancelled) break;
            }
        }
        finally
        {
            foreach (var row in rows) row.IsWorking = false;
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
        }
        ResultText = string.Join("\n", messages);
        if (failed.Count > 0) Dialogs.Warn(string.Join("\n", failed));
    }

    [RelayCommand]
    public void Stop() => _cts?.Cancel();
}
