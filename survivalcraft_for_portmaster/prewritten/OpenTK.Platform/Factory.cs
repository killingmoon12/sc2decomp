using System;
using OpenTK.Graphics;
using OpenTK.Input;
using OpenTK.Platform.Linux;

namespace OpenTK.Platform;

internal sealed class Factory : IPlatformFactory
{
    private class UnsupportedPlatform : IPlatformFactory
    {
        private static readonly string error_string = "Please, refer to http://www.opentk.com for more information.";
        public INativeWindow CreateNativeWindow(int x, int y, int width, int height, string title, GraphicsMode mode, GameWindowFlags options, DisplayDevice device) => throw new PlatformNotSupportedException(error_string);
        public IDisplayDeviceDriver CreateDisplayDeviceDriver() => throw new PlatformNotSupportedException(error_string);
        public IGraphicsContext CreateGLContext(GraphicsMode mode, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags) => throw new PlatformNotSupportedException(error_string);
        public IGraphicsContext CreateGLContext(ContextHandle handle, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags) => throw new PlatformNotSupportedException(error_string);
        public IGraphicsContext CreateESContext(GraphicsMode mode, IWindowInfo window, IGraphicsContext shareContext, int major, int minor, GraphicsContextFlags flags) => throw new PlatformNotSupportedException(error_string);
        public GraphicsContext.GetCurrentContextDelegate CreateGetCurrentGraphicsContext() => throw new PlatformNotSupportedException(error_string);
        public IGraphicsMode CreateGraphicsMode() => throw new PlatformNotSupportedException(error_string);
        public IKeyboardDriver2 CreateKeyboardDriver() => throw new PlatformNotSupportedException(error_string);
        public IMouseDriver2 CreateMouseDriver() => throw new PlatformNotSupportedException(error_string);
    }

    private static IPlatformFactory default_implementation;
    private static IPlatformFactory embedded_implementation;
    public static IPlatformFactory Default { get => default_implementation; private set => default_implementation = value; }
    public static IPlatformFactory Embedded { get => embedded_implementation; private set => embedded_implementation = value; }

    static Factory() { Default = new LinuxFactory(); Embedded = new LinuxFactory(); }

    public INativeWindow CreateNativeWindow(int x, int y, int width, int height, string title, GraphicsMode mode, GameWindowFlags options, DisplayDevice device) => default_implementation.CreateNativeWindow(x, y, width, height, title, mode, options, device);
    public IDisplayDeviceDriver CreateDisplayDeviceDriver() => default_implementation.CreateDisplayDeviceDriver();
    public IGraphicsContext CreateGLContext(GraphicsMode mode, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags) => default_implementation.CreateGLContext(mode, window, shareContext, directRendering, major, minor, flags);
    public IGraphicsContext CreateGLContext(ContextHandle handle, IWindowInfo window, IGraphicsContext shareContext, bool directRendering, int major, int minor, GraphicsContextFlags flags) => default_implementation.CreateGLContext(handle, window, shareContext, directRendering, major, minor, flags);
    public GraphicsContext.GetCurrentContextDelegate CreateGetCurrentGraphicsContext() => default_implementation.CreateGetCurrentGraphicsContext();
    public IGraphicsMode CreateGraphicsMode() => default_implementation.CreateGraphicsMode();
    public IKeyboardDriver2 CreateKeyboardDriver() => default_implementation.CreateKeyboardDriver();
    public IMouseDriver2 CreateMouseDriver() => default_implementation.CreateMouseDriver();
}
