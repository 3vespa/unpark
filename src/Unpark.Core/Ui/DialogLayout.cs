namespace Unpark.Core.Ui;

/// <summary>Icône et couleur de la fenêtre de confirmation.</summary>
public enum DialogKind { Info, Warning, Error, Question }

/// <summary>Boutons proposés (mêmes jeux que les anciennes boîtes de dialogue Windows).</summary>
public enum DialogButtons { Ok, YesNo, YesNoCancel }

public enum DialogChoice { Ok, Yes, No, Cancel }

/// <param name="IsPrimary">Bouton mis en avant (vert, ou rouge pour une action risquée). Validé par Entrée.</param>
/// <param name="IsCancel">Choisi par Échap (ou la fermeture de la fenêtre).</param>
public sealed record DialogButton(string Label, DialogChoice Choice, bool IsPrimary, bool IsCancel);

/// <summary>
/// Boutons des fenêtres de confirmation d'Unpark, sans dépendance à WPF (testable).
/// Entrée = bouton principal ; Échap = le choix le plus prudent (Non, ou Annuler quand il existe).
/// </summary>
public static class DialogLayout
{
    public static IReadOnlyList<DialogButton> For(DialogButtons buttons) => buttons switch
    {
        DialogButtons.Ok => [new("OK", DialogChoice.Ok, IsPrimary: true, IsCancel: true)],
        DialogButtons.YesNo =>
        [
            new("Oui", DialogChoice.Yes, IsPrimary: true, IsCancel: false),
            new("Non", DialogChoice.No, IsPrimary: false, IsCancel: true),
        ],
        DialogButtons.YesNoCancel =>
        [
            new("Oui", DialogChoice.Yes, IsPrimary: true, IsCancel: false),
            new("Non", DialogChoice.No, IsPrimary: false, IsCancel: false),
            new("Annuler", DialogChoice.Cancel, IsPrimary: false, IsCancel: true),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(buttons)),
    };

    /// <summary>Choix renvoyé par Entrée.</summary>
    public static DialogChoice EnterChoice(DialogButtons buttons) => For(buttons).Single(b => b.IsPrimary).Choice;

    /// <summary>Choix renvoyé par Échap, Alt+F4 ou la fermeture de la fenêtre.</summary>
    public static DialogChoice EscapeChoice(DialogButtons buttons) => For(buttons).Single(b => b.IsCancel).Choice;

    /// <summary>Glyphe Segoe MDL2 Assets de l'icône.</summary>
    public static string Glyph(DialogKind kind) => kind switch
    {
        DialogKind.Warning => "",
        DialogKind.Error => "",
        DialogKind.Question => "",
        _ => "",
    };
}
