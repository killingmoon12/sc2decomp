using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics;
using OpenTK.Graphics.ES20;
using OpenTK.Graphics.ES30;

namespace OpenTK.Platform.Linux
{
    internal sealed class LinuxGLContext : DesktopGraphicsContext
    {
        private IntPtr _context;
        private LinuxWindowInfo _windowInfo;
        private bool _disposed;
        public override bool IsCurrent => GLX.glXGetCurrentContext() == _context;
        public override int SwapInterval { get => 1; set { } }

        public LinuxGLContext(GraphicsMode mode, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags)
        {
            if (window is not LinuxWindowInfo linuxWindow) throw new ArgumentException("Window must be LinuxWindowInfo", nameof(window));
            _windowInfo = linuxWindow;
            IntPtr share = shareContext is LinuxGLContext l ? l._context : IntPtr.Zero;
            // glXCreateContext takes the XVisualInfo returned by glXChooseVisual,
            // NOT the window (drawable) handle.
            if (_windowInfo.VisualInfo == IntPtr.Zero) throw new GraphicsContextException("LinuxWindowInfo is missing its GLX visual");
            _context = GLX.glXCreateContext(_windowInfo.DisplayHandle, _windowInfo.VisualInfo, share, directRendering);
            if (_context == IntPtr.Zero) throw new GraphicsContextException("Failed to create GLX context");
            Handle = new ContextHandle(_context);
            MakeCurrent(window);
        }

        public LinuxGLContext(ContextHandle handle, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags)
        {
            if (handle == ContextHandle.Zero) throw new ArgumentException("handle");
            if (window is not LinuxWindowInfo linuxWindow) throw new ArgumentException("Window must be LinuxWindowInfo", nameof(window));
            _windowInfo = linuxWindow;
            _context = handle.Handle;
            Handle = new ContextHandle(_context);
        }

        public override void LoadAll()
        {
            // The Engine binds its own GL entry points (Engine.Graphics.Bindings.GL);
            // OpenTK's ES20/ES30 wrappers are not used by the game, so there is
            // nothing to load here.
        }

        public override void MakeCurrent(IWindowInfo window)
        {
            if (window is LinuxWindowInfo linuxWindow) _windowInfo = linuxWindow;
            int result = GLX.glXMakeCurrent(_windowInfo.DisplayHandle, _windowInfo.WindowHandle, _context);
            if (result == 0) throw new GraphicsContextException("Failed to make GLX context current");
        }

        public override IntPtr GetAddress(string function) => GLX.glXGetProcAddress(function);

        public override void SwapBuffers() => GLX.glXSwapBuffers(_windowInfo.DisplayHandle, _windowInfo.WindowHandle);

        public override void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
        private void Dispose(bool manual)
        {
            if (!_disposed)
            {
                if (manual)
                {
                    GLX.glXMakeCurrent(_windowInfo.DisplayHandle, IntPtr.Zero, IntPtr.Zero);
                    GLX.glXDestroyContext(_windowInfo.DisplayHandle, _context);
                }
                _disposed = true;
            }
        }
        ~LinuxGLContext() { Dispose(false); }
        public static ContextHandle GetCurrentContext() => new ContextHandle(GLX.glXGetCurrentContext());
    }
}
