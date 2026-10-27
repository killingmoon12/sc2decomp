using System;

namespace OpenTK.Platform.Linux
{
    /// <summary>
    /// Handle for the SDL window and its GL context. SDL owns the native window;
    /// there is no X11 display or GLX visual on the PortMaster target.
    /// </summary>
    internal sealed class LinuxWindowInfo : IWindowInfo
    {
        public LinuxWindowInfo(IntPtr window, int glMajor, int glMinor)
        {
            WindowHandle = window;
            GLMajor = glMajor;
            GLMinor = glMinor;
        }

        public IntPtr WindowHandle { get; }

        /// <summary>
        /// Version of the ES context the window's EGL configuration was chosen
        /// for. The context must use the same version SDL used to select it.
        /// </summary>
        public int GLMajor { get; }

        public int GLMinor { get; }

        /// <summary>Kept for callers that expect an X11 display handle.</summary>
        public IntPtr DisplayHandle => IntPtr.Zero;

        public void Dispose()
        {
        }
    }
}
