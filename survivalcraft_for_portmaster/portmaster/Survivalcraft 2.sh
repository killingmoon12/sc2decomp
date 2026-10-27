#!/bin/bash

# This marker runs before PortMaster's control.txt. If the launcher starts at
# all, one of these files will contain the bootstrap line even when the later
# source/get_controls or the game itself fails.
BOOT_DIR="$(cd "$(dirname "$0")" 2>/dev/null && pwd)"
if [ -z "$BOOT_DIR" ]; then
  BOOT_DIR="/roms/ports"
fi
BOOT_LOG="$BOOT_DIR/survivalcraft2/bootstrap.txt"
mkdir -p "$BOOT_DIR/survivalcraft2" 2>/dev/null || true
printf '=== launcher bootstrap %s argv0=%s dir=%s ===\n' "$(date 2>/dev/null)" "$0" "$BOOT_DIR" > "$BOOT_LOG" 2>/dev/null || true
printf '=== launcher bootstrap %s argv0=%s dir=%s ===\n' "$(date 2>/dev/null)" "$0" "$BOOT_DIR" > /tmp/survivalcraft2-bootstrap.txt 2>/dev/null || true
sync 2>/dev/null || true

XDG_DATA_HOME=${XDG_DATA_HOME:-$HOME/.local/share}

if [ -d "/opt/system/Tools/PortMaster/" ]; then
  controlfolder="/opt/system/Tools/PortMaster"
elif [ -d "/opt/tools/PortMaster/" ]; then
  controlfolder="/opt/tools/PortMaster"
elif [ -d "$XDG_DATA_HOME/PortMaster/" ]; then
  controlfolder="$XDG_DATA_HOME/PortMaster"
else
  controlfolder="/roms/ports/PortMaster"
fi

source $controlfolder/control.txt
[ -f "${controlfolder}/mod_${CFW_NAME}.txt" ] && source "${controlfolder}/mod_${CFW_NAME}.txt"
get_controls

GAMEDIR=/$directory/ports/survivalcraft2
BINARY="dotnet"
GAME_DLL="$GAMEDIR/Survivalcraft.dll"
runtime="dotnet-8.0.12"
runtime_dir="/tmp/$runtime"

mkdir -p "$GAMEDIR/conf" 2>/dev/null || true
chmod +x "$0" 2>/dev/null || $ESUDO chmod +x "$0" 2>/dev/null || true
cd "$GAMEDIR" || true

LOG="$GAMEDIR/log.txt"
if ! : > "$LOG" 2>/dev/null; then
  $ESUDO chmod -R a+rwX "$GAMEDIR" 2>/dev/null || true
fi
if ! : > "$LOG" 2>/dev/null; then
  LOG="/tmp/survivalcraft2-log.txt"
  : > "$LOG" 2>/dev/null || true
fi
exec > "$LOG" 2>&1
trap 'status=$?; echo "=== Launcher exit: $status ==="; sync' EXIT
set -x

echo "=== Survivalcraft 2 package controls v8 (2026-10-27) ==="
echo "LOG=$LOG PWD=$(pwd)"
echo "GAME_DLL=$GAME_DLL"
if command -v sha256sum >/dev/null 2>&1; then
  sha256sum "$GAME_DLL" "$GAMEDIR/OpenTK-1.0.dll" 2>&1 || true
fi
ls -ld "$GAMEDIR" "$GAMEDIR/conf" 2>&1 || true
ls -l "$GAME_DLL" "$GAMEDIR/OpenTK-1.0.dll" 2>&1 || true

echo "=== Launch environment ==="
date
uname -a
echo "CFW=${CFW_NAME-} arch=${DEVICE_ARCH-} display=${DISPLAY-} can_mount=${PM_CAN_MOUNT-}"
echo "SDL_VIDEODRIVER=${SDL_VIDEODRIVER-<unset>} SDL_AUDIODRIVER=${SDL_AUDIODRIVER-<unset>} LIBGL_ALWAYS_SOFTWARE=${LIBGL_ALWAYS_SOFTWARE-<unset>}"
ls -l /dev/dri 2>&1 || true
if command -v ldconfig >/dev/null 2>&1; then
  ldconfig -p 2>/dev/null | grep -E 'libSDL2|libEGL|libGLESv2|libgbm|libdrm' | head -20
