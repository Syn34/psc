#!/usr/bin/env bash
# Builds Poke Sprite Creator with Mono's C# compiler (Linux/macOS). On Windows use build.cmd or open PokeSpriteCreator.sln.
set -e
cd "$(dirname "$0")"
OUT=bin
mkdir -p "$OUT"
# Compile against the real .NET Framework 4.8 reference assemblies (not Mono's own library,
# which has extra methods that don't exist on Windows and would crash at runtime).
API=/usr/lib/mono/4.8-api
MCS="mcs -langversion:5 -nowarn:1591,0168,0219,0649 -debug- -nostdlib -r:$API/mscorlib.dll -r:$API/System.dll -lib:$API"
echo "== Poke Sprite Creator"
$MCS -target:winexe -out:$OUT/PokeSpriteCreator.exe -recurse:'src/PokeSpriteCreator/*.cs' -r:$API/System.Core.dll -r:$API/System.Drawing.dll -r:$API/System.Windows.Forms.dll
echo "Build OK -> $OUT/PokeSpriteCreator.exe"
