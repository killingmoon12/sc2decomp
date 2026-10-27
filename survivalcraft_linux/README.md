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
    ├── patches/                            # patches against the decompile
    ├── prewritten/                         # files copied into src/ verbatim
    │   └── Engine/                         # SDL window/input, Storage, Utilities
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
2. **Apply** every `patches/*.patch` with `patch -p1`. A patch that does not
   apply cleanly **stops the build** — it is never skipped silently.
3. **Copy** `prewritten/` over it — these are whole-file replacements that are
   too large or too structural to express as patches (the SDL window/input
   layer, `Engine.Window`, `Engine.Storage`).
4. **Generate** the three `.csproj` files (the decompile has none).
5. **Build** Engine → EntitySystem.Android.Net → Survivalcraft.
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
cursor (in-world, no menus) `Window` switches SDL into relative mouse mode.

* Relative mode feeds `SDL_MOUSEMOTION` deltas straight to the camera; SDL also
  hides and confines the pointer. If the video backend cannot do relative mode
  the port falls back to warping the pointer back to the window centre and
  reading the offset.
* Releasing the cursor (menus, alt-tab) leaves the desktop pointer alone; the
  window ignores input while unfocused.
* Keyboard and text input come from `SDL_KEYDOWN`/`SDL_TEXTINPUT`, so text
  fields receive layout-aware characters (including non-ASCII) as well as
  navigation keys.
* `ComponentInput` starts in mouse/keyboard mode (`IsControlledByTouch=false`),
  so the on-screen move/look pads stay hidden instead of appearing until the
  first mouse movement.

## Window sizing

`SDL_WINDOWEVENT_SIZE_CHANGED` is tracked, so `Window.Size`, the GL viewport
and the UI layout follow the real window size when the window is resized or
maximised. The 3D view renders at `ResolutionMode` scale (High = 1.0) of the
widget size, so it scales with the window.

## Graphics

SDL creates the window and a desktop OpenGL compatibility context
(`SDL_GL_CreateContext`). The engine still issues its original `libGLESv2`
P/Invokes, which resolve to Mesa's GLES-on-GL layer (`libGLESv2.so.2`), so
shaders and bindings are unchanged from the Android build. Presenting is done
with `SDL_GL_SwapWindow`.

## Patches

| Patch | Purpose |
|-------|---------|
| `Engine_Storage.cs` | Replace Android `StatFs` with `DriveInfo` |
| `Engine_Utilities.cs` | `/proc/meminfo` memory query |
| `Survivalcraft_ComponentInput.cs` | Desktop port: start in mouse/keyboard mode |
| `Survivalcraft_DeviceManager.cs` | Read device info from `/proc` and `/etc/os-release` |
| `Survivalcraft_ScreenCaptureManager.cs` | Save screenshots as PNG |
| `Survivalcraft_ScreenResolutionManager.cs` | Default to 96 DPI |
| `Survivalcraft_WebManager.cs` | Ping via `System.Net.NetworkInformation` |
| `Survivalcraft_WebBrowserManager.cs` | `Process.Start` with `UseShellExecute` |
| `Survivalcraft_*ExternalContentProvider.cs` | Linux provider under `~/SurvivalcraftExternal` |

## Requirements

.NET 9 SDK, bash, `rsync`, `patch`, SDL2 (`libSDL2-2.0.so.0`), Mesa
(`libGLESv2.so.2`), OpenAL Soft (`libopenal.so.1`).

## Notes

* The startup log reports `Platform=Android`; that is the original value and is
  harmless — no Android API is reached at runtime.
* `tools/xinject.py` sends synthetic X11 input (`key:` / `move:` / `click:`)
  and `tools/screenshot_window.py` grabs a window via `XGetImage` — useful for
  headless-ish smoke testing.

## License

Reverse-engineering project for educational purposes. Survivalcraft 2 is
copyright Candy Rufus Games.
