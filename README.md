# SC2DECOMP

Bunch of scripts to decompile Survivalcraft 2 and build it.

## Getting Started
Currently Linux is the only supported platform, other platforms are planned in future.

## Building
Get your apk from the device where you bought the game and place it in the root of repository
To decompile and get everything ready to build run:
```
./decompile_survivalcraft.sh
```
To build the game run:
```
./survivalcraft_linux/build_survivalcraft_linux.sh
```
Built files will be located in:
```
./survivalcraft_linux/build/
```

## Running
In the `build` directory run:
```
dotnet Survivalcraft.dll
```

## Multiplayer
The Linux port supports host-authoritative multiplayer over both the local
network and the internet for up to eight players. Open a loaded world to other
players from the in-game menu, then join from the **Multiplayer** button on the
worlds screen — either from the LAN discovery list or by typing the host's
address and port (after forwarding `27020/tcp` on the host router).
See [`survivalcraft_linux/MULTIPLAYER.md`](survivalcraft_linux/MULTIPLAYER.md)
for design, protocol, ports, testing and known limitations.

### Dependencies

.NET SDK 9.0+ bash, rsync, patch, python3 ILSpy (for decompilation via decompile_survivalcraft.sh) SDL2 (libSDL2-2.0.so.0) libGLESv2.so.2 (Mesa) libopenal.so.1 (OpenAL Soft)

## License

This project is licensed under the MIT License - see the LICENSE.md file for details

# This project is made heavily using AI.

## All rights are reserved by Candy Rufus Games.
