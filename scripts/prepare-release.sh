#!/usr/bin/env bash
# Prépare une version à publier, SANS RIEN PUBLIER :
#   1. vérifie que releases/Unpark-vX.Y.Z.exe existe (sinon : ./build.sh) ;
#   2. met à jour le site (version, taille, date, SHA-256, liens GitHub) ;
#   3. écrit les notes de version dans releases/notes-vX.Y.Z.md ;
#   4. affiche les commandes à lancer toi-même pour publier.
set -euo pipefail
cd "$(dirname "$0")/.."

source scripts/release.conf
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Directory.Build.props | head -1)"
EXE="releases/Unpark-v$VERSION.exe"
NOTES="releases/notes-v$VERSION.md"
[ -f "$EXE" ] || { echo "✗ $EXE introuvable : lance d'abord ./build.sh"; exit 1; }

SHA="$(sha256sum "$EXE" | cut -d' ' -f1)"
BYTES="$(stat -c %s "$EXE")"
python3 -I - "$VERSION" "$EXE" "$SHA" "$BYTES" "$GITHUB_REPO" <<'PY'
import re, sys, os, datetime
version, exe, sha, size, repo = sys.argv[1:6]
months = ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"]
d = datetime.date.fromtimestamp(os.path.getmtime(exe))
date = f"{d.day} {months[d.month - 1]} {d.year}"
mb = int(size) / 1024 / 1024
size_text = f"{mb:.0f} Mo"
url = f"https://github.com/{repo}/releases/download/v{version}/Unpark-v{version}.exe"
latest = f"https://github.com/{repo}/releases/latest"
p = "site/index.html"
html = open(p, encoding="utf-8").read()
for key, value in {"version": version, "size": size_text, "date": date, "sha256": sha}.items():
    html = re.sub(rf'(data-r="{key}">)[^<]*(<)', rf"\g<1>{value}\g<2>", html)
html = re.sub(r'href="[^"]*"(\s+data-r-href="url")', rf'href="{url}"\1', html)
html = re.sub(r'href="[^"]*"(\s+data-r-href="latest")', rf'href="{latest}"\1', html)
open(p, "w", encoding="utf-8").write(html)
print(f"✓ site/index.html : v{version}, {size_text}, {date}")
PY

PREV_TAG="$(git describe --tags --abbrev=0 2>/dev/null || true)"
CHANGES="$(git log --pretty='- %s' ${PREV_TAG:+$PREV_TAG..}HEAD | grep -v '^- Merge' | head -40)"
cat > "$NOTES" <<NOTES_EOF
## Unpark v$VERSION

Optimisation PC gaming pour Windows 10 (22H2) et Windows 11. Un seul fichier, rien à installer.

### Nouveautés
$CHANGES

### Installation
1. Télécharge \`Unpark-v$VERSION.exe\` ci-dessous.
2. Windows SmartScreen bloque ? L'appli n'est pas signée : clique sur **« Informations complémentaires »** puis **« Exécuter quand même »**.
3. Accepte la demande d'autorisation administrateur.

À la première modification, Unpark propose de créer un point de restauration : dis oui. Tout s'annule avec **Accueil → Tout annuler**.

### Vérification
SHA-256 : \`$SHA\`

PowerShell : \`Get-FileHash .\\Unpark-v$VERSION.exe\`
NOTES_EOF
echo "✓ $NOTES"

echo
if [ "$GITHUB_REPO" = "TON-PSEUDO/unpark" ]; then
  echo "⚠ scripts/release.conf contient encore TON-PSEUDO : remplace-le par ton dépôt, puis relance ce script."
fi
cat <<CMDS_EOF
Rien n'a été publié. Pour publier, lance toi-même :

  # 1. Publier l'exe sur GitHub Releases (crée le tag v$VERSION)
  gh release create v$VERSION "$EXE" --repo "$GITHUB_REPO" --title "Unpark v$VERSION" --notes-file "$NOTES"

  # 2. Mettre le site en ligne (si tu n'as pas relié Cloudflare Pages au dépôt GitHub)
  npx wrangler pages deploy site --project-name unpark
CMDS_EOF
