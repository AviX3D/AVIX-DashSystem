#!/bin/bash
# Compilation sous Linux/macOS : il faut les DLL de SimHub (SIMHUB_DIR) et les
# assemblies de référence .NET Framework 4.8 (NET48_REFS, dossier "v4.8" + "Facades").
set -e
cd "$(dirname "$0")"
: "${SIMHUB_DIR:?Définis SIMHUB_DIR (dossier contenant SimHub.Plugins.dll)}"
: "${NET48_REFS:?Définis NET48_REFS (Reference Assemblies .NETFramework v4.8)}"
for p in Plugin Installer; do
  dotnet build "src/$p" -c Release -p:SimHubDir="$SIMHUB_DIR" -p:FrameworkPathOverride="$NET48_REFS" \
    -p:AutomaticallyUseReferenceAssemblyPackages=false
done
cp build/AvixLmuDashSwitcher.dll Plugin/
cp build/AvixInstaller.exe .
