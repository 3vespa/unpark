using Unpark.Core.Registry;

namespace Unpark.Core.Settings;

/// <summary>
/// Réglage de TEST, sans aucun effet sur Windows : écrit une valeur dans HKCU\Software\Unpark.
/// Sert à vérifier sur un vrai PC que sauvegarde, journal et annulation fonctionnent. Sera retiré à l'étape 3.
/// </summary>
public sealed class DemoSetting(SettingContext ctx) : RegistrySetting(
    ctx,
    id: "demo.test",
    name: "Réglage de test",
    description: "Écrit une valeur dans HKCU\\Software\\Unpark. Aucun effet sur Windows : sert à tester l'annulation.",
    requiresRestart: false,
    changes: [new RegistryChange(new RegPath(RegHive.CurrentUser, @"Software\Unpark", "Demo"), RegValue.DWord(1))]);
