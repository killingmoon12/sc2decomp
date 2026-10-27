using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenTK.Graphics;
using OpenTK.Input;
using OpenTK.Platform;

namespace OpenTK.Platform.Linux
{
    /// <summary>
    /// SDL2 window. The display is always fullscreen desktop; the game's
    /// requested width/height are only a hint and the real mode comes from the
    /// device, including on KMS/DRM where there is no window manager.
    /// </summary>
    internal sealed class LinuxNativeWindow : INativeWindow
    {
        private readonly IntPtr _eventBuffer = Marshal.AllocHGlobal(64);
        private LinuxWindowInfo _windowInfo;
        private IntPtr _window;
        private bool _exists = true;
        private bool _disposed;
        private int _x;
        private int _y;
        private int _width;
        private int _height;
        private string _title;
        private bool _visible = true;
        private bool _focused = true;
        private WindowState _windowState = WindowState.Fullscreen;
        private WindowBorder _windowBorder = WindowBorder.Resizable;

        private readonly struct GLRequest
        {
            public GLRequest(int major, int minor, int red, int green, int blue, int alpha, int depth, int stencil, bool fullscreen, bool desktopFullscreen)
            {
                Major = major;
                Minor = minor;
                Red = red;
                Green = green;
                Blue = blue;
                Alpha = alpha;
                Depth = depth;
                Stencil = stencil;
                Fullscreen = fullscreen;
                DesktopFullscreen = desktopFullscreen;
            }

            public int Major { get; }
            public int Minor { get; }
            public int Red { get; }
            public int Green { get; }
            public int Blue { get; }
            public int Alpha { get; }
            public int Depth { get; }
            public int Stencil { get; }
            public bool Fullscreen { get; }
            public bool DesktopFullscreen { get; }

            public string Label
            {
                get
                {
                    string mode = !Fullscreen ? "windowed" : DesktopFullscreen ? "desktop-fullscreen" : "fullscreen";
                    return $"ES{Major}.{Minor} R{Red}G{Green}B{Blue} A{Alpha} D{Depth} S{Stencil} {mode}";
                }
            }
        }

        // The R36S Mali stack exposes EGL/GLES2 through fbdev, while SDL's
        // KMS/DRM path can also use an ARGB8888 or XRGB8888 GBM surface
        // depending on its build. Try SDL's default alpha-free config first,
        // then the alpha channel the newer GBM path expects, and only then
        // higher quality or desktop-fullscreen variants. Plain fullscreen is
        // listed first because some device SDL builds reject desktop
        // fullscreen before they ever reach EGL config selection.
        private static readonly GLRequest[] WindowRequests =
        {
            // Plain fullscreen at the panel's native mode.
            new GLRequest(2, 0, 8, 8, 8, 0, 16, 0, true, false),
            new GLRequest(2, 0, 8, 8, 8, 8, 16, 0, true, false),
            new GLRequest(2, 0, 3, 3, 2, 0, 16, 0, true, false),
            new GLRequest(2, 0, 8, 8, 8, 0, 24, 0, true, false),
            new GLRequest(2, 0, 8, 8, 8, 8, 24, 8, true, false),
            // Desktop-fullscreen variants.
            new GLRequest(2, 0, 8, 8, 8, 0, 16, 0, true, true),
            new GLRequest(2, 0, 8, 8, 8, 8, 16, 0, true, true),
            new GLRequest(2, 0, 3, 3, 2, 0, 16, 0, true, true),
            new GLRequest(2, 0, 3, 3, 2, 8, 16, 0, true, true),
            // Some Mali EGL builds expose no depth buffer in their native
            // configs. Fall back to depth 0 rather than failing to open a
            // window at all.
            new GLRequest(2, 0, 8, 8, 8, 0, 0, 0, true, false),
            new GLRequest(2, 0, 8, 8, 8, 8, 0, 0, true, false),
            new GLRequest(3, 0, 8, 8, 8, 0, 0, 0, true, false),
            new GLRequest(2, 0, 8, 8, 8, 0, 0, 0, true, true),
            // ES3 fallbacks.
            new GLRequest(3, 0, 8, 8, 8, 0, 16, 0, true, false),
            new GLRequest(3, 0, 8, 8, 8, 8, 16, 0, true, false),
            // Windowed fallback.
            new GLRequest(2, 0, 8, 8, 8, 0, 16, 0, false, false),
            new GLRequest(3, 0, 8, 8, 8, 0, 16, 0, false, false),
        };

