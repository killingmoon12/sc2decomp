# Survivalcraft 2 Linux Port

Builds Survivalcraft 2 for Linux from a decompiled Android APK. Everything the
port changes lives in `patches/` and `prewritten/`; `src/` is regenerated from
the pristine decompile on every run, so a build is fully reproducible from the
APK plus this directory.

## Layout

```
/home/georgefloyd/build/
├── decompile_survivalcraft.sh              # APK -> C# (run once, or to refresh)
├── Survivalcraft-2-...-full-apkvision.apk  # untouched source APK
├── survivalcraft_decompiled/decompiled/    # pristine decompile (DECOMP_DIR)
└── survivalcraft_linux/
    ├── build_survivalcraft_linux.sh        # the build
    ├── patches/                            # 19 patches against the decompile
    ├── prewritten/                         # files copied into src/ verbatim
    │   ├── OpenTK.Platform.Linux/          # hand-written X11 + GLX + input layer
    │   ├── OpenTK.Platform/                # Factory -> LinuxFactory
    │   ├── OpenTK.Input/                   # OpenTK Mouse + pointer-capture API
    │   └── Engine/                         # Window, Storage, Utilities, input
    ├── src/                                # generated; never edit
    ├── build/                              # output
    │   ├── Survivalcraft                   # launcher
    │   ├── *.dll, libGLESv2.so, libopenal.so
    └── tools/                              # xinject.py, screenshot_window.py
```

## Quick start

```bash
./build_survivalcraft_linux.sh            # APK is auto-discovered in the parent dir
./build/Survivalcraft                     # play
```

## How the build works

1. **Sync** `src/` from the pristine decompile (`DECOMP_DIR`, overridable).
2. **Copy** `prewritten/` over it — these are whole-file replacements that are
   too large or too structural to express as patches (the Linux platform layer,
   `Engine.Window`, `Engine.Storage`, the OpenTK input drivers).
3. **Apply** every `patches/*.patch` with `patch -p1`. A patch that does not
   apply cleanly **stops the build** — it is never skipped silently.
4. **Generate** the four `.csproj` files (the decompile has none).
5. **Build** OpenTK-1.0 → Engine → EntitySystem.Android.Net → Survivalcraft.
6. **Link** `libGLESv2.so` / `libopenal.so` to the installed SONAMEs, because
   the engine dlopens the unversioned names.
7. **Write** the `build/Survivalcraft` launcher (sets `LD_LIBRARY_PATH`, runs
   `dotnet Survivalcraft.dll`).

## Data directory

Worlds, settings, skins, texture packs and logs live in
**`~/.config/Survivalcraft2`**.

| Override | Effect |
|----------|--------|
| `SC_DATA_DIR` | Use this directory instead |
| `XDG_CONFIG_HOME` | Respect the XDG base directory |

A first run migrates any data left in the old `~/.local/share` location and
removes the legacy folders once the move succeeds. The log is appended to
`~/.config/Survivalcraft2/Logs/Game.log`.

## Desktop input

The engine drives the pointer from the widget system: when the game hides the
cursor (in-world, no menus) `Window` switches to relative mode.

* The pointer is warped back to the window centre every frame and the camera
  reads the offset — no `XGrabPointer` confinement. Under Xwayland a confined
  pointer is turned into a pointer lock by the compositor, after which real
  mouse motion produces no `MotionNotify` at all, so the camera would never
  turn. Warping works on both X11 and Wayland.
* The cursor is hidden with a 1x1 blank cursor while relative mode is active.
* The window ignores pointer warps and key synthesis while unfocused, so
  alt-tabbing never steals the desktop pointer.
* `ComponentInput` starts in mouse/keyboard mode (`IsControlledByTouch=false`),
  so the on-screen move/look pads stay hidden instead of appearing until the
  first mouse movement.

## Window sizing

`ConfigureNotify` is tracked, so `Window.Size`, the GL viewport and the UI
layout follow the real window size when the window is resized or maximised.
The 3D view renders at `ResolutionMode` scale (High = 1.0) of the widget size,
so it scales with the window.

## Graphics

