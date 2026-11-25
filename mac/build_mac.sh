#!/bin/bash
# Compila o VoxIA.app (Apple Silicon e Intel) e gera build/VoxIA-Mac.zip.
# Usado pelo GitHub Actions (.github/workflows/build-mac.yml); também roda num Mac com as
# ferramentas de linha de comando do Xcode (xcode-select --install).
set -euo pipefail
cd "$(dirname "$0")"

BUILD="${VOXIA_BUILD:-0}"          # 0 = compilado à mão (sem busca automática de atualizações)
VERSION="${VOXIA_VERSION:-$(cat VERSION)}"
OUT=build
rm -rf "$OUT"
mkdir -p "$OUT"

if ! command -v swiftc >/dev/null; then
    echo "[ERRO] swiftc não encontrado. Instale com: xcode-select --install"
    exit 1
fi

SOURCES=(src/*.swift)
for ARCH in arm64 x86_64; do
    echo "Compilando para $ARCH..."
    swiftc -O -swift-version 5 -parse-as-library -target "$ARCH-apple-macos12.0" "${SOURCES[@]}" -o "$OUT/VoxIA-$ARCH"
done

APP="$OUT/VoxIA.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
lipo -create "$OUT/VoxIA-arm64" "$OUT/VoxIA-x86_64" -output "$APP/Contents/MacOS/VoxIA"
sed -e "s/__BUILD__/$BUILD/g" -e "s/__VERSION__/$VERSION/g" Info.plist > "$APP/Contents/Info.plist"

# Assinatura local (ad-hoc): exigida para o app rodar em Macs com Apple Silicon
codesign --force --deep --sign - "$APP"

(cd "$OUT" && ditto -c -k --keepParent VoxIA.app VoxIA-Mac.zip)
echo "Pronto: $OUT/VoxIA.app e $OUT/VoxIA-Mac.zip (versão $VERSION, build $BUILD)"
