#!/bin/bash
set -e

runtimes=("win-x64" "linux-x64" "osx-arm64")

for rid in "${runtimes[@]}"; do
    echo "Building single binary for $rid..."
    dotnet publish \
      -c Release \
      -r "$rid" \
      --self-contained true \
      -p:PublishSingleFile=true \
      -p:EnableCompressionInSingleFile=true \
      -p:DebugType=None \
      -p:DebugSymbols=false \
      -o "./publish/$rid"
done

echo "Build complete."
