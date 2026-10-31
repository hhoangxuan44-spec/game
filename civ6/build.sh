#!/usr/bin/env bash
# Civ6 (com.aspyr.civvi) base.apk + mod folder  ->  out/civ6-mod.apk
# The mod is bundled in assets/mods/ and copied to the game's own Mods folder at every start
# (Civ6Application.onCreate -> ModInstaller.install), so no file manager / Android/data access is needed.
#
# usage: civ6/build.sh <base.apk> <mod-folder> [apktool.jar]
#   needs: JDK (java, keytool, jarsigner), apktool 2.9+ (jar), python3
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
APK="$1"; MOD="$2"; APKTOOL="${3:-${APKTOOL:-apktool.jar}}"
WORK="$HERE/work"; OUT="$HERE/out"; KEYSTORE="$HERE/debug.keystore"
rm -rf "$WORK"; mkdir -p "$WORK" "$OUT"

java -jar "$APKTOOL" d -f -o "$WORK/ap" "$APK" >/dev/null

# 1. bundle the mod (ASCII folder name; the game does not care about it)
mkdir -p "$WORK/ap/assets/mods"
cp -r "$MOD" "$WORK/ap/assets/mods/AdvancedCheatMenu"
# add the "spawn any unit" popup (patch_mod.py edits the copy, never the original folder)
python3 "$HERE/patch_mod.py" "$WORK/ap/assets/mods/AdvancedCheatMenu"
# the mod's file list says UI/POPUPS/, the real folder is UI/Popups/ -- matters on a case-sensitive filesystem
sed -i 's|UI/POPUPS/|UI/Popups/|g' "$WORK/ap/assets/mods/AdvancedCheatMenu/"*.modinfo

# 2. installer class + one call at the top of Civ6Application.onCreate
python3 - "$HERE/ModInstaller.smali" "$HERE/options.conf" "$WORK/ap/smali/com/aspyr/civvi/ModInstaller.smali" <<'PY'
import sys
src, conf, dst = sys.argv[1:4]
smali = open(src, encoding="utf-8").read()
body = ""
for line in open(conf, encoding="utf-8"):
    line = line.strip()
    if not line or line.startswith("#"):
        continue
    key, value = line.split(None, 1)
    assert key.isalnum() and all(c.isalnum() or c == "." for c in value), line
    # regex "(?m)^Key [^\\r\\n]*"  ->  "Key value"   (replaceAll)
    body += ('    const-string v4, "(?m)^%s [^\\\\r\\\\n]*"\n\n    const-string v5, "%s %s"\n\n'
             '    invoke-virtual {v3, v4, v5}, Ljava/lang/String;->replaceAll(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;\n\n'
             '    move-result-object v3\n\n') % (key, key, value)
assert "# @OPTION_REPLACEMENTS@" in smali
open(dst, "w", encoding="utf-8").write(smali.replace("# @OPTION_REPLACEMENTS@\n", body))
PY
python3 - "$WORK/ap/smali/com/aspyr/civvi/Civ6Application.smali" <<'PY'
import sys
p = sys.argv[1]
s = open(p, encoding="utf-8").read()
if "ModInstaller" in s:
    sys.exit("already patched")
marker = "    sput-object p0, Lcom/aspyr/civvi/Civ6Application;->d:Lcom/aspyr/civvi/Civ6Application;\n"
assert marker in s, "Civ6Application.onCreate layout changed"
s = s.replace(marker, marker + "\n    invoke-static {p0}, Lcom/aspyr/civvi/ModInstaller;->install(Landroid/content/Context;)V\n", 1)
open(p, "w", encoding="utf-8").write(s)
PY

# 2b. full screen on tall / notched phones: the original theme forbids drawing into the display cutout
#     (windowLayoutInDisplayCutoutMode=never -> black bars) and declares no maximum aspect ratio
sed -i 's|name="android:windowLayoutInDisplayCutoutMode">never<|name="android:windowLayoutInDisplayCutoutMode">shortEdges<|' "$WORK/ap/res/values/styles.xml"
grep -q 'shortEdges' "$WORK/ap/res/values/styles.xml" || { echo "cutout theme patch failed"; exit 1; }
python3 - "$WORK/ap/AndroidManifest.xml" <<'PY'
import sys
p = sys.argv[1]
s = open(p, encoding="utf-8").read()
if "android.max_aspect" not in s:
    i = s.index("<application ")
    j = s.index(">", i)
    s = s[:j] + ' android:resizeableActivity="true"' + s[j:]
    k = s.index(">", s.index("<application ")) + 1
    s = s[:k] + '\n        <meta-data android:name="android.max_aspect" android:value="3.5"/>' + s[k:]
    open(p, "w", encoding="utf-8").write(s)
PY

# 3. rebuild + sign
java -jar "$APKTOOL" b -o "$WORK/unsigned.apk" "$WORK/ap" >/dev/null
if [ ! -f "$KEYSTORE" ]; then
  keytool -genkeypair -keystore "$KEYSTORE" -storepass civ6mod -keypass civ6mod -alias civ6 -keyalg RSA -keysize 2048 \
    -validity 10000 -dname "CN=Civ6 Mod" >/dev/null 2>&1
fi
cp "$WORK/unsigned.apk" "$OUT/civ6-mod.apk"
jarsigner -keystore "$KEYSTORE" -storepass civ6mod -keypass civ6mod -sigalg SHA256withRSA -digestalg SHA-256 "$OUT/civ6-mod.apk" civ6 >/dev/null
echo "built $OUT/civ6-mod.apk"
