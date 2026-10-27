using System;
using System.Drawing;
using OpenTK.Platform;

namespace OpenTK.Platform.Linux
{
    /// <summary>
    /// Reads the real display mode from SDL. Native resolution changes are not
    /// used; the game always runs fullscreen desktop.
    /// </summary>
    internal sealed class LinuxDisplayDeviceDriver : DisplayDeviceBase
    {
        public LinuxDisplayDeviceDriver()
        {
            int width = 1280;
            int height = 720;
            float refresh = 60f;

            if (SDL2.InitSubSystem(SDL2.SDL_INIT_VIDEO) == 0)
            {
                if (SDL2.GetNumVideoDisplays() > 0 &&
                    SDL2.GetDesktopDisplayMode(0, out SDL2.SDL_DisplayMode mode) == 0 &&
                    mode.w > 0 && mode.h > 0)
                {
                    width = mode.w;
                    height = mode.h;
                    if (mode.refresh_rate > 0) refresh = mode.refresh_rate;
                }
            }

            Primary = new DisplayDevice(
                new DisplayResolution(0, 0, width, height, 32, refresh),
                true,
                Array.Empty<DisplayResolution>(),
                new Rectangle(0, 0, width, height),
                null);
        }

        public override bool TryChangeResolution(DisplayDevice device, DisplayResolution resolution) => false;

        public override bool TryRestoreResolution(DisplayDevice device) => false;
    }
}
