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

Unpark ne garde que des réglages à l'effet reconnu, et dit honnêtement ce que chacun apporte. Tout est optionnel.

| Réglage | Ce qu'il apporte vraiment | Annulable ? |
| --- | --- | --- |
| **Plan d'alimentation Unpark Performance** | Gain réel mais modeste : surtout de meilleurs 1 % low dans les jeux limités par le processeur ; souvent nul si la carte graphique limite. Sur portable, seulement sur secteur. | ✅ « Revenir à mon ancien plan » ou « Supprimer le plan Unpark » |
| **Mode Jeu** | Priorité au jeu, pas de mises à jour installées pendant la partie. Effet faible mais réel. | ✅ |
| **Enregistrement en arrière-plan (Game Bar)** | Quelques % de FPS récupérés s'il était actif, rien sinon. | ✅ |
| **Optimisations pour les jeux en fenêtre** (Windows 11) | Latence plus basse en fenêtre sans bordure, Auto HDR et VRR en fenêtré. | ✅ |
| **HAGS** | Nécessaire pour DLSS Frame Generation (RTX 40/50, recommandé seulement là). Ailleurs, effet quasi nul et peut réserver de la mémoire vidéo sur les cartes de 6-8 Go. Redémarrage requis. | ✅ |
| **Accélération de la souris désactivée** | Aucun gain de FPS : rend la visée constante. | ✅ |
| **Intégrité de la mémoire désactivée** | 0 à ~10 % selon le processeur et le jeu, **mais moins de sécurité**. Jamais dans le profil 1 clic, avertissement avant. Redémarrage requis. | ✅ |
| **Démarrage** | Windows démarre plus vite ; en jeu, utile seulement si ces programmes consomment en fond. | ✅ réactiver remet l'état d'origine |
| **Nettoyage** | Espace disque uniquement, aucun effet sur les FPS (corbeille décochée par défaut). | ❌ fichiers supprimés pour de bon |
| **Optimisation des lecteurs** (page Nettoyage) | Entretien du disque : temps de chargement et longévité, aucun effet direct sur les FPS. TRIM pour un SSD, défragmentation pour un disque dur, jamais de défragmentation sur un SSD. | — entretien normal de Windows ; l'activation de l'optimisation planifiée, elle, est annulable ✅ |
| **Debloat** | Rangement : quasi aucun effet sur les FPS. Le Store, la Calculatrice, Photos, le Terminal et les applis Xbox ne sont jamais proposés. | ⚠️ réinstallation depuis le Microsoft Store (bouton dans la page) |
| **Diagnostic** | Lecture seule : RAM (XMP/EXPO, double canal), fréquence de l'écran, pilote graphique, erreurs, disques, réseau… avec des corrections directement sur la ligne (voir ci-dessous). **« Copier le rapport »** pour Discord. | ✅ la fréquence de l'écran (« Tout annuler ») |
| **Infos système** | Processeur, carte graphique, mémoire, disques… Bouton **« Copier le résumé »**. | — |

Ce qu'Unpark ne fait **jamais**, parce que c'est inutile ou risqué : désactiver Windows Defender, le pare-feu ou Windows Update, « nettoyer la RAM », réglages réseau du registre « anti-ping », désactivation de services au hasard, commandes bcdedit (HPET, dynamic tick…), résolution de la minuterie, effets visuels « performances » (aucun effet en jeu).

### Optimisation des lecteurs (page Nettoyage)

C'est l'outil Windows « Optimiser les lecteurs », dans Unpark :

1. La liste montre chaque lecteur : type (SSD ou disque dur), date de dernière optimisation et état (« OK », « 12 % fragmenté »…).
2. **Analyser** (disques durs seulement, comme dans Windows) mesure la fragmentation sans rien modifier.
3. **Optimiser** ou **Tout optimiser** fait automatiquement le bon traitement : **TRIM** pour un SSD, **défragmentation** pour un disque dur. Un SSD n'est **jamais** défragmenté. Un lecteur de type inconnu (clé USB, disque virtuel) n'est pas touché.
4. Une barre de progression s'affiche dans la ligne du lecteur ; tu peux continuer à utiliser le PC, ou cliquer sur **Arrêter** (sans risque pour tes fichiers).
5. En bas : l'**optimisation planifiée** de Windows (activée et sa fréquence). Si elle est coupée, **Activer** la relance ; c'est annulable.

Entretien du disque : temps de chargement et longévité, aucun effet direct sur les FPS.

