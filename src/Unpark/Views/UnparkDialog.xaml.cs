using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Unpark.Core.Ui;

namespace Unpark.Views;

/// <summary>
/// Fenêtre de confirmation dans le style d'Unpark (remplace les boîtes de dialogue grises de Windows).
/// Entrée = bouton principal, Échap = choix prudent, Ctrl+C = copier le texte (comme les boîtes de Windows).
/// </summary>
public partial class UnparkDialog : Window
{
    private string _message;
    private DispatcherTimer? _timer;

    public UnparkDialog(string message, DialogKind kind, DialogButtons buttons, bool danger)
        : this(message, kind, DialogLayout.For(buttons), danger) { }

    public UnparkDialog(string message, DialogKind kind, IReadOnlyList<DialogButton> buttons, bool danger)
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
        Choice = buttons.Single(b => b.IsCancel).Choice;
        Button? primary = null;
        foreach (var spec in buttons)
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
            if (spec.IsCancel) _cancelButton = (button, spec.Label);
        }
        Loaded += (_, _) => primary?.Focus();
    }

    private readonly (Button Button, string Label)? _cancelButton;

    public DialogChoice Choice { get; private set; }

    /// <summary>
    /// Compte à rebours : chaque seconde, le texte et le bouton « Échap » affichent le temps restant ;
    /// à zéro, la fenêtre se ferme avec le choix d'Échap (ex. « Revenir à 60 Hz »).
    /// </summary>
    public void StartCountdown(int seconds, Func<int, string> messageFor)
    {
        var left = seconds;
        void Show()
        {
            _message = messageFor(left);
            MessageText.Text = _message;
            if (_cancelButton is { } c) c.Button.Content = DialogLayout.CountdownLabel(c.Label, left);
        }
        Show();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            left--;
            if (left > 0) { Show(); return; }
            _timer.Stop();
            Close(); // Choice garde la valeur d'Échap
        };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

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
