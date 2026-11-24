using System.Windows;

namespace Unpark.Services;

/// <summary>Boîtes de dialogue (toutes en français).</summary>
public static class Dialogs
{
    private static Window? Owner => Application.Current?.MainWindow;

    public static void Info(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Information);
    public static void Warn(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Warning);
    public static void Error(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    public static bool Confirm(string message) =>
        Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>Oui / Non / Annuler.</summary>
    public static MessageBoxResult Ask(string message) =>
        Show(message, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage icon) =>
        Owner is { IsLoaded: true } owner
            ? MessageBox.Show(owner, message, "Unpark", buttons, icon)
            : MessageBox.Show(message, "Unpark", buttons, icon);
}