        public bool Exists => _exists;
        public IWindowInfo WindowInfo => _windowInfo;
        public bool Focused => _focused;
        public IInputDriver InputDriver => null;

        public string Title
        {
            get => _title;
            set
            {
                _title = value ?? string.Empty;
                if (_window != IntPtr.Zero) SDL2.SetWindowTitle(_window, _title);
                TitleChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (_window != IntPtr.Zero)
                {
                    if (value) SDL2.ShowWindow(_window);
                    else SDL2.HideWindow(_window);
                }
                VisibleChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public WindowState WindowState
        {
            get => _windowState;
            set
            {
                if (_windowState == value) return;
                _windowState = value;
                if (_window != IntPtr.Zero)
                {
                    uint flags = value == WindowState.Fullscreen ? SDL2.SDL_WINDOW_FULLSCREEN_DESKTOP : 0;
                    SDL2.SetWindowFullscreen(_window, flags);
                    RefreshGeometry();
                }
                WindowStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public WindowBorder WindowBorder
        {
            get => _windowBorder;
            set
            {
                if (_windowBorder == value) return;
                _windowBorder = value;
                WindowBorderChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public Rectangle Bounds
        {
            get => new Rectangle(_x, _y, _width, _height);
            set
            {
                _x = value.X;
                _y = value.Y;
                _width = Math.Max(1, value.Width);
                _height = Math.Max(1, value.Height);
                ApplyGeometry();
                Resize?.Invoke(this, EventArgs.Empty);
            }
        }

        public Point Location
        {
            get => new Point(_x, _y);
            set
            {
                _x = value.X;
                _y = value.Y;
                ApplyPosition();
                Move?.Invoke(this, EventArgs.Empty);
            }
        }

        public Size Size
        {
            get => new Size(_width, _height);
            set
            {
                _width = Math.Max(1, value.Width);
                _height = Math.Max(1, value.Height);
                ApplyGeometry();
                Resize?.Invoke(this, EventArgs.Empty);
            }
        }

        public int X
        {
            get => _x;
            set
            {
                _x = value;
                ApplyPosition();
                Move?.Invoke(this, EventArgs.Empty);
            }
        }

        public int Y
        {
            get => _y;
            set
            {
                _y = value;
                ApplyPosition();
                Move?.Invoke(this, EventArgs.Empty);
            }
        }

        public int Width
        {
            get => _width;
            set => Size = new Size(value, _height);
        }

        public int Height
        {
            get => _height;
            set => Size = new Size(_width, value);
        }

        public Rectangle ClientRectangle
        {
            get => new Rectangle(0, 0, _width, _height);
            set { }
        }

        public Size ClientSize
        {
            get => new Size(_width, _height);
            set { }
        }

        public event EventHandler<EventArgs> Move;
        public event EventHandler<EventArgs> Resize;
        public event EventHandler<CancelEventArgs> Closing;
        public event EventHandler<EventArgs> Closed;
        public event EventHandler<EventArgs> Disposed;
        public event EventHandler<EventArgs> TitleChanged;
        public event EventHandler<EventArgs> VisibleChanged;
        public event EventHandler<EventArgs> FocusedChanged;
        public event EventHandler<EventArgs> WindowBorderChanged;
        public event EventHandler<EventArgs> WindowStateChanged;
        public event EventHandler<KeyPressEventArgs> KeyPress;
        public event EventHandler<EventArgs> MouseLeave;
        public event EventHandler<EventArgs> MouseEnter;

        public LinuxNativeWindow(int x, int y, int width, int height, string title, GraphicsMode mode, GameWindowFlags options, DisplayDevice device)
        {
            _x = 0;
            _y = 0;
            _title = title ?? string.Empty;

            InitializeSDL();

            // SDL needs these before window creation. The engine asks for a
            // desktop GL 3.3 context but only uses GLES2 functions, so request
            // an ES2 context. The R36S panel is 640x480, so use the panel mode
            // rather than the engine's 1280x720 hint for fullscreen attempts.
            uint baseFlags = SDL2.SDL_WINDOW_OPENGL | SDL2.SDL_WINDOW_SHOWN;
            int windowWidth = Math.Max(1, width);
            int windowHeight = Math.Max(1, height);
            GLRequest selected = default;
            string lastError = "unknown SDL2 error";
            bool selectedWindowed = false;

            SDL2.GetVersion(out SDL2.SDL_version sdlVersion);
            string display = "unknown";
            if (SDL2.GetCurrentDisplayMode(0, out SDL2.SDL_DisplayMode displayMode) == 0)
            {
                display = $"{displayMode.w}x{displayMode.h}@{displayMode.refresh_rate}";
                if (displayMode.w > 0 && displayMode.h > 0)
                {
                    windowWidth = displayMode.w;
                    windowHeight = displayMode.h;
                }
            }
            Console.Error.WriteLine(
                $"[SDL] driver={SDL2.GetCurrentVideoDriver()} version={sdlVersion.major}.{sdlVersion.minor}.{sdlVersion.patch} display={display} window={windowWidth}x{windowHeight}");

            for (int i = 0; i < WindowRequests.Length; i++)
            {
                GLRequest request = WindowRequests[i];
                SDL2.GL_SetAttribute(SDL2.SDL_GL_CONTEXT_MAJOR_VERSION, request.Major);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_CONTEXT_MINOR_VERSION, request.Minor);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_CONTEXT_EGL, 1);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_CONTEXT_PROFILE_MASK, SDL2.SDL_GL_CONTEXT_PROFILE_ES);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_CONTEXT_FLAGS, 0);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_DOUBLEBUFFER, 1);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_RED_SIZE, request.Red);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_GREEN_SIZE, request.Green);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_BLUE_SIZE, request.Blue);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_ALPHA_SIZE, request.Alpha);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_DEPTH_SIZE, request.Depth);
                SDL2.GL_SetAttribute(SDL2.SDL_GL_STENCIL_SIZE, request.Stencil);
                if (i == 0)
                {
                    SDL2.GL_GetAttribute(SDL2.SDL_GL_CONTEXT_PROFILE_MASK, out int profileMask);
                    SDL2.GL_GetAttribute(SDL2.SDL_GL_CONTEXT_EGL, out int eglProfile);
                    Console.Error.WriteLine($"[SDL] GL profile mask={profileMask} egl={eglProfile}");
                }

                uint flags = baseFlags;
                if (request.Fullscreen)
                {
                    flags |= request.DesktopFullscreen
                        ? SDL2.SDL_WINDOW_FULLSCREEN_DESKTOP
                        : SDL2.SDL_WINDOW_FULLSCREEN;
                }

                Console.Error.WriteLine(
                    $"[SDL] Window config {i + 1}/{WindowRequests.Length}: {request.Label} at {windowWidth}x{windowHeight}");
                _window = SDL2.CreateWindow(_title, SDL2.SDL_WINDOWPOS_CENTERED, SDL2.SDL_WINDOWPOS_CENTERED,
                    windowWidth, windowHeight, flags);
                if (_window != IntPtr.Zero)
                {
                    selected = request;
                    selectedWindowed = !request.Fullscreen;
                    break;
                }

                lastError = SDL2.GetError();
                Console.Error.WriteLine($"[SDL] Window attempt {i + 1}/{WindowRequests.Length} ({request.Label}) failed: {lastError}");
            }

            if (_window == IntPtr.Zero)
            {
                throw new PlatformNotSupportedException(
                    $"Failed to create SDL2 window after {WindowRequests.Length} attempts: {lastError}");
            }

            if (selectedWindowed && SDL2.SetWindowFullscreen(_window, SDL2.SDL_WINDOW_FULLSCREEN_DESKTOP) != 0)
            {
                _windowState = WindowState.Normal;
            }

            SDL2.RaiseWindow(_window);
            SDL2.ShowWindow(_window);
            SDL2.ShowCursor(0);
            SDL2.StartTextInput();

            RefreshGeometry();
            if (_width <= 1 || _height <= 1)
            {
                _width = Math.Max(1, width);
                _height = Math.Max(1, height);
            }

            Console.Error.WriteLine($"[SDL] Window created with {selected.Label}, drawable {_width}x{_height}");
            _windowInfo = new LinuxWindowInfo(_window, selected.Major, selected.Minor);
            LinuxInput.SetWindow(_window, _width, _height);
            LinuxInput.OpenGameControllers();
            LinuxInput.OnKeyPress += HandleTextInput;
        }

        private static void InitializeSDL()
        {
            const uint flags = SDL2.SDL_INIT_VIDEO | SDL2.SDL_INIT_EVENTS | SDL2.SDL_INIT_JOYSTICK | SDL2.SDL_INIT_GAMECONTROLLER;
            if ((SDL2.WasInit(0) & flags) == flags) return;
            SDL2.LogSetAllPriority(3);
            if (SDL2.Init(flags) != 0)
            {
                throw new PlatformNotSupportedException("Failed to initialize SDL2: " + SDL2.GetError());
            }
        }

        private void HandleTextInput(string text)
        {
            foreach (char c in text)
            {
                KeyPress?.Invoke(this, new KeyPressEventArgs(c));
            }
        }

        private void RefreshGeometry()
        {
            if (_window == IntPtr.Zero) return;
            SDL2.GetWindowSize(_window, out int width, out int height);
            SDL2.GetWindowPosition(_window, out int x, out int y);
            if (width > 0) _width = width;
            if (height > 0) _height = height;
            _x = x;
            _y = y;
            LinuxInput.SetWindowSize(_width, _height);
        }

        private void ApplyGeometry()
        {
            if (_window == IntPtr.Zero || _windowState == WindowState.Fullscreen) return;
            SDL2.SetWindowPosition(_window, _x, _y);
            SDL2.SetWindowSize(_window, _width, _height);
            RefreshGeometry();
        }

        private void ApplyPosition()
        {
            if (_window == IntPtr.Zero || _windowState == WindowState.Fullscreen) return;
            SDL2.SetWindowPosition(_window, _x, _y);
        }

        private bool RequestClose()
        {
            if (!_exists) return false;
            CancelEventArgs args = new CancelEventArgs();
            Closing?.Invoke(this, args);
            if (args.Cancel) return false;
            _exists = false;
            Closed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void ProcessEvents()
        {
            while (SDL2.PollEvent(_eventBuffer) != 0)
            {
                uint type = (uint)Marshal.ReadInt32(_eventBuffer, 0);
                if (type == SDL2.SDL_QUIT)
                {
                    RequestClose();
                }
                else if (type == SDL2.SDL_WINDOWEVENT)
                {
                    int windowEvent = Marshal.ReadByte(_eventBuffer, 12);
                    if (windowEvent == SDL2.SDL_WINDOWEVENT_CLOSE)
                    {
                        RequestClose();
                    }
                    else if (windowEvent == SDL2.SDL_WINDOWEVENT_SIZE_CHANGED || windowEvent == SDL2.SDL_WINDOWEVENT_RESIZED)
                    {
                        SDL2.GetWindowSize(_window, out int width, out int height);
                        if (width > 0 && height > 0 && (width != _width || height != _height))
                        {
                            _width = width;
                            _height = height;
                            LinuxInput.SetWindowSize(_width, _height);
                            Resize?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    else if (windowEvent == SDL2.SDL_WINDOWEVENT_MOVED)
                    {
                        SDL2.GetWindowPosition(_window, out int x, out int y);
                        if (x != _x || y != _y)
                        {
                            _x = x;
                            _y = y;
                            Move?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    else if (windowEvent == SDL2.SDL_WINDOWEVENT_FOCUS_GAINED)
                    {
                        SetFocused(true);
                    }
                    else if (windowEvent == SDL2.SDL_WINDOWEVENT_FOCUS_LOST)
                    {
                        SetFocused(false);
                    }
                    else if (windowEvent == SDL2.SDL_WINDOWEVENT_SHOWN)
                    {
                        _visible = true;
                    }
                    else if (windowEvent == SDL2.SDL_WINDOWEVENT_HIDDEN)
                    {
                        _visible = false;
                    }
                }
                else
                {
                    LinuxInput.ProcessEvent(type, _eventBuffer);
                }
            }
        }

        private void SetFocused(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            if (!focused) LinuxInput.ResetInput();
            FocusedChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Close()
        {
            RequestClose();
        }

        public Point PointToClient(Point point) => point;
        public Point PointToScreen(Point point) => point;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool manual)
        {
            if (_disposed) return;
            if (manual)
            {
                LinuxInput.OnKeyPress -= HandleTextInput;
                LinuxInput.Shutdown();
                SDL2.StopTextInput();
                if (_window != IntPtr.Zero)
                {
                    SDL2.DestroyWindow(_window);
                    _window = IntPtr.Zero;
                }
                _exists = false;
                Disposed?.Invoke(this, EventArgs.Empty);
            }
            if (_eventBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_eventBuffer);
            _disposed = true;
        }

        ~LinuxNativeWindow() => Dispose(false);
    }
}
