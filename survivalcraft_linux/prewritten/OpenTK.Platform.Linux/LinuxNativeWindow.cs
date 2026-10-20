using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenTK.Graphics;
using OpenTK.Input;
using OpenTK.Platform;

namespace OpenTK.Platform.Linux
{
    internal sealed class LinuxNativeWindow : INativeWindow
    {
        private readonly LinuxWindowInfo _windowInfo;
        private bool _exists = true, _disposed;
        private int _x, _y, _width, _height; private string _title; private bool _visible = true;
        private readonly IntPtr _eventBuffer = Marshal.AllocHGlobal(192);
        private WindowState _windowState = WindowState.Normal; private WindowBorder _windowBorder = WindowBorder.Resizable;
        private bool _focused = true;
        public bool Exists => _exists; public IWindowInfo WindowInfo => _windowInfo;
        public string Title { get => _title; set { _title = value; if (_exists && _windowInfo.DisplayHandle != IntPtr.Zero && _windowInfo.WindowHandle != IntPtr.Zero) { X11.XStoreName(_windowInfo.DisplayHandle, _windowInfo.WindowHandle, value); X11.XFlush(_windowInfo.DisplayHandle); } TitleChanged?.Invoke(this, EventArgs.Empty); } }
        public bool Focused => _focused;
        public bool Visible { get => _visible; set { _visible = value; VisibleChanged?.Invoke(this, EventArgs.Empty); } }
        public WindowState WindowState { get => _windowState; set { _windowState = value; WindowStateChanged?.Invoke(this, EventArgs.Empty); } }
        public WindowBorder WindowBorder { get => _windowBorder; set { _windowBorder = value; WindowBorderChanged?.Invoke(this, EventArgs.Empty); } }
        public Rectangle Bounds { get => new Rectangle(_x, _y, _width, _height); set { _x = value.X; _y = value.Y; _width = value.Width; _height = value.Height; if (_exists) MoveWindow(_x, _y, _width, _height); Resize?.Invoke(this, EventArgs.Empty); } }
        public Point Location { get => new Point(_x, _y); set { _x = value.X; _y = value.Y; if (_exists) MoveWindow(_x, _y, _width, _height); Move?.Invoke(this, EventArgs.Empty); } }
        public Size Size { get => new Size(_width, _height); set { _width = value.Width; _height = value.Height; if (_exists) MoveWindow(_x, _y, _width, _height); Resize?.Invoke(this, EventArgs.Empty); } }
        public int X { get => _x; set { _x = value; if (_exists) MoveWindow(_x, _y, _width, _height); Move?.Invoke(this, EventArgs.Empty); } }
        public int Y { get => _y; set { _y = value; if (_exists) MoveWindow(_x, _y, _width, _height); Move?.Invoke(this, EventArgs.Empty); } }
        public int Width { get => _width; set { _width = value; if (_exists) MoveWindow(_x, _y, _width, _height); Resize?.Invoke(this, EventArgs.Empty); } }
        public int Height { get => _height; set { _height = value; if (_exists) MoveWindow(_x, _y, _width, _height); Resize?.Invoke(this, EventArgs.Empty); } }
        public Rectangle ClientRectangle { get => new Rectangle(0, 0, _width, _height); set { } }
        public Size ClientSize { get => new Size(_width, _height); set { } }
        public IInputDriver InputDriver => null;
        public event EventHandler<EventArgs> Move; public event EventHandler<EventArgs> Resize; public event EventHandler<CancelEventArgs> Closing; public event EventHandler<EventArgs> Closed; public event EventHandler<EventArgs> Disposed; public event EventHandler<EventArgs> TitleChanged; public event EventHandler<EventArgs> VisibleChanged; public event EventHandler<EventArgs> FocusedChanged; public event EventHandler<EventArgs> WindowBorderChanged; public event EventHandler<EventArgs> WindowStateChanged; public event EventHandler<KeyPressEventArgs> KeyPress; public event EventHandler<EventArgs> MouseLeave; public event EventHandler<EventArgs> MouseEnter;

