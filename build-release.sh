#!/bin/bash
set -e

# Ensure we are in the project root
cd "$(dirname "$0")"

echo "Checking for required tools..."
if ! dotnet dotnet-gitversion > /dev/null 2>&1; then
    echo "Error: GitVersion.Tool is not working. Run 'dotnet tool restore' first."
    exit 1
fi

if ! dotnet vpk --help > /dev/null 2>&1; then
    echo "Error: vpk tool not found. Please install it with: dotnet tool install --local vpk"
    exit 1
fi

echo "Determining version with GitVersion..."
# Extract version info
VERSION_JSON=$(dotnet dotnet-gitversion)
SEMVER=$(echo "$VERSION_JSON" | grep -oP '"SemVer":\s*"\K[^"]+')
INFO_VERSION=$(echo "$VERSION_JSON" | grep -oP '"InformationalVersion":\s*"\K[^"]+')
ASSEMBLY_VERSION=$(echo "$VERSION_JSON" | grep -oP '"AssemblySemVer":\s*"\K[^"]+')

echo "Building version: $SEMVER"

# Clean previous builds
rm -rf publish Releases
mkdir -p publish/win-x64
mkdir -p publish/linux-x64
mkdir -p Releases

# Common publish flags
PUBLISH_FLAGS="-c Release -p:SelfContained=true -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false -p:InformationalVersion=$INFO_VERSION -p:Version=$SEMVER -p:AssemblyVersion=$ASSEMBLY_VERSION"

echo "Publishing for Windows (win-x64)..."
dotnet publish WorldBuilder.Windows/WorldBuilder.Windows.csproj -r win-x64 -o publish/win-x64 $PUBLISH_FLAGS

echo "Publishing for Linux (linux-x64)..."
dotnet publish WorldBuilder.Linux/WorldBuilder.Linux.csproj -r linux-x64 -o publish/linux-x64 $PUBLISH_FLAGS

echo "Packing Windows release..."
dotnet vpk [win] pack -u WorldBuilder -v "$SEMVER" -p publish/win-x64 -e WorldBuilder.Windows.exe --framework net10.0-x64-desktop --channel windows -o Releases

echo "Packing Linux release..."
dotnet vpk pack -u WorldBuilder -v "$SEMVER" -p publish/linux-x64 -e WorldBuilder.Linux --channel linux -o Releases

echo "Build and Pack complete. Artifacts are in Releases/"
