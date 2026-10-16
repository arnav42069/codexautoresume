#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:?Pass linux-x64, linux-arm64, osx-x64, or osx-arm64}"
case "$rid" in linux-x64|linux-arm64|osx-x64|osx-arm64) ;; *) echo 'Unsupported desktop runtime' >&2; exit 2;; esac
dotnet publish src/CodexAutoResume.Desktop -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "artifacts/$rid"
dotnet publish tools/CodexAutoResume.Probe -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "artifacts/probe-$rid"
python3 scripts/package-desktop.py "$rid"
