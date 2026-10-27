using System;
using OpenTK.Graphics;
using OpenTK.Input;
using OpenTK.Platform;

namespace OpenTK.Platform.Linux
{
    internal sealed class LinuxFactory : IPlatformFactory
    {
        public INativeWindow CreateNativeWindow(int x, int y, int width, int height, string title, GraphicsMode mode, GameWindowFlags options, DisplayDevice device)
        {
            return new LinuxNativeWindow(x, y, width, height, title, mode, options, device);
        }
        public IDisplayDeviceDriver CreateDisplayDeviceDriver() => new LinuxDisplayDeviceDriver();
        public IGraphicsContext CreateGLContext(GraphicsMode mode, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags)
        {
            return new LinuxGLContext(mode, window, shareContext, directRendering, major, minor, flags);
        }
        public IGraphicsContext CreateGLContext(ContextHandle handle, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags)
        {
            return new LinuxGLContext(handle, window, shareContext, directRendering, major, minor, flags);
        }
        public GraphicsContext.GetCurrentContextDelegate CreateGetCurrentGraphicsContext() => LinuxGLContext.GetCurrentContext;
        public IGraphicsMode CreateGraphicsMode() => new LinuxGraphicsMode();
        public IKeyboardDriver2 CreateKeyboardDriver() => new LinuxKeyboardDriver();
        public IMouseDriver2 CreateMouseDriver() => new LinuxMouseDriver();
    }
}
