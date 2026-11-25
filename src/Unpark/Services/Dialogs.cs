using System.Windows;
using Unpark.Core.Ui;
using Unpark.Views;

namespace Unpark.Services;

/// <summary>Fenêtres de confirmation (toutes en français), dans le style d'Unpark.</summary>
public static class Dialogs
{
    public static void Info(string message) => Show(message, DialogKind.Info, DialogButtons.Ok);
    public static void Warn(string message) => Show(message, DialogKind.Warning, DialogButtons.Ok);
    public static void Error(string message) => Show(message, DialogKind.Error, DialogButtons.Ok);

    /// <summary>Oui / Non. <paramref name="danger"/> : action risquée ou définitive, bouton « Oui » en rouge.</summary>
    public static bool Confirm(string message, bool danger = false) =>
        Show(message, danger ? DialogKind.Warning : DialogKind.Question, DialogButtons.YesNo, danger) == DialogChoice.Yes;

    /// <summary>Oui / Non / Annuler.</summary>
    public static DialogChoice Ask(string message) => Show(message, DialogKind.Question, DialogButtons.YesNoCancel);

    private static DialogChoice Show(string message, DialogKind kind, DialogButtons buttons, bool danger = false)
    {
        var app = Application.Current;
        if (app is null) return DialogLayout.EscapeChoice(buttons);
        if (!app.Dispatcher.CheckAccess())
            return app.Dispatcher.Invoke(() => Show(message, kind, buttons, danger));

        var dialog = new UnparkDialog(message, kind, buttons, danger);
        if (app.MainWindow is { IsLoaded: true, IsVisible: true } owner && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            // Pas encore de fenêtre principale (erreur au démarrage) : fenêtre autonome, visible dans la barre des tâches.
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
        }
        dialog.ShowDialog();
        return dialog.Choice;
    }
}