fi
SDL2_CANDIDATES="$(ls -1 /usr/lib/aarch64-linux-gnu/libSDL2-2.0.so.0.* /usr/lib/libSDL2-2.0.so.0.* /lib/aarch64-linux-gnu/libSDL2-2.0.so.0.* /lib/libSDL2-2.0.so.0.* 2>/dev/null | sort -V)"
printf '%s\n' "$SDL2_CANDIDATES"

# Some ArkOS images keep an older SDL2 as the default SONAME. Load the newest
# system copy for the game only; it is the build made for this Mali stack.
SYS_SDL="$(printf '%s\n' "$SDL2_CANDIDATES" | tail -1)"
if [ -z "$SYS_SDL" ]; then
  SYS_SDL="$(find /usr/lib /lib -maxdepth 4 -name 'libSDL2-2.0.so.0.*' -type f 2>/dev/null | sort -V | tail -1)"
fi
echo "Selected system SDL2: ${SYS_SDL:-<none>}"
sync

if [ ! -f "$controlfolder/libs/${runtime}.squashfs" ]; then
  if [ ! -f "$controlfolder/harbourmaster" ]; then
    pm_show_error "This port requires the latest PortMaster to run, please go to https://portmaster.games/ for more info." --title "PortMaster update needed"
    exit 1
  fi
  pm_message "Downloading the .NET runtime, this can take a few minutes..." --title "Please wait"
  $ESUDO $controlfolder/harbourmaster --quiet --no-check runtime_check "${runtime}.squashfs"
fi

echo "=== Runtime mount ==="
$ESUDO mkdir -p "$runtime_dir"
if [[ "$PM_CAN_MOUNT" != "N" ]]; then
  $ESUDO umount "$runtime_dir" 2>/dev/null || true
fi
$ESUDO mount "$controlfolder/libs/${runtime}.squashfs" "$runtime_dir" || true
ls -l "$runtime_dir/$BINARY" 2>&1 || true

if [ ! -x "$runtime_dir/$BINARY" ]; then
  echo "Runtime unavailable: $runtime_dir/$BINARY is missing or not executable"
  pm_show_error "Could not mount the .NET runtime. Please reinstall PortMaster and try again." --title "Runtime unavailable"
  exit 1
fi

export DOTNET_ROOT="$runtime_dir"
export DOTNET_MULTILEVEL_LOOKUP=0
export SC_DATA_DIR="$GAMEDIR/conf"

if ! find /usr/lib /lib -maxdepth 3 -name 'libicuuc.so*' -print -quit 2>/dev/null | grep -q .; then
  export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi

export LD_LIBRARY_PATH="$GAMEDIR/libs.${DEVICE_ARCH}${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
if [ -n "$sdl_controllerconfig" ]; then
  export SDL_GAMECONTROLLERCONFIG="$sdl_controllerconfig"
fi

$GPTOKEYB2 "$BINARY" > /dev/null 2>&1 &
echo "GPTOKEYB2 pid=$!"

pm_platform_helper "$runtime_dir/$BINARY"

echo "=== Launching $runtime_dir/$BINARY $GAME_DLL ==="
echo "DOTNET_ROOT=$DOTNET_ROOT SC_DATA_DIR=$SC_DATA_DIR"
echo "LD_LIBRARY_PATH=$LD_LIBRARY_PATH"
if [ -n "$SYS_SDL" ]; then
  echo "LD_PRELOAD=$SYS_SDL${LD_PRELOAD:+:$LD_PRELOAD}"
  LD_PRELOAD="$SYS_SDL${LD_PRELOAD:+:$LD_PRELOAD}" "$runtime_dir/$BINARY" "$GAME_DLL"
else
  echo "LD_PRELOAD=<none>"
  "$runtime_dir/$BINARY" "$GAME_DLL"
fi
status=$?
echo "=== Game exited: $status ==="
sync

if [[ "$PM_CAN_MOUNT" != "N" ]]; then
  $ESUDO umount "$runtime_dir" 2>/dev/null || true
fi

pm_finish
