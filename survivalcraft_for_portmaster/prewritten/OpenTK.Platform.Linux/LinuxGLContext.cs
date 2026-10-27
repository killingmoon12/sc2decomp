using System;
using OpenTK.Graphics;

namespace OpenTK.Platform.Linux
{
    /// <summary>
    /// OpenGL ES 2 context owned by SDL2. The engine only uses GLES2 entry
    /// points, and SDL picks EGL/KMSDRM or EGL/Wayland for the device.
    /// </summary>
    internal sealed class LinuxGLContext : DesktopGraphicsContext
    {
        private IntPtr _context;
        private LinuxWindowInfo _windowInfo;
        private bool _disposed;
        private int _swapInterval = 1;

        public override bool IsCurrent => SDL2.GL_GetCurrentContext() == _context;

        public override int SwapInterval
        {
            get => _swapInterval;
            set
            {
                _swapInterval = Math.Clamp(value, 0, 1);
                if (_context != IntPtr.Zero)
                {
                    SDL2.GL_SetSwapInterval(_swapInterval);
                }
            }
        }

        public LinuxGLContext(GraphicsMode mode, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags)
        {
            if (window is not LinuxWindowInfo linuxWindow) throw new ArgumentException("Window must be LinuxWindowInfo", nameof(window));
            _windowInfo = linuxWindow;
            Mode = mode;

            // The EGL configuration was chosen when SDL created the window.
            // Ask for the same ES version here; changing the depth or stencil
            // attributes at this point cannot change that configuration.
            SetContextVersion(_windowInfo.GLMajor, _windowInfo.GLMinor);
            _context = SDL2.GL_CreateContext(_windowInfo.WindowHandle);

            if (_context == IntPtr.Zero && _windowInfo.GLMajor > 2)
            {
                // An ES3 EGL config can normally provide an ES2 context as
                // well. Fall back to ES2 rather than failing to start.
                Console.Error.WriteLine("[SDL] ES" + _windowInfo.GLMajor + " context failed: " + SDL2.GetError());
                SetContextVersion(2, 0);
                _context = SDL2.GL_CreateContext(_windowInfo.WindowHandle);
            }

            if (_context == IntPtr.Zero)
            {
                throw new GraphicsContextException("Failed to create SDL2 GLES2 context: " + SDL2.GetError());
            }

            Console.Error.WriteLine(
                $"[SDL] GL context created (requested ES{_windowInfo.GLMajor}.{_windowInfo.GLMinor})");

            Handle = new ContextHandle(_context);
            MakeCurrent(window);
            SDL2.GL_SetSwapInterval(_swapInterval);
        }

        public LinuxGLContext(ContextHandle handle, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags)
        {
            if (handle == ContextHandle.Zero) throw new ArgumentException("handle");
            if (window is not LinuxWindowInfo linuxWindow) throw new ArgumentException("Window must be LinuxWindowInfo", nameof(window));
            _windowInfo = linuxWindow;
            _context = handle.Handle;
            Handle = new ContextHandle(_context);
        }

        private static void SetContextVersion(int major, int minor)
        {
            SDL2.GL_SetAttribute(SDL2.SDL_GL_CONTEXT_MAJOR_VERSION, major);
            SDL2.GL_SetAttribute(SDL2.SDL_GL_CONTEXT_MINOR_VERSION, minor);
            SDL2.GL_SetAttribute(SDL2.SDL_GL_CONTEXT_PROFILE_MASK, SDL2.SDL_GL_CONTEXT_PROFILE_ES);
            SDL2.GL_SetAttribute(SDL2.SDL_GL_CONTEXT_FLAGS, 0);
            SDL2.GL_SetAttribute(SDL2.SDL_GL_DOUBLEBUFFER, 1);
        }

        public override void LoadAll()
        {
            // Engine.Graphics.Bindings.GL owns its own P/Invokes; OpenTK's
            // ES20/ES30 wrappers are not used by the game.
        }

        public override void MakeCurrent(IWindowInfo window)
        {
            if (window is LinuxWindowInfo linuxWindow) _windowInfo = linuxWindow;
            if (SDL2.GL_MakeCurrent(_windowInfo.WindowHandle, _context) != 0)
            {
                throw new GraphicsContextException("Failed to make SDL2 GLES2 context current: " + SDL2.GetError());
            }
        }

        public override IntPtr GetAddress(string function) => SDL2.GL_GetProcAddress(function);

        public override void SwapBuffers() => SDL2.GL_SwapWindow(_windowInfo.WindowHandle);

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool manual)
        {
            if (_disposed) return;
            if (manual && _context != IntPtr.Zero)
            {
                SDL2.GL_MakeCurrent(_windowInfo.WindowHandle, IntPtr.Zero);
                SDL2.GL_DeleteContext(_context);
                _context = IntPtr.Zero;
            }
            _disposed = true;
        }

        ~LinuxGLContext() => Dispose(false);

        public static ContextHandle GetCurrentContext()
        {
            return new ContextHandle(SDL2.GL_GetCurrentContext());
        }
    }
}
