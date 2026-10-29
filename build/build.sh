#!/usr/bin/env bash
# original/kapai.apk -> out/kapai-offline.apk
#   needs: dotnet SDK 8, python3, JDK (keytool + jarsigner)
# usage: build/build.sh                    offline server embedded in the game (nothing else to run)
#        build/build.sh --server [URL]     use the external Python server instead (default http://127.0.0.1:8083/api/)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
EMBED=1
URL="http://127.0.0.1:8083/api/"
if [ "${1:-}" = "--server" ]; then EMBED=0; URL="${2:-$URL}"; fi
WORK="$ROOT/build/work"
OUT="$ROOT/out"
DLL_ENTRY="assets/bin/Data/Managed/Assembly-CSharp.dll"
KEYSTORE="$ROOT/build/debug.keystore"

[ -f "$ROOT/original/kapai.apk" ] || { echo "missing original/kapai.apk"; exit 1; }
mkdir -p "$WORK" "$OUT"

# 1. build the patcher
dotnet build "$ROOT/patcher" -c Release -o "$WORK/patcher" -nologo -v q >/dev/null

# 2. extract the managed DLL (+ its siblings: Cecil needs UnityEngine.dll to resolve references)
rm -rf "$WORK/managed"; mkdir -p "$WORK/managed"
unzip -q -o -j "$ROOT/original/kapai.apk" "assets/bin/Data/Managed/*.dll" -d "$WORK/managed"

# 3. patch (and embed the offline server, compiled against the game's own class libraries)
EMBED_ARGS=()
if [ "$EMBED" = 1 ]; then
  python3 "$ROOT/embedded/gen_data.py" >/dev/null
  dotnet build "$ROOT/embedded/Monsu.Offline.csproj" -c Release -o "$WORK/embedded" -p:ManagedDir="$WORK/managed" -nologo -v q >/dev/null
  EMBED_ARGS=(--embed "$WORK/embedded/MonsuOffline.dll")
fi
DOTNET_ROLL_FORWARD=Major dotnet "$WORK/patcher/Patcher.dll" "$WORK/managed/Assembly-CSharp.dll" "$WORK/Assembly-CSharp.patched.dll" "$URL" "${EMBED_ARGS[@]}"

# 4. repack without the old signature
python3 "$ROOT/build/repack.py" "$ROOT/original/kapai.apk" "$WORK/unsigned.apk" "$DLL_ENTRY" "$WORK/Assembly-CSharp.patched.dll"

# 5. sign (key is generated on first run and never committed)
if [ ! -f "$KEYSTORE" ]; then
  keytool -genkeypair -keystore "$KEYSTORE" -storepass monsu123 -keypass monsu123 -alias monsu \
    -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=Monsu Offline" >/dev/null 2>&1
fi
cp "$WORK/unsigned.apk" "$OUT/kapai-offline.apk"
jarsigner -keystore "$KEYSTORE" -storepass monsu123 -keypass monsu123 -sigalg SHA256withRSA -digestalg SHA-256 \
  "$OUT/kapai-offline.apk" monsu >/dev/null
jarsigner -verify "$OUT/kapai-offline.apk" | head -1
if [ "$EMBED" = 1 ]; then echo "built $OUT/kapai-offline.apk (server embedded)"; else echo "built $OUT/kapai-offline.apk (server $URL)"; fi
