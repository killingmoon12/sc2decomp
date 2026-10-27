## Notes

Thanks to [Marcin Kalicinski (Kaalus)](https://kaalus.wordpress.com/) for creating Survivalcraft 2, a blocky survival sandbox whose four-season world, crafting, farming, and animal husbandry give it far more depth than its simple voxel look suggests.

Survivalcraft 2, its code, artwork, audio, and other content are Copyright (c) 2011-2025 Candy Rufus Games. This package is for personal use by owners of a legally purchased copy and must not be redistributed or published as a ready-to-run game without permission from Candy Rufus Games.

The bundled OpenAL Soft library is LGPL-2.1. Its source is available at https://github.com/kcat/openal-soft.

## Install

Copy `Survivalcraft 2.sh` and the `survivalcraft2` folder into `/roms/ports/` on the R36S, then refresh EmulationStation. PortMaster downloads the .NET 8 runtime automatically the first time the port starts.

## Controls

| Control | Action |
|--|--|
| Left stick | Move |
| Right stick | Look |
| A | Jump / confirm |
| B | Drop / back |
| X | Inventory |
| Y | Clothing |
| L1 | Aim / interact |
| R1 | Dig / hit |
| L2 | Edit item |
| R2 | Crouch |
| L3 | Mount or dismount |
| R3 | Change camera |
| D-pad up | Mount or dismount |
| D-pad down | Change camera |
| D-pad left | Previous inventory slot |
| D-pad right | Next inventory slot |
| Start | Back / cancel |
| Select | Gamepad help |
| Start + Select | Quit the game |

Keyboard and mouse input also work.

## Compile

The game is closed source. The port's patches and SDL2 platform layer are in the source workspace used to build this package. Install the .NET 8 SDK and the build tools:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
export PATH="$HOME/.dotnet:$PATH"
sudo dnf install rsync patch zip
```

The build script expects a decompiled source tree in `../survivalcraft_decompiled/decompiled` or in the directory named by `DECOMP_DIR`. It regenerates `src/`, applies every patch from `patches/`, installs the platform layer from `prewritten/`, and publishes the aarch64 package under `build/`:

```bash
./build_survivalcraft_linux.sh /path/to/survivalcraft-2.apk
```
