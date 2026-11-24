#!/usr/bin/env bash
# Compile Unpark, lance les tests, publie un .exe Windows autonome et le copie dans releases/.
# Utilisation : ./build.sh            (refuse d'écraser une version déjà publiée)
#               ./build.sh --force    (remplace releases/Unpark-vX.Y.Z.exe)
set -euo pipefail

cd "$(dirname "$0")"
ROOT="$(pwd)"

# SDK officiel Microsoft dans ~/.dotnet : celui d'Ubuntu (apt) ne sait pas compiler WPF.
if [ -x "$HOME/.dotnet/dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if ! dotnet --list-sdks | grep -q '^8\.'; then
  echo "✗ SDK .NET 8 introuvable."; exit 1
fi
if [ ! -d "$(dirname "$(command -v dotnet)")/sdk/$(dotnet --version)/Sdks/Microsoft.NET.Sdk.WindowsDesktop" ]; then
  echo "✗ Ce SDK .NET ne contient pas WPF (SDK d'Ubuntu ?). Installe le SDK officiel dans ~/.dotnet."; exit 1
fi

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Directory.Build.props | head -1)"
[ -n "$VERSION" ] || { echo "✗ Version introuvable dans src/Directory.Build.props."; exit 1; }
OUT="releases/Unpark-v$VERSION.exe"

if [ -e "$OUT" ] && [ "${1:-}" != "--force" ]; then
  echo "✗ $OUT existe déjà. Augmente <Version> dans src/Directory.Build.props, ou relance avec --force."
  exit 1
fi

echo "→ Tests…"
dotnet test src/Unpark.Tests/Unpark.Tests.csproj -c Release --nologo -v quiet

echo "→ Publication de Unpark v$VERSION (win-x64, autonome, un seul fichier)…"
PUBLISH="$ROOT/src/Unpark/bin/publish"
rm -rf "$PUBLISH"
dotnet publish src/Unpark/Unpark.csproj -c Release -o "$PUBLISH" --nologo -v quiet

mkdir -p releases
cp "$PUBLISH/Unpark.exe" "$OUT"

SIZE="$(du -h "$OUT" | cut -f1)"
SHA="$(sha256sum "$OUT" | cut -d' ' -f1)"
echo "✓ $OUT ($SIZE)"
echo "  SHA-256 : $SHA"
