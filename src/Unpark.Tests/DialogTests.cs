using System.Text.RegularExpressions;
using Unpark.Core.Ui;

namespace Unpark.Tests;

public class DialogTests
{
    [Fact]
    public void Oui_Non_Entree_valide_et_Echap_refuse()
    {
        var b = DialogLayout.For(DialogButtons.YesNo);
        Assert.Equal(["Oui", "Non"], b.Select(x => x.Label));
        Assert.Equal(DialogChoice.Yes, DialogLayout.EnterChoice(DialogButtons.YesNo));
        Assert.Equal(DialogChoice.No, DialogLayout.EscapeChoice(DialogButtons.YesNo));
    }

    [Fact]
    public void Oui_Non_Annuler_Echap_annule_comme_avant()
    {
        // Le point de restauration : Annuler = ne rien modifier. Échap doit donner Annuler, jamais Non (qui continue sans point).
        Assert.Equal(["Oui", "Non", "Annuler"], DialogLayout.For(DialogButtons.YesNoCancel).Select(x => x.Label));
        Assert.Equal(DialogChoice.Yes, DialogLayout.EnterChoice(DialogButtons.YesNoCancel));
        Assert.Equal(DialogChoice.Cancel, DialogLayout.EscapeChoice(DialogButtons.YesNoCancel));
    }

    [Fact]
    public void OK_seul_Entree_et_Echap_ferment()
    {
        var b = Assert.Single(DialogLayout.For(DialogButtons.Ok));
        Assert.Equal("OK", b.Label);
        Assert.True(b.IsPrimary && b.IsCancel);
    }

    [Theory]
    [InlineData(DialogButtons.Ok)]
    [InlineData(DialogButtons.YesNo)]
    [InlineData(DialogButtons.YesNoCancel)]
    public void Un_seul_bouton_principal_et_un_seul_bouton_Echap(DialogButtons buttons)
    {
        var b = DialogLayout.For(buttons);
        Assert.Single(b, x => x.IsPrimary);
        Assert.Single(b, x => x.IsCancel);
    }

    private static string SourceDir([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "Unpark"));

    [Fact]
    public void Plus_aucune_boite_de_dialogue_Windows_dans_l_appli()
    {
        var offenders = Directory.EnumerateFiles(SourceDir(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bMessageBox\b"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Integrite_de_la_memoire_confirmee_avec_le_bouton_rouge()
    {
        var safety = File.ReadAllText(Path.Combine(SourceDir(), "Services", "SafetyFlow.cs"));
        Assert.Contains("Dialogs.Confirm(w.Warning, danger: true)", safety);
    }
}
