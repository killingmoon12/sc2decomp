using System;
using System.Runtime.InteropServices;
using OpenTK.Platform.Egl;

namespace OpenTK.Platform.Linux
{
    internal sealed class LinuxWindowInfo : IWindowInfo
    {
        private readonly IntPtr _window;
        private readonly IntPtr _display;
        private readonly IntPtr _surface;
        private readonly IntPtr _visualInfo;
        private bool _disposed;
        public IntPtr WindowHandle => _window;
        public IntPtr DisplayHandle => _display;
        public IntPtr SurfaceHandle => _surface;
        // Pointer to the XVisualInfo returned by glXChooseVisual (Xlib/GLX allocated).
        public IntPtr VisualInfo => _visualInfo;
        public LinuxWindowInfo(IntPtr window, IntPtr display) : this(window, display, IntPtr.Zero, IntPtr.Zero) { }
        public LinuxWindowInfo(IntPtr window, IntPtr display, IntPtr surface) : this(window, display, surface, IntPtr.Zero) { }
        public LinuxWindowInfo(IntPtr window, IntPtr display, IntPtr surface, IntPtr visualInfo) { _window = window; _display = display; _surface = surface; _visualInfo = visualInfo; }
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
        private void Dispose(bool manual) { if (!_disposed) { if (manual && _surface != IntPtr.Zero && _display != IntPtr.Zero) EglDestroySurface(_display, _surface); _disposed = true; } }
        ~LinuxWindowInfo() { Dispose(false); }
        [DllImport("libEGL.so.1", EntryPoint = "eglDestroySurface")] private static extern bool EglDestroySurface(IntPtr dpy, IntPtr surface);
    }
}