### Corrections en un clic dans le Diagnostic

Sur la ligne du problème, sans changer de page :

- **Fréquence de l'écran** : si un écran tourne en dessous de sa fréquence maximale (à sa résolution actuelle), un bouton **« Passer à X Hz »**. Comme dans Windows, une fenêtre **« Garder cette fréquence ? »** s'affiche avec un compte à rebours de **15 secondes** : sans réponse (par exemple si l'écran devient noir), l'ancienne fréquence revient toute seule. Entrée ou Échap reviennent aussi en arrière : il faut cliquer sur **Garder**. Chaque écran a son bouton. **Tout annuler** remet la fréquence d'avant Unpark.
- **XMP / EXPO désactivé** : impossible à changer depuis Windows. Le bouton **« Comment l'activer ? »** déplie un mini-guide selon ta carte mère (ASUS, MSI, Gigabyte, ASRock…) : touche pour entrer dans le BIOS, où trouver le réglage et son nom (XMP, EXPO, D.O.C.P., A-XMP). En cas d'instabilité, désactive-le. Sur un PC de marque (Dell, HP, Lenovo…) ou un portable, le guide indique que le réglage n'est souvent pas disponible.
- **Pilote graphique ancien** : **« Mettre à jour »** ouvre la page officielle de NVIDIA, AMD ou Intel selon ta carte. Unpark ne télécharge et n'installe jamais de pilote.

> Le mode dynamique a été retiré en v0.9.1. Si tu l'avais activé avec une ancienne version, **Tout annuler** supprime encore sa tâche de démarrage.

## 4. Mises à jour

À partir de la **v0.10.0**, Unpark se met à jour tout seul, comme Steam ou Discord :

1. Au démarrage, en arrière-plan (sans ralentir l'ouverture), Unpark regarde s'il existe une nouvelle version sur GitHub (`3vespa/unpark`). Sans Internet, rien ne s'affiche.
2. Si oui, il la télécharge pendant que tu continues : un bandeau vert en haut affiche « Mise à jour vX.Y.Z en cours de téléchargement… » avec la progression.
3. Une fois prête : **« Redémarrer maintenant »** ferme Unpark et le relance sur la nouvelle version en quelques secondes. Si tu ne cliques pas, elle s'installe toute seule à la **prochaine ouverture**, avant l'affichage de la fenêtre. Unpark ne redémarre jamais pendant une opération en cours (optimisation de disque, application de réglages…).
4. Le fichier `.exe` garde **son nom et son emplacement** (s'il s'appelle `Unpark-v0.10.0.exe`, il garde ce nom même une fois à jour : la vraie version s'affiche en bas du menu). Tes réglages et la sauvegarde « Tout annuler » sont conservés.

**Sécurité** : chaque mise à jour est signée (Ed25519) et Unpark vérifie la signature et l'empreinte SHA-256 avant de l'installer, uniquement en HTTPS et uniquement depuis les releases de `3vespa/unpark`. Une mise à jour qui ne correspond pas est refusée avec un message clair, et ta version actuelle reste intacte. Si le remplacement échoue à n'importe quelle étape, l'ancienne version est remise en place.

**Désactiver** : **Accueil → Mises à jour → décocher « Mettre à jour Unpark automatiquement »**. Unpark ne se connecte alors plus du tout à GitHub ; tu télécharges les nouvelles versions toi-même.

> Depuis la v0.9.1 ou avant, télécharge la v0.10.0 une dernière fois à la main : les anciennes versions ne savent pas se mettre à jour.

## 5. Tout annuler

Trois niveaux, du plus simple au plus radical :

1. **Un seul réglage** : bouton **Annuler** à côté du réglage (ou « Supprimer le plan Unpark »).
2. **Tout d'un coup** : **Accueil → Tout annuler** (ou **Gaming en 1 clic → Tout annuler**). Unpark remet chaque réglage **exactement** comme il était avant, pas une valeur « par défaut ».
3. **Si Unpark ne s'ouvre plus** : utilise le point de restauration de Windows.
   Démarrer → tape **« Créer un point de restauration »** → bouton **Restauration du système** → choisis **« Avant modifications Unpark »**.

> Le nettoyage et les applis retirées (Debloat) ne reviennent pas avec « Tout annuler ». Les applis se réinstallent depuis le Microsoft Store (page Debloat → « Réinstaller (Store) »).

## 6. En cas de souci

- **Journal** : chaque action est notée dans `%AppData%\Unpark\logs\` (Accueil → « Ouvrir les journaux »). Envoie le fichier du jour sur Discord.
- **Mise à jour** : l'ancienne version est gardée à côté de l'exe (`….exe.old`) jusqu'au lancement suivant. Si une mise à jour a mal tourné et qu'Unpark ne s'ouvre plus, supprime le nouvel exe et retire `.old` du nom de l'ancien (ex. `Unpark.exe.old` → `Unpark.exe`).
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
                    point de restauration, plan d'alimentation, Gaming, Démarrage, Nettoyage, disques, Debloat, infos
src/Unpark.Tests/   tests automatiques (tournent sous Linux, Windows simulé)
releases/           exe publiés (pas dans git)
```

### Logo, site et publication

```bash
python3 scripts/make-icons.py      # régénère les icônes (exe, favicon) à partir du logo
./scripts/prepare-release.sh       # met le site à jour (version, taille, date, SHA-256) et écrit releases/notes-vX.Y.Z.md
```

- `site/` : site vitrine statique (HTML + CSS, aucune dépendance, police Space Grotesk hébergée sur place, licence OFL dans `site/fonts/`). Publié comme **Worker Cloudflare à fichiers statiques** : `wrangler.jsonc` à la racine (`npx wrangler deploy`), en-têtes dans `site/_headers`, page `site/404.html` pour les adresses inconnues.
- L'exe (~70 Mo) va sur **GitHub Releases** : Cloudflare refuse les fichiers statiques de plus de 25 Mo.
- `scripts/release.conf` : le dépôt GitHub des releases (`pseudo/depot`).
- `prepare-release.sh` ne publie rien : il signe l'exe, puis affiche les commandes `gh release create` (exe **et** `.sig`) et `wrangler deploy` à lancer soi-même.

### Clé de signature des mises à jour

La mise à jour intégrée n'installe qu'un exe signé avec la clé Ed25519 d'Unpark.

- **Clé privée** : `~/.config/unpark/update-signing-key.pem` sur le serveur de compilation (dossier en `700`, fichier en `600`). Elle est **hors du dépôt** et ne doit jamais être commitée, envoyée sur Discord ou copiée sur un cloud non chiffré. Elle a été créée par `scripts/make-signing-key.sh`, qui refuse d'en écraser une existante.
- **Clé publique** : dans `src/Unpark.Core/Updates/UpdateKeys.cs` (elle peut être publique).
- **Signature** : `scripts/prepare-release.sh` appelle `scripts/sign-update.sh`, qui écrit `releases/Unpark-vX.Y.Z.exe.sig` (version, nom, taille, SHA-256 de l'exe, et la signature de ces lignes). Le script vérifie que la clé privée correspond bien à la clé publique de l'appli.
- **La sauvegarder** (à faire tout de suite) : c'est un petit fichier texte de 3 lignes. Copie son contenu (`cat ~/.config/unpark/update-signing-key.pem`) dans une **note sécurisée d'un gestionnaire de mots de passe** (Bitwarden, KeePassXC…), et/ou une copie chiffrée sur une clé USB : `gpg -c ~/.config/unpark/update-signing-key.pem` (donne un `.pem.gpg`, protégé par une phrase de passe). Pour la remettre : recopier le fichier au même endroit puis `chmod 600`.
- **Si elle est perdue** : impossible de signer de nouvelles versions. Les applis déjà installées ne se mettront plus à jour toutes seules : il faudra créer une nouvelle clé, publier une version avec la nouvelle clé publique, et que chacun la télécharge une fois à la main.
- **Si elle a fuité** : même chose (nouvelle clé, nouvelle version à télécharger à la main). Pour piéger une mise à jour, il faudrait à la fois la clé ET un accès en écriture aux releases de `3vespa/unpark` : protège aussi ton compte GitHub (double authentification).

**Tester une mise à jour avant de publier** : compile une version plus récente, signe-la (`scripts/sign-update.sh releases/Unpark-vX.Y.Z.exe X.Y.Z`), copie l'exe et son `.sig` dans un dossier du PC Windows, puis lance l'ancienne version avec `--maj-depuis "C:\chemin\du\dossier"`. Mêmes vérifications qu'un téléchargement (signature, version plus récente, SHA-256), puis le bandeau « Redémarrer maintenant » s'affiche. Rien n'est téléchargé dans ce mode.
