using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core;
using Unpark.Core.Updates;
using Unpark.Services;

namespace Unpark.ViewModels;

public enum UpdateBannerState { Hidden, Downloading, Ready, Updated, Error }

/// <summary>
/// Bandeau discret en haut de la fenêtre : téléchargement en cours, mise à jour prête (« Redémarrer maintenant »),
/// mise à jour installée, ou mise à jour refusée. Sans Internet : rien ne s'affiche.
/// </summary>
public sealed partial class UpdateBannerViewModel : ObservableObject
{
    private static AppServices S => AppServices.Current;
    private StagedUpdate? _staged;

    /// <summary>Le bandeau de la fenêtre principale (une seule fenêtre).</summary>
    public static UpdateBannerViewModel? Instance { get; private set; }

    public UpdateBannerViewModel() => Instance = this;
    private bool _started;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsDownloading), nameof(IsReady), nameof(IsError), nameof(CanDismiss))]
    private UpdateBannerState _state;

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private double _progress;

    public bool IsVisible => State != UpdateBannerState.Hidden;
    public bool IsDownloading => State == UpdateBannerState.Downloading;
    public bool IsReady => State == UpdateBannerState.Ready;
    public bool IsError => State == UpdateBannerState.Error;
    public bool CanDismiss => State is UpdateBannerState.Updated or UpdateBannerState.Error;

    /// <summary>Lancé une fois la fenêtre affichée : n'en ralentit jamais l'ouverture.</summary>
    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;

        if (UpdateFlow.StartupError is { } error)
        {
            Show(UpdateBannerState.Error, error);
            return;
        }
        if (UpdateFlow.LocalStaged is { } local)
        {
            Ready(local);
            return;
        }
        if (UpdateFlow.JustUpdated) Show(UpdateBannerState.Updated, $"Unpark a été mis à jour en {AppInfo.DisplayVersion}.");
        if (!S.Preferences.AutoUpdate) return;

        var info = await S.Updates.CheckAsync();
        if (info is null || !S.Preferences.AutoUpdate) return;

        Progress = 0;
        Show(UpdateBannerState.Downloading, DownloadingText(info.Version, 0));
        var lastPercent = -1;
        var progress = new Progress<double>(p =>
        {
            var percent = (int)(p * 100);
            if (percent == lastPercent || State != UpdateBannerState.Downloading) return;
            lastPercent = percent;
            Progress = percent;
            Text = DownloadingText(info.Version, percent);
        });
        try
        {
            Ready(await Task.Run(() => S.Updates.DownloadAsync(info, progress)));
        }
        catch (UpdateRejectedException e)
        {
            S.Log.Warn($"Mise à jour v{info.Version} refusée : {e.Message}");
            Show(UpdateBannerState.Error, $"Mise à jour v{info.Version} refusée : {e.Message} Rien n'a été installé, ta version actuelle n'a pas été modifiée.");
        }
        catch (Exception e)
        {
            // Connexion coupée pendant le téléchargement : on réessaiera au prochain lancement, sans déranger.
            S.Log.Info($"Mise à jour v{info.Version} : téléchargement interrompu ({e.Message})");
            Show(UpdateBannerState.Hidden, "");
        }
    }

    /// <summary>L'option a été décochée : on arrête de proposer, et ce qui avait été téléchargé est supprimé.</summary>
    public void OnAutoUpdateDisabled()
    {
        if (State is UpdateBannerState.Ready && UpdateFlow.LocalStaged is null)
        {
            S.Updates.DeleteStaged();
            _staged = null;
            Show(UpdateBannerState.Hidden, "");
        }
    }

    private static string DownloadingText(string version, int percent) => $"Mise à jour v{version} en cours de téléchargement… {percent} %";

    private void Ready(StagedUpdate staged)
    {
        _staged = staged;
        Show(UpdateBannerState.Ready, $"Mise à jour v{staged.Version} prête. Sinon, elle s'installera toute seule à la prochaine ouverture d'Unpark.");
    }

    private void Show(UpdateBannerState state, string text)
    {
        Text = text;
        State = state;
    }

    [RelayCommand]
    private void RestartNow()
    {
        if (_staged is null) return;
        if (UpdateFlow.RunningOperation() is { } running)
        {
            Dialogs.Info($"Impossible de redémarrer maintenant : {running}.\n\nClique à nouveau quand elle sera terminée, ou ferme Unpark plus tard : la mise à jour s'installera à la prochaine ouverture.");
            return;
        }
        S.Log.Info($"Mise à jour v{_staged.Version} : redémarrage demandé par l'utilisateur");
        if (UpdateFlow.RestartNow(_staged) is { } error)
        {
            _staged = null;
            Show(UpdateBannerState.Error, error);
        }
    }

    [RelayCommand]
    private void Dismiss() => Show(UpdateBannerState.Hidden, "");
}
