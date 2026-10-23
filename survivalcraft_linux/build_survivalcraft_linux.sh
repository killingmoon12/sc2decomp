#!/usr/bin/env bash
#
# build_survivalcraft_linux.sh
#
# Build Survivalcraft 2 for Linux from the decompiled APK source.
#
# Reproducible build:
#   * src/ is regenerated from the decompiled tree on every run
#   * every source change comes from patches/ or prewritten/ - never from
#     hand-edits to src/
#
# Requirements: dotnet SDK 9.0+, bash, rsync, patch
#
# Usage: ./build_survivalcraft_linux.sh [path/to/app.apk]
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$SCRIPT_DIR"
SRC_DIR="$ROOT_DIR/src"
PATCH_DIR="$ROOT_DIR/patches"
PREWRITTEN_DIR="$ROOT_DIR/prewritten"
BUILD_DIR="$ROOT_DIR/build"
DECOMP_DIR="${DECOMP_DIR:-$ROOT_DIR/../survivalcraft_decompiled/decompiled}"

step() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

# Check dependencies
for cmd in dotnet rsync patch; do
    command -v "$cmd" >/dev/null || { echo "Missing dependency: $cmd" >&2; exit 1; }
done

[[ -d "$DECOMP_DIR" ]] || { echo "Decompiled source not found: $DECOMP_DIR" >&2; exit 1; }

# Find the APK (only needed to validate the run; the source comes from DECOMP_DIR)
APK="${1:-}"
if [[ -z "$APK" ]]; then
    APK=$(find "$ROOT_DIR/.." -maxdepth 1 -name '*.apk' -print -quit)
fi
[[ -n "$APK" && -f "$APK" ]] || { echo "APK not found. Usage: $0 [path/to/app.apk]" >&2; exit 1; }

step "Using APK: $APK"

# ---------------------------------------------------------------------------
# Source tree from the pristine decompiled source
# ---------------------------------------------------------------------------
step "Setting up source tree"
mkdir -p "$SRC_DIR"
rsync -a --delete "$DECOMP_DIR/EntitySystem.Android.Net/" "$SRC_DIR/EntitySystem.Android.Net/"
rsync -a --delete "$DECOMP_DIR/Engine/" "$SRC_DIR/Engine/"
rsync -a --delete "$DECOMP_DIR/Survivalcraft/" "$SRC_DIR/Survivalcraft/"
rsync -a --delete "$DECOMP_DIR/OpenTK-1.0/" "$SRC_DIR/OpenTK-1.0/"

# Copy content pak
cp "$DECOMP_DIR/Survivalcraft/Game.Content.pak" "$SRC_DIR/Survivalcraft/"

# ---------------------------------------------------------------------------
# Linux platform layer (hand-written; not present in the original source)
# ---------------------------------------------------------------------------
step "Adding Linux platform layer"
mkdir -p "$SRC_DIR/OpenTK-1.0/OpenTK.Platform.Linux"
cp "$PREWRITTEN_DIR/OpenTK.Platform.Linux/"*.cs "$SRC_DIR/OpenTK-1.0/OpenTK.Platform.Linux/"

# ---------------------------------------------------------------------------
# Multiplayer networking library + UI (new files, not in the decompile)
# Placed under src/Survivalcraft/Multiplayer so the Survivalcraft project's
# **/*.cs glob picks them up (same assembly as the game code).
# ---------------------------------------------------------------------------
step "Adding multiplayer library"
mkdir -p "$SRC_DIR/Survivalcraft/Multiplayer"
cp "$PREWRITTEN_DIR/Multiplayer/"*.cs "$SRC_DIR/Survivalcraft/Multiplayer/"

