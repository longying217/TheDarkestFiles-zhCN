#!/bin/bash
# Build TdfZhCN.dll with Mono's mcs.
# Usage: GAME_MANAGED=<game>/The Darkest Files_Data/Managed BEPINEX_CORE=<game>/BepInEx/core ./build.sh
set -e
cd "$(dirname "$0")"
M="${GAME_MANAGED:?set GAME_MANAGED to the game's Managed folder}"
B="${BEPINEX_CORE:?set BEPINEX_CORE to BepInEx/core}"
mcs -target:library -out:TdfZhCN.dll -langversion:7.2 -nostdlib -noconfig -optimize+ \
 -r:"$M/mscorlib.dll" -r:"$M/netstandard.dll" -r:"$M/System.dll" -r:"$M/System.Core.dll" \
 -r:"$B/BepInEx.dll" -r:"$B/0Harmony.dll" \
 -r:"$M/UnityEngine.dll" -r:"$M/UnityEngine.CoreModule.dll" -r:"$M/UnityEngine.TextRenderingModule.dll" \
 -r:"$M/UnityEngine.TextCoreFontEngineModule.dll" -r:"$M/UnityEngine.TextCoreTextEngineModule.dll" \
 -r:"$M/UnityEngine.UI.dll" -r:"$M/UnityEngine.UIModule.dll" -r:"$M/Unity.TextMeshPro.dll" \
 -r:"$M/Unity.Localization.dll" -r:"$M/Unity.ResourceManager.dll" \
 -r:"$M/Assembly-CSharp-firstpass.dll" -r:"$M/Assembly-CSharp.dll" \
 TdfZhCN.cs
