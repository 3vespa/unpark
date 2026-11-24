# Unpark — Optimisation PC gaming

Petite appli Windows pour optimiser ton PC pour le jeu, **sans rien casser** : avant chaque modification, Unpark sauvegarde le réglage d'origine, et tout s'annule en un clic.

Fonctionne sur **Windows 10 (22H2)** et **Windows 11**. Rien à installer : c'est un seul fichier `.exe`.

---

## 1. Télécharger et lancer

1. Télécharge `Unpark-vX.Y.Z.exe` (le lien est sur Discord). Range-le où tu veux (Bureau, Documents…).
2. Double-clique dessus.
3. **Windows SmartScreen bloque ?** C'est normal : l'appli n'est pas signée (une signature coûte cher pour une appli entre potes).
   Clique sur **« Informations complémentaires »**, puis sur **« Exécuter quand même »**.
4. Windows demande **« Voulez-vous autoriser cette application à apporter des modifications ? »** → **Oui**.
   Unpark a besoin des droits administrateur pour changer les réglages de Windows.

> Le premier lancement peut prendre 2-3 secondes (l'exe se décompresse).

> **Ton antivirus râle ?** Une appli qui modifie le registre et le plan d'alimentation peut être signalée par erreur. En cas de doute, demande l'empreinte SHA-256 sur Discord et compare-la (PowerShell : `Get-FileHash .\Unpark-vX.Y.Z.exe`).

## 2. Le plus simple : « Gaming en 1 clic »

1. Dans le menu à gauche, ouvre **Gaming en 1 clic**.
2. Regarde la liste « Ce qui va être fait » (tu peux décocher ce que tu ne veux pas).
3. Clique sur **Tout appliquer** : un récapitulatif s'affiche, valide-le.
4. **Point de restauration** : à la première modification, Unpark propose d'en créer un. **Dis oui** (30 s à 1 min). Si Windows indique qu'il en existe déjà un récent, tu peux le garder.
5. À la fin, Unpark propose un nettoyage : tu vois l'espace récupérable avant de confirmer.
6. Si un bandeau orange **« Redémarrage nécessaire »** apparaît en haut, redémarre le PC quand tu peux.

## 3. Ce que fait Unpark

| Page | Ce qu'elle fait | Annulable ? |
| --- | --- | --- |
| **Plan d'alimentation** | Crée le plan « Unpark Performance » : tous les cœurs du processeur actifs et à 100 %, boost agressif, pas de mise en veille sur secteur. Sur portable, réglages agressifs sur secteur seulement. | ✅ « Revenir à mon ancien plan » ou « Supprimer le plan Unpark » |
| **Gaming** | Mode Jeu, planification GPU (HAGS, redémarrage requis), captures Game DVR coupées, accélération de la souris désactivée, effets visuels sur « performances » (optionnel). | ✅ bouton « Annuler » sur chaque réglage |
| **Démarrage** | Active / désactive les programmes lancés avec Windows, comme le Gestionnaire des tâches (rien n'est supprimé). | ✅ réactiver remet l'état d'origine |
| **Nettoyage** | Fichiers temporaires, cache de Windows Update, miniatures, corbeille (décochée par défaut). | ❌ fichiers supprimés pour de bon |
| **Debloat** | Retire des applis préinstallées inutiles (liste sûre cochée par défaut). Le Store, la Calculatrice, Photos, le Terminal et les applis Xbox ne sont jamais proposés. | ⚠️ réinstallation depuis le Microsoft Store (bouton dans la page) |
| **Infos système** | Processeur, carte graphique, mémoire, disques, version de Windows… Bouton **« Copier le résumé »** à coller sur Discord si tu as besoin d'aide. | — |

Ce qu'Unpark ne fait **jamais** : désactiver Windows Defender, le pare-feu ou Windows Update, « nettoyer la RAM », ou toucher à des réglages non documentés impossibles à annuler.

## 4. Tout annuler

Trois niveaux, du plus simple au plus radical :

1. **Un seul réglage** : bouton **Annuler** à côté du réglage (ou « Supprimer le plan Unpark »).
2. **Tout d'un coup** : **Accueil → Tout annuler** (ou **Gaming en 1 clic → Tout annuler**). Unpark remet chaque réglage **exactement** comme il était avant, pas une valeur « par défaut ».
3. **Si Unpark ne s'ouvre plus** : utilise le point de restauration de Windows.
   Démarrer → tape **« Créer un point de restauration »** → bouton **Restauration du système** → choisis **« Avant modifications Unpark »**.

> Le nettoyage et les applis retirées (Debloat) ne reviennent pas avec « Tout annuler ». Les applis se réinstallent depuis le Microsoft Store (page Debloat → « Réinstaller (Store) »).

## 5. En cas de souci

- **Journal** : chaque action est notée dans `%AppData%\Unpark\logs\` (Accueil → « Ouvrir les journaux »). Envoie le fichier du jour sur Discord.
- **Sauvegarde** : les réglages d'origine sont dans `%AppData%\Unpark\backup.json`. **Ne supprime pas ce fichier** tant que des réglages Unpark sont actifs, sinon « Tout annuler » ne pourra plus les remettre.
- **Infos système → Copier le résumé** pour décrire ton PC en une seconde.

---

## Pour le développeur (compilation sur le serveur Linux)

```bash
./build.sh            # tests + publication → releases/Unpark-vX.Y.Z.exe (SHA-256 affiché)
./build.sh --force    # remplace l'exe de la même version
```

- La version se change dans `src/Directory.Build.props` (`<Version>`) ; elle s'affiche dans l'appli.
- Il faut le **SDK .NET 8 officiel de Microsoft** dans `~/.dotnet` : celui d'Ubuntu (`apt install dotnet-sdk-8.0`) ne contient pas WPF.
- Exe autonome (self-contained, single-file, win-x64, ~70 Mo), manifeste `requireAdministrator`.

```
src/Unpark/         interface WPF (MVVM CommunityToolkit.Mvvm, thème sombre)
src/Unpark.Core/    logique : réglages réversibles (IsApplied/Apply/Revert), sauvegarde, journal,
                    point de restauration, plan d'alimentation, Gaming, Démarrage, Nettoyage, Debloat, infos
src/Unpark.Tests/   tests automatiques (tournent sous Linux, Windows simulé)
releases/           exe publiés (pas dans git)
```
