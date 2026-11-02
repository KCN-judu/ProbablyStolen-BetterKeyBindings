#!/usr/bin/env bash
# Builds the mod with the C# 5 compiler that ships with Windows, against the game's own Mono assemblies.
#   GAME=/d/SteamLibrary/steamapps/common/"Probably Stolen" CSC=/path/to/csc.exe ./build.sh [--install]
set -euo pipefail
cd "$(dirname "$0")"
GAME="${GAME:-/d/SteamLibrary/steamapps/common/Probably Stolen}"
M="$GAME/Probably Stolen_Data/Managed"
CSC="${CSC:-/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"
mkdir -p out/better_key_bindings

refs=()
for r in mscorlib netstandard System System.Core Assembly-CSharp Assembly-CSharp-firstpass 0Harmony \
         UnityEngine UnityEngine.CoreModule UnityEngine.InputLegacyModule UnityEngine.UI UnityEngine.UIModule UnityEngine.TextRenderingModule \
         Unity.TextMeshPro; do
  [ -f "$M/$r.dll" ] && refs+=("-r:$M/$r.dll")
done

# The .NET Framework csc cannot create its temp files under long paths, so compile in a short folder.
# It also reads "/" as an option prefix, so sources and output use backslashes.
WORK="${TEMP:-/tmp}/bkb_build"
rm -rf "$WORK" && mkdir -p "$WORK/src"
cp src/*.cs "$WORK/src/"
srcs=()
for f in src/*.cs; do srcs+=("src\\$(basename "$f")"); done
( cd "$WORK" && "$CSC" -nologo -nostdlib -noconfig -target:library -optimize+ -warn:4 -nowarn:1701,1702 \
  "${refs[@]}" '-out:BetterKeyBindings.dll' "${srcs[@]}" )
cp "$WORK/BetterKeyBindings.dll" out/better_key_bindings/

cp manifest.xml out/better_key_bindings/
cp preview.png out/better_key_bindings/
mkdir -p out/better_key_bindings/Localization
cp Localization/*.csv out/better_key_bindings/Localization/
echo "built out/better_key_bindings"

if [ "${1:-}" = "--install" ]; then
  rm -rf "$GAME/Mods/better_key_bindings"
  cp -r out/better_key_bindings "$GAME/Mods/"
  echo "installed to $GAME/Mods/better_key_bindings"
fi
