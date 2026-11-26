#!/usr/bin/env bash
# Crée la clé Ed25519 qui signe les mises à jour d'Unpark. À lancer UNE SEULE FOIS.
#
# - Clé privée : ~/.config/unpark/update-signing-key.pem (droits 600), HORS du dépôt git. Ne jamais la commiter ni l'envoyer.
# - Clé publique : affichée en base64, à mettre dans src/Unpark.Core/Updates/UpdateKeys.cs (elle, peut être publique).
#
# Refuse d'écraser une clé existante : une nouvelle clé rendrait impossible la mise à jour automatique
# de toutes les versions déjà installées (elles ne connaissent que l'ancienne clé publique).
set -euo pipefail

KEY="${UNPARK_SIGNING_KEY:-$HOME/.config/unpark/update-signing-key.pem}"

if [ -e "$KEY" ]; then
  echo "✗ $KEY existe déjà : rien n'est fait (ne la remplace pas, sauf si elle a fuité)."
  exit 1
fi

umask 077
mkdir -p "$(dirname "$KEY")"
chmod 700 "$(dirname "$KEY")"
openssl genpkey -algorithm ed25519 -out "$KEY"
chmod 600 "$KEY"

# Les 32 derniers octets de la clé publique au format DER sont la clé publique Ed25519 « brute ».
PUB="$(openssl pkey -in "$KEY" -pubout -outform DER | tail -c 32 | base64 -w0)"
echo "✓ Clé privée : $KEY"
echo "  Clé publique (à mettre dans src/Unpark.Core/Updates/UpdateKeys.cs) : $PUB"
echo
echo "Sauvegarde la clé privée MAINTENANT dans un endroit sûr hors du serveur (voir README, « Clé de signature »)."
