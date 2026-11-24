# Unpark — Optimisation PC gaming

Appli Windows (10 22H2 et 11) pour optimiser un PC pour le jeu, **sans rien casser** : chaque réglage est sauvegardé avant d'être modifié et s'annule en un clic.

> 🚧 En construction. Le guide complet (installation, SmartScreen, point de restauration, tout annuler) arrive avec la version 1.

## Compiler (sur le serveur Linux)

```bash
./build.sh            # tests + publication → releases/Unpark-vX.Y.Z.exe
./build.sh --force    # remplace l'exe de la même version
```

- La version se change dans `src/Directory.Build.props` (`<Version>`).
- Il faut le **SDK .NET 8 officiel de Microsoft** dans `~/.dotnet` : celui d'Ubuntu (`apt install dotnet-sdk-8.0`) ne contient pas WPF.
- L'exe est autonome (rien à installer sur le PC) et demande les droits administrateur au lancement.

## Structure

```
src/Unpark/         interface WPF (MVVM, CommunityToolkit.Mvvm, thème sombre)
src/Unpark.Core/    logique (réglages réversibles, sauvegarde, journal…)
src/Unpark.Tests/   tests automatiques (tournent sous Linux)
releases/           exe publiés (pas dans git)
```
