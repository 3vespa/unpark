#!/usr/bin/env bash
# Signe un exe pour la mise à jour intégrée : écrit <exe>.sig à côté (à publier avec la release).
# Utilisation : scripts/sign-update.sh releases/Unpark-vX.Y.Z.exe X.Y.Z
# Appelé par scripts/prepare-release.sh. La clé privée n'est jamais copiée ni affichée.
set -euo pipefail
cd "$(dirname "$0")/.."

EXE="$1"
VERSION="$2"
KEY="${UNPARK_SIGNING_KEY:-$HOME/.config/unpark/update-signing-key.pem}"

[ -f "$EXE" ] || { echo "✗ $EXE introuvable."; exit 1; }
[ -f "$KEY" ] || { echo "✗ Clé de signature introuvable : $KEY (voir README, « Clé de signature »)."; exit 1; }
case "$(stat -c %a "$KEY")" in
  600|400) ;;
  *) echo "✗ $KEY est lisible par d'autres comptes : chmod 600 \"$KEY\""; exit 1 ;;
esac

NAME="$(basename "$EXE")"
[ "$NAME" = "Unpark-v$VERSION.exe" ] || { echo "✗ Le fichier doit s'appeler Unpark-v$VERSION.exe (reçu : $NAME)."; exit 1; }

# La clé privée doit correspondre à la clé publique intégrée dans l'appli, sinon aucune appli installée n'accepterait la mise à jour.
PUB="$(openssl pkey -in "$KEY" -pubout -outform DER | tail -c 32 | base64 -w0)"
EMBEDDED="$(sed -n 's/.*PublicKeyBase64 = "\(.*\)";.*/\1/p' src/Unpark.Core/Updates/UpdateKeys.cs)"
if [ "$PUB" != "$EMBEDDED" ] && [ -z "${UNPARK_ALLOW_OTHER_KEY:-}" ]; then
  echo "✗ Cette clé privée ne correspond pas à la clé publique de src/Unpark.Core/Updates/UpdateKeys.cs."
  exit 1
fi

SIZE="$(stat -c %s "$EXE")"
SHA="$(sha256sum "$EXE" | cut -d' ' -f1)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Texte signé : exactement UpdateManifest.SignedText (src/Unpark.Core/Updates/UpdateManifest.cs).
printf 'Unpark update 1\nversion: %s\nfile: %s\nsize: %s\nsha256: %s\n' "$VERSION" "$NAME" "$SIZE" "$SHA" > "$TMP/body"
openssl pkeyutl -sign -inkey "$KEY" -rawin -in "$TMP/body" -out "$TMP/sig"

# Contrôle immédiat avec la clé publique.
openssl pkey -in "$KEY" -pubout -out "$TMP/pub.pem"
openssl pkeyutl -verify -pubin -inkey "$TMP/pub.pem" -rawin -in "$TMP/body" -sigfile "$TMP/sig" > /dev/null

{ cat "$TMP/body"; printf 'signature: %s\n' "$(base64 -w0 "$TMP/sig")"; } > "$EXE.sig"
echo "✓ $EXE.sig (Ed25519, SHA-256 $SHA)"
