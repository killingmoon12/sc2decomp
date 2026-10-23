# SC2DECOMP

Bunch of scripts to decompile Survivalcraft 2 and build it.

## Getting Started
Currently Linux is the only supported platform, other OSes are planned in future.

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

### Dependencies

.NET SDK 9.0+ bash, rsync, patch, python3 ILSpy (for decompilation via decompile_survivalcraft.sh) libGLESv2.so.2 (Mesa) libopenal.so.1 (OpenAL Soft) libX11, libGLX, libXrandr, libXi, libXcursor XWayland

## License

This project is licensed under the MIT License - see the LICENSE.md file for details

# This project is made heavily using AI.

## All rights are reserved by Candy Rufus Games.
