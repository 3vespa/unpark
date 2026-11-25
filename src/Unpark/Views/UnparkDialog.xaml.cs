using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Unpark.Core.Ui;

namespace Unpark.Views;

/// <summary>
/// Fenêtre de confirmation dans le style d'Unpark (remplace les boîtes de dialogue grises de Windows).
/// Entrée = bouton principal, Échap = choix prudent, Ctrl+C = copier le texte (comme les boîtes de Windows).
/// </summary>
public partial class UnparkDialog : Window
{
    private readonly string _message;

    public UnparkDialog(string message, DialogKind kind, DialogButtons buttons, bool danger)
    {
        InitializeComponent();
        _message = message;
        MessageText.Text = message;
        IconGlyph.Text = DialogLayout.Glyph(kind);
        IconGlyph.Foreground = (Brush)FindResource(kind switch
        {
            DialogKind.Warning => "WarnBrush",
            DialogKind.Error => "DangerBrush",
            DialogKind.Question => danger ? "DangerButtonBrush" : "AccentBrush",
            _ => "InfoBrush",
        });

        // Alt+F4 ou fermeture : même choix qu'Échap.
        Choice = DialogLayout.EscapeChoice(buttons);
        Button? primary = null;
        foreach (var spec in DialogLayout.For(buttons))
        {
            var button = new Button
            {
                Content = spec.Label,
                MinWidth = 104,
                Margin = new Thickness(10, 0, 0, 0),
                IsDefault = spec.IsPrimary,
                IsCancel = spec.IsCancel,
            };
            if (spec.IsPrimary)
            {
                button.Style = (Style)FindResource(danger ? "DangerButton" : "AccentButton");
                primary = button;
            }
            var choice = spec.Choice;
            var closesItself = spec.IsCancel; // WPF ferme déjà la fenêtre (DialogResult = false) après le clic d'un bouton IsCancel
            button.Click += (_, _) =>
            {
                Choice = choice;
                if (!closesItself) DialogResult = true;
            };
            ButtonsPanel.Children.Add(button);
        }
        Loaded += (_, _) => primary?.Focus();
    }

    public DialogChoice Choice { get; private set; }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            try { Clipboard.SetText(_message); } catch { /* presse-papiers occupé : sans gravité */ }
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