        public LinuxNativeWindow(int x, int y, int width, int height, string title, GraphicsMode mode, GameWindowFlags options, DisplayDevice device)
        {
            _x = x; _y = y; _width = width; _height = height; _title = title;
            IntPtr display = X11.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) throw new PlatformNotSupportedException("Failed to open X11 display");
            int screen = X11.XDefaultScreen(display);
            IntPtr root = X11.XRootWindow(display, screen);
            int[] attribs = { GLX.GLX_RGBA, GLX.GLX_RED_SIZE, 8, GLX.GLX_GREEN_SIZE, 8, GLX.GLX_BLUE_SIZE, 8, GLX.GLX_ALPHA_SIZE, 8, GLX.GLX_DEPTH_SIZE, mode.Depth, GLX.GLX_STENCIL_SIZE, mode.Stencil, GLX.GLX_DOUBLEBUFFER, 1, 0 };
            IntPtr visualInfo = GLX.glXChooseVisual(display, screen, attribs);
            if (visualInfo == IntPtr.Zero) throw new PlatformNotSupportedException("Failed to find suitable GLX visual");
            // glXChooseVisual returns an XVisualInfo*. XCreateWindow/XCreateColormap need
            // its embedded Visual*, not the XVisualInfo* itself.
            X11.XVisualInfo vi = Marshal.PtrToStructure<X11.XVisualInfo>(visualInfo);
            IntPtr visual = vi.visual;
            if (visual == IntPtr.Zero) throw new PlatformNotSupportedException("GLX visual has no X Visual");
            X11.XSetWindowAttributes attrs = new();
            attrs.colormap = X11.XCreateColormap(display, root, visual, X11.AllocNone);
            attrs.background_pixel = IntPtr.Zero;
            attrs.border_pixel = IntPtr.Zero;
            attrs.event_mask = X11.ExposureMask | X11.KeyPressMask | X11.KeyReleaseMask | X11.ButtonPressMask | X11.ButtonReleaseMask | X11.PointerMotionMask | X11.StructureNotifyMask | X11.FocusChangeMask;
            IntPtr window = X11.XCreateWindow(display, root, x, y, width, height, 0, vi.depth, X11.InputOutput, visual, X11.CWColormap | X11.CWEventMask | X11.CWBackPixel | X11.CWBorderPixel, ref attrs);
            if (window == IntPtr.Zero) throw new PlatformNotSupportedException("Failed to create X11 window");
            X11.XStoreName(display, window, title);
            X11.XMapWindow(display, window);
            LinuxInput.SetWindow(display, window);
            X11.XFlush(display);
            _windowInfo = new LinuxWindowInfo(window, display, IntPtr.Zero, visualInfo);
        }
        private void MoveWindow(int x, int y, int width, int height) { X11.XMoveResizeWindow(_windowInfo.DisplayHandle, _windowInfo.WindowHandle, x, y, width, height); X11.XFlush(_windowInfo.DisplayHandle); }
        public void CreateWindow(int width, int height, GraphicsMode mode, int major, int minor, GraphicsContextFlags flags, out IGraphicsContext context) => context = new LinuxGLContext(mode, _windowInfo, null, true, major, minor, flags);
        public void DestroyWindow() { if (_exists) { Closed?.Invoke(this, EventArgs.Empty); _exists = false; } }
        public void ProcessEvents()
        {
            IntPtr display = _windowInfo.DisplayHandle;
            while (X11.XPending(display) > 0)
            {
                X11.XNextEvent(display, _eventBuffer);
                int type = Marshal.ReadInt32(_eventBuffer, 0);
                if (type == X11.MapNotify)
                {
                    // The window is now viewable, so it is legal to take input focus.
                    if (!_focused)
                    {
                        _focused = true;
                        FocusedChanged?.Invoke(this, EventArgs.Empty);
                    }
                    X11.XSetInputFocus(display, _windowInfo.WindowHandle, 2 /* RevertToParent */, 0);
                    X11.XFlush(display);
                }
                else if (type == X11.FocusIn || type == X11.FocusOut)
                {
                    bool focused = type == X11.FocusIn;
                    if (focused != _focused)
                    {
                        _focused = focused;
                        FocusedChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
                else if (type == X11.ConfigureNotify)
                {
                    // The window manager resized/moved the window. Track it so the
                    // engine's viewport (and the mouse-look centre) follow the
                    // real drawable size instead of the size we asked for.
                    X11.XConfigureEvent ce = Marshal.PtrToStructure<X11.XConfigureEvent>(_eventBuffer);
                    bool resized = ce.width > 0 && ce.height > 0 && (ce.width != _width || ce.height != _height);
                    bool moved = ce.x != _x || ce.y != _y;
                    _x = ce.x; _y = ce.y;
                    if (resized) { _width = ce.width; _height = ce.height; }
                    if (resized) Resize?.Invoke(this, EventArgs.Empty);
                    else if (moved) Move?.Invoke(this, EventArgs.Empty);
                }
                LinuxInput.ProcessEvent(type, _eventBuffer);
            }
        }
        public void Close() => DestroyWindow(); public Point PointToClient(Point point) => point; public Point PointToScreen(Point point) => point;
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
        private void Dispose(bool manual) { if (!_disposed) { if (manual && _exists) DestroyWindow(); if (manual) { X11.XDestroyWindow(_windowInfo.DisplayHandle, _windowInfo.WindowHandle); X11.XCloseDisplay(_windowInfo.DisplayHandle); } if (_eventBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_eventBuffer); Disposed?.Invoke(this, EventArgs.Empty); _disposed = true; } }
        ~LinuxNativeWindow() { Dispose(false); }
    }
}