# ---------------------------------------------------------------------------
# Patches
# ---------------------------------------------------------------------------
# Every patch here must apply cleanly to the freshly decompiled tree; a patch
# that does not stops the build rather than silently leaving src/ half-patched.
step "Applying patches"
if [[ -d "$PATCH_DIR" ]]; then
    for patch in "$PATCH_DIR"/*.patch; do
        [[ -f "$patch" ]] || continue
        base=$(basename "$patch")
        echo "  Applying $base"
        if ! patch -p1 -d "$SRC_DIR" < "$patch"; then
            echo "error: $base did not apply cleanly" >&2
            exit 1
        fi
    done
fi

# ---------------------------------------------------------------------------
# Pre-written sources (survive regeneration of src/)
# ---------------------------------------------------------------------------
step "Installing pre-written sources"
cp "$PREWRITTEN_DIR/Engine/Keyboard.cs"  "$SRC_DIR/Engine/Engine.Input/Keyboard.cs"
cp "$PREWRITTEN_DIR/Engine/Mouse.cs"     "$SRC_DIR/Engine/Engine.Input/Mouse.cs"
cp "$PREWRITTEN_DIR/Engine/Touch.cs"     "$SRC_DIR/Engine/Engine.Input/Touch.cs"
cp "$PREWRITTEN_DIR/Engine/GamePad.cs"   "$SRC_DIR/Engine/Engine.Input/GamePad.cs"
cp "$PREWRITTEN_DIR/Engine/Window.cs"    "$SRC_DIR/Engine/Engine/Window.cs"
cp "$PREWRITTEN_DIR/Engine/Storage.cs"   "$SRC_DIR/Engine/Engine/Storage.cs"
cp "$PREWRITTEN_DIR/Engine/Utilities.cs" "$SRC_DIR/Engine/Engine/Utilities.cs"
cp "$PREWRITTEN_DIR/OpenTK.Platform/Factory.cs"             "$SRC_DIR/OpenTK-1.0/OpenTK.Platform/Factory.cs"
cp "$PREWRITTEN_DIR/OpenTK.Input/Mouse.cs"                  "$SRC_DIR/OpenTK-1.0/OpenTK.Input/Mouse.cs"
cp "$PREWRITTEN_DIR/OpenTK.Input/IMouseCursorController.cs" "$SRC_DIR/OpenTK-1.0/OpenTK.Input/IMouseCursorController.cs"

# ---------------------------------------------------------------------------
# Project files (patches don't apply well due to path differences)
# ---------------------------------------------------------------------------
step "Fixing project files"

cat > "$SRC_DIR/OpenTK-1.0/OpenTK-1.0.csproj" << 'CSPROJ_EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>OpenTK-1.0</AssemblyName>
    <RootNamespace>OpenTK</RootNamespace>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>$(NoWarn);CS0108;CS0114;CS0169;CS0414;CS0649;CS0612;CS0618;CS1591;CS8981</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <!-- Android-only platform glue removed; Linux equivalents provided via prewritten/ -->
    <Compile Include="**/*.cs" Exclude="obj/**;bin/**;Properties/AssemblyInfo.cs;OpenTK.Android/**;OpenTK.Platform.Android/**;OpenTK/GameViewBase.cs;OpenTK.Platform.Egl/EglAndroidPlatformFactory.cs" />
  </ItemGroup>
</Project>
CSPROJ_EOF

cat > "$SRC_DIR/Engine/Engine.csproj" << 'CSPROJ_EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>Engine</AssemblyName>
    <RootNamespace>Engine</RootNamespace>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>$(NoWarn);CS0108;CS0114;CS0169;CS0414;CS0649;CS0612;CS0618;CS1591;CS8981</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenTK-1.0\OpenTK-1.0.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- Android-only platform glue replaced by Linux patches -->
    <Compile Include="**/*.cs"
             Exclude="obj/**;bin/**;Properties/AssemblyInfo.cs;Engine/Resource.cs;Engine/EngineActivity.cs;Engine/EngineView.cs" />
  </ItemGroup>
  <ItemGroup>
    <!-- Shaders and debug font shipped as embedded resources with their exact
         logical names (the code resolves them via GetManifestResourceStream).
         Without WithCulture=false MSBuild reads "Lit"/"Unlit" as satellite
         cultures and moves those files out of Engine.dll. -->
    <EmbeddedResource Include="Engine.Resources.Embedded.*">
      <WithCulture>false</WithCulture>
      <LogicalName>%(Filename)%(Extension)</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
</Project>
CSPROJ_EOF

