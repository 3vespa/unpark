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
| **Diagnostic** | Lecture seule : RAM (XMP/EXPO, double canal), fréquence de l'écran, pilote graphique, erreurs, disques, réseau… avec un bouton vers la correction. **« Copier le rapport »** pour Discord. | — |
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

> Le mode dynamique a été retiré en v0.9.1. Si tu l'avais activé avec une ancienne version, **Tout annuler** supprime encore sa tâche de démarrage.

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
- `prepare-release.sh` ne publie rien : il affiche les commandes `gh release create` et `wrangler deploy` à lancer soi-même.