The window is a desktop GLX compatibility context. The engine still issues its
original `libGLESv2` P/Invokes, which resolve to Mesa's GLES-on-GL layer
(`libGLESv2.so.2`), so shaders and bindings are unchanged from the Android
build. `SwapBuffers` must call `glXSwapBuffers` — dropping it produces a black
screen.

## Patches

| Patch | Purpose |
|-------|---------|
| `OpenTK_Configuration.cs` | Disable Android detection |
| `OpenTK_Vector*.cs` | Fix `Clamp` for C# 12 ref-struct rules |
| `OpenTK.Platform_Factory.cs` | Use `LinuxFactory` |
| `Engine_Window.cs` | Replace Android Activity/View with OpenTK `GameWindow` |
| `Engine_Storage.cs` | Replace Android `StatFs` with `DriveInfo` |
| `Engine_Utilities.cs` | `/proc/meminfo` memory query |
| `Survivalcraft_ComponentInput.cs` | Desktop port: start in mouse/keyboard mode |
| `Survivalcraft_DeviceManager.cs` | Read device info from `/proc` and `/etc/os-release` |
| `Survivalcraft_ScreenCaptureManager.cs` | Save screenshots as PNG |
| `Survivalcraft_ScreenResolutionManager.cs` | Default to 96 DPI |
| `Survivalcraft_WebManager.cs` | Ping via `System.Net.NetworkInformation` |
| `Survivalcraft_WebBrowserManager.cs` | `Process.Start` with `UseShellExecute` |
| `Survivalcraft_*ExternalContentProvider.cs` | Linux provider under `~/SurvivalcraftExternal` |
| `Multiplayer_PlayScreen.cs` | "Multiplayer" button + join screen UI on world selection |
| `Multiplayer_GameMenuDialog.cs` | "Host to LAN"/"Stop Hosting" + "Connected Players" in-game menu |
| `Multiplayer_PlayerData.cs` | `IsRemote` flag; remote players skip local input/GameWidget |
| `Multiplayer_SubsystemPlayers.cs` | Raise local player cap to 8; remote players exempt |
| `Multiplayer_ComponentPlayer.cs` | Skip local input simulation for remote players |
| `Multiplayer_GameManager.cs` | Tick network session each frame; stop on world dispose |

## Multiplayer

The multiplayer stage (`patches/Multiplayer_*.patch` + `prewritten/Multiplayer/`) adds LAN multiplayer modelled on Vintage Story:

* **Host-authoritative** — the host simulates the world; clients send input/player state and receive the host's broadcast state.
* **UDP game channel** — unreliable, latest-wins player-state broadcasts (20 Hz) and ping/pong.
* **TCP control channel** — reliable handshake, chat, block-change requests, and chunked world-snapshot transfer on join.
* **LAN discovery** — UDP broadcast probe (`SC2MP!`) to find hosts on the local network.
* **Connection flow** — client connects → host sends world snapshot → client loads the host's world → players spawn and sync.

### How to use

1. Start a world in single-player, open the "..." menu, and choose **"Host to LAN"** (port `55551` by default).
2. On another machine, open the world-selection screen, click **"Multiplayer"**, enter the host IP/port (or click **"Find LAN Games"**), and click **Connect**.
3. The host's world is transferred to the client, which then loads it and joins as a remote player.
4. Use **"Connected Players"** in the "..." menu to see who is joined; **"Stop Hosting"** ends the session.

All multiplayer code lives in `prewritten/Multiplayer/` (new files, survive `src/` regeneration) and `patches/Multiplayer_*.patch` (revertable with `patch -R`).

## Requirements

.NET 9 SDK, bash, `rsync`, `patch`, libX11, Mesa, OpenAL.

## Notes

* The startup log reports `Platform=Android`; that is the original value and is
  harmless — no Android API is reached at runtime.
* `tools/xinject.py` sends synthetic X11 input (`key:` / `move:` / `click:`)
  and `tools/screenshot_window.py` grabs a window via `XGetImage` — useful for
  headless-ish smoke testing.

## License

Reverse-engineering project for educational purposes. Survivalcraft 2 is
copyright Candy Rufus Games.