cat > "$SRC_DIR/EntitySystem.Android.Net/EntitySystem.Android.Net.csproj" << 'CSPROJ_EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>EntitySystem.Android.Net</AssemblyName>
    <RootNamespace>EntitySystem.Android.Net</RootNamespace>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>$(NoWarn);CS0108;CS0114;CS0169;CS0414;CS0649;CS0612;CS0618;CS1591;CS8981</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Engine\Engine.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="**/*.cs" Exclude="obj/**;bin/**;Properties/AssemblyInfo.cs;EntitySystem.Android.Net/Resource.cs" />
  </ItemGroup>
</Project>
CSPROJ_EOF

cat > "$SRC_DIR/Survivalcraft/Survivalcraft.csproj" << 'CSPROJ_EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <AssemblyName>Survivalcraft</AssemblyName>
    <RootNamespace>Game</RootNamespace>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <!-- Built on net9.0 but should run on whatever modern runtime is installed. -->
    <RollForward>Major</RollForward>
    <NoWarn>$(NoWarn);CS0108;CS0114;CS0169;CS0414;CS0649;CS0612;CS0618;CS1591;CS8981</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="9.0.0" />
    <ProjectReference Include="..\Engine\Engine.csproj" />
    <ProjectReference Include="..\EntitySystem.Android.Net\EntitySystem.Android.Net.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="**/*.cs" Exclude="obj/**;bin/**;Properties/AssemblyInfo.cs;Game/Resource.cs" />
    <EmbeddedResource Include="Game.Content.pak" LogicalName="Game.Content.pak" />
  </ItemGroup>
</Project>
CSPROJ_EOF

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
step "Building OpenTK-1.0 (Linux platform layer)"
cd "$SRC_DIR/OpenTK-1.0"
dotnet restore
dotnet build -c Release --no-restore

step "Building Engine"
cd "$SRC_DIR/Engine"
dotnet restore
dotnet build -c Release --no-restore

step "Building EntitySystem.Android.Net"
cd "$SRC_DIR/EntitySystem.Android.Net"
dotnet restore
dotnet build -c Release --no-restore

step "Building Survivalcraft"
cd "$SRC_DIR/Survivalcraft"
dotnet restore
dotnet build -c Release --no-restore

# ---------------------------------------------------------------------------
# Output
# ---------------------------------------------------------------------------
step "Preparing output"
mkdir -p "$BUILD_DIR"
cp "$SRC_DIR/Survivalcraft/bin/Release/net9.0/Survivalcraft.dll" "$BUILD_DIR/"
cp "$SRC_DIR/Engine/bin/Release/net9.0/Engine.dll" "$BUILD_DIR/"
cp "$SRC_DIR/EntitySystem.Android.Net/bin/Release/net9.0/EntitySystem.Android.Net.dll" "$BUILD_DIR/"
cp "$SRC_DIR/OpenTK-1.0/bin/Release/net9.0/OpenTK-1.0.dll" "$BUILD_DIR/"
cp -r "$SRC_DIR/Survivalcraft/bin/Release/net9.0/"* "$BUILD_DIR/" 2>/dev/null || true

# The engine calls into libGLESv2 / libopenal, whose installed SONAMEs carry a
# version suffix the app does not use, so link the expected names here.
step "Linking native libraries"
link_native() {
    local link_name="$1" soname="$2" path=""
    path="$(ldconfig -p 2>/dev/null | awk -v s="$soname" '$1 == s { print $NF; exit }')" || true
    if [[ -n "$path" && -e "$path" ]]; then
        ln -sfn "$path" "$BUILD_DIR/$link_name"
        printf '  %-14s -> %s\n' "$link_name" "$path"
    else
        printf '  \033[33mwarning:\033[0m %s not found, %s left unresolved\n' "$soname" "$link_name" >&2
    fi
}
link_native libGLESv2.so libGLESv2.so.2
link_native libopenal.so libopenal.so.1

# Launch script
cat > "$BUILD_DIR/Survivalcraft" << 'LAUNCH_EOF'
#!/usr/bin/env bash
# Survivalcraft 2 Linux launcher
cd "$(dirname "$0")" || exit 1
# Build step symlinks libGLESv2.so / libopenal.so into this directory.
export LD_LIBRARY_PATH="$PWD${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec dotnet Survivalcraft.dll "$@"
LAUNCH_EOF
chmod +x "$BUILD_DIR/Survivalcraft"

step "Build complete!"
echo "Output directory: $BUILD_DIR"
echo "Run with: $BUILD_DIR/Survivalcraft"
