using System;
using System.Runtime.InteropServices;

namespace OpenTK.Platform.Linux
{
    /// <summary>
    /// Minimal SDL2 binding used by the Linux platform layer.
    ///
    /// The SONAME is used directly so the game always talks to the CFW's own
    /// SDL2 build; SDL2 is never bundled with the port. No SDL_VIDEODRIVER or
    /// SDL_AUDIODRIVER is selected here, SDL picks the driver for the device.
    /// </summary>
    internal static class SDL2
    {
        public const string Lib = "libSDL2-2.0.so.0";

        // SDL_Init flags.
        public const uint SDL_INIT_TIMER = 0x00000001;
        public const uint SDL_INIT_AUDIO = 0x00000010;
        public const uint SDL_INIT_VIDEO = 0x00000020;
        public const uint SDL_INIT_JOYSTICK = 0x00000200;
        public const uint SDL_INIT_HAPTIC = 0x00001000;
        public const uint SDL_INIT_GAMECONTROLLER = 0x00002000;
        public const uint SDL_INIT_EVENTS = 0x00004000;

        // SDL_WindowFlags.
        public const uint SDL_WINDOW_FULLSCREEN = 0x00000001;
        public const uint SDL_WINDOW_OPENGL = 0x00000002;
        public const uint SDL_WINDOW_SHOWN = 0x00000004;
        public const uint SDL_WINDOW_HIDDEN = 0x00000008;
        public const uint SDL_WINDOW_BORDERLESS = 0x00000010;
        public const uint SDL_WINDOW_RESIZABLE = 0x00000020;
        public const uint SDL_WINDOW_INPUT_FOCUS = 0x00000200;
        public const uint SDL_WINDOW_MOUSE_FOCUS = 0x00000400;
        public const uint SDL_WINDOW_FULLSCREEN_DESKTOP = SDL_WINDOW_FULLSCREEN | 0x00001000;

        public const int SDL_WINDOWPOS_CENTERED = 0x2FFF0000;
        public const int SDL_WINDOWPOS_UNDEFINED = 0x1FFF0000;

        // SDL_GLattr.
        public const int SDL_GL_RED_SIZE = 0;
        public const int SDL_GL_GREEN_SIZE = 1;
        public const int SDL_GL_BLUE_SIZE = 2;
        public const int SDL_GL_ALPHA_SIZE = 3;
        public const int SDL_GL_BUFFER_SIZE = 4;
        public const int SDL_GL_DOUBLEBUFFER = 5;
        public const int SDL_GL_DEPTH_SIZE = 6;
        public const int SDL_GL_STENCIL_SIZE = 7;
        public const int SDL_GL_ACCUM_RED_SIZE = 8;
        public const int SDL_GL_ACCUM_GREEN_SIZE = 9;
        public const int SDL_GL_ACCUM_BLUE_SIZE = 10;
        public const int SDL_GL_ACCUM_ALPHA_SIZE = 11;
        public const int SDL_GL_CONTEXT_MAJOR_VERSION = 17;
        public const int SDL_GL_CONTEXT_MINOR_VERSION = 18;
        public const int SDL_GL_CONTEXT_EGL = 19;
        public const int SDL_GL_CONTEXT_FLAGS = 20;
        public const int SDL_GL_CONTEXT_PROFILE_MASK = 21;
        public const int SDL_GL_SHARE_WITH_CURRENT_CONTEXT = 22;
        public const int SDL_GL_FRAMEBUFFER_SRGB_CAPABLE = 23;

        public const int SDL_GL_CONTEXT_PROFILE_CORE = 0x0001;
        public const int SDL_GL_CONTEXT_PROFILE_COMPATIBILITY = 0x0002;
        public const int SDL_GL_CONTEXT_PROFILE_ES = 0x0004;
        public const int SDL_GL_CONTEXT_FLAG_DEBUG_BIT = 0x0001;
        public const int SDL_GL_CONTEXT_FLAG_FORWARD_COMPATIBLE_BIT = 0x0002;

        // Event types.
        public const uint SDL_QUIT = 0x100;
        public const uint SDL_WINDOWEVENT = 0x200;
        public const uint SDL_SYSWMEVENT = 0x201;
        public const uint SDL_KEYDOWN = 0x300;
        public const uint SDL_KEYUP = 0x301;
        public const uint SDL_TEXTEDITING = 0x302;
        public const uint SDL_TEXTINPUT = 0x303;
        public const uint SDL_MOUSEMOTION = 0x400;
        public const uint SDL_MOUSEBUTTONDOWN = 0x401;
        public const uint SDL_MOUSEBUTTONUP = 0x402;
        public const uint SDL_MOUSEWHEEL = 0x403;
        public const uint SDL_CONTROLLERAXISMOTION = 0x650;
        public const uint SDL_CONTROLLERBUTTONDOWN = 0x651;
        public const uint SDL_CONTROLLERBUTTONUP = 0x652;
        public const uint SDL_CONTROLLERDEVICEADDED = 0x653;
        public const uint SDL_CONTROLLERDEVICEREMOVED = 0x654;
        public const uint SDL_CONTROLLERDEVICEREMAPPED = 0x655;

        // SDL_WindowEventID.
        public const int SDL_WINDOWEVENT_SHOWN = 1;
        public const int SDL_WINDOWEVENT_HIDDEN = 2;
        public const int SDL_WINDOWEVENT_EXPOSED = 3;
        public const int SDL_WINDOWEVENT_MOVED = 4;
        public const int SDL_WINDOWEVENT_RESIZED = 5;
        public const int SDL_WINDOWEVENT_SIZE_CHANGED = 6;
        public const int SDL_WINDOWEVENT_MINIMIZED = 7;
        public const int SDL_WINDOWEVENT_MAXIMIZED = 8;
        public const int SDL_WINDOWEVENT_RESTORED = 9;
        public const int SDL_WINDOWEVENT_ENTER = 10;
        public const int SDL_WINDOWEVENT_LEAVE = 11;
        public const int SDL_WINDOWEVENT_FOCUS_GAINED = 12;
        public const int SDL_WINDOWEVENT_FOCUS_LOST = 13;
        public const int SDL_WINDOWEVENT_CLOSE = 14;

        // SDL_GameControllerAxis.
        public const int SDL_CONTROLLER_AXIS_LEFTX = 0;
        public const int SDL_CONTROLLER_AXIS_LEFTY = 1;
        public const int SDL_CONTROLLER_AXIS_RIGHTX = 2;
        public const int SDL_CONTROLLER_AXIS_RIGHTY = 3;
        public const int SDL_CONTROLLER_AXIS_TRIGGERLEFT = 4;
        public const int SDL_CONTROLLER_AXIS_TRIGGERRIGHT = 5;

        // SDL_GameControllerButton.
        public const int SDL_CONTROLLER_BUTTON_A = 0;
        public const int SDL_CONTROLLER_BUTTON_B = 1;
        public const int SDL_CONTROLLER_BUTTON_X = 2;
        public const int SDL_CONTROLLER_BUTTON_Y = 3;
        public const int SDL_CONTROLLER_BUTTON_BACK = 4;
        public const int SDL_CONTROLLER_BUTTON_GUIDE = 5;
        public const int SDL_CONTROLLER_BUTTON_START = 6;
        public const int SDL_CONTROLLER_BUTTON_LEFTSTICK = 7;
        public const int SDL_CONTROLLER_BUTTON_RIGHTSTICK = 8;
        public const int SDL_CONTROLLER_BUTTON_LEFTSHOULDER = 9;
        public const int SDL_CONTROLLER_BUTTON_RIGHTSHOULDER = 10;
        public const int SDL_CONTROLLER_BUTTON_DPAD_UP = 11;
        public const int SDL_CONTROLLER_BUTTON_DPAD_DOWN = 12;
        public const int SDL_CONTROLLER_BUTTON_DPAD_LEFT = 13;
        public const int SDL_CONTROLLER_BUTTON_DPAD_RIGHT = 14;

        public const int SDL_MOUSEWHEEL_NORMAL = 0;
        public const int SDL_MOUSEWHEEL_FLIPPED = 1;

        public const int SDL_BUTTON_LEFT = 1;
        public const int SDL_BUTTON_MIDDLE = 2;
        public const int SDL_BUTTON_RIGHT = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_Rect
        {
            public int x;
            public int y;
            public int w;
            public int h;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_DisplayMode
        {
            public uint format;
            public int w;
            public int h;
            public int refresh_rate;
            public IntPtr driverdata;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_version
        {
            public byte major;
            public byte minor;
            public byte patch;
        }

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetVersion", ExactSpelling = true)]
        public static extern void GetVersion(out SDL_version version);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_Init", ExactSpelling = true)]
        public static extern int Init(uint flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_InitSubSystem", ExactSpelling = true)]
        public static extern int InitSubSystem(uint flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_WasInit", ExactSpelling = true)]
        public static extern uint WasInit(uint flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_QuitSubSystem", ExactSpelling = true)]
        public static extern void QuitSubSystem(uint flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_SetHint", ExactSpelling = true)]
        public static extern int SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetError", ExactSpelling = true)]
        private static extern IntPtr SDL_GetErrorNative();

        public static string GetError() => Marshal.PtrToStringAnsi(SDL_GetErrorNative()) ?? "unknown SDL2 error";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetCurrentVideoDriver", ExactSpelling = true)]
        private static extern IntPtr SDL_GetCurrentVideoDriverNative();

        public static string GetCurrentVideoDriver() => Marshal.PtrToStringAnsi(SDL_GetCurrentVideoDriverNative()) ?? "";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_CreateWindow", ExactSpelling = true)]
        public static extern IntPtr CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int x, int y, int w, int h, uint flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_DestroyWindow", ExactSpelling = true)]
        public static extern void DestroyWindow(IntPtr window);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_ShowWindow", ExactSpelling = true)]
        public static extern void ShowWindow(IntPtr window);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_HideWindow", ExactSpelling = true)]
        public static extern void HideWindow(IntPtr window);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_RaiseWindow", ExactSpelling = true)]
        public static extern void RaiseWindow(IntPtr window);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_SetWindowTitle", ExactSpelling = true)]
        public static extern void SetWindowTitle(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_SetWindowFullscreen", ExactSpelling = true)]
        public static extern int SetWindowFullscreen(IntPtr window, uint flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_SetWindowSize", ExactSpelling = true)]
        public static extern void SetWindowSize(IntPtr window, int w, int h);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_SetWindowPosition", ExactSpelling = true)]
        public static extern void SetWindowPosition(IntPtr window, int x, int y);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetWindowSize", ExactSpelling = true)]
        public static extern void GetWindowSize(IntPtr window, out int w, out int h);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetWindowPosition", ExactSpelling = true)]
        public static extern void GetWindowPosition(IntPtr window, out int x, out int y);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetWindowFlags", ExactSpelling = true)]
        public static extern uint GetWindowFlags(IntPtr window);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetWindowDisplayIndex", ExactSpelling = true)]
        public static extern int GetWindowDisplayIndex(IntPtr window);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetNumVideoDisplays", ExactSpelling = true)]
        public static extern int GetNumVideoDisplays();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetDesktopDisplayMode", ExactSpelling = true)]
        public static extern int GetDesktopDisplayMode(int displayIndex, out SDL_DisplayMode mode);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetCurrentDisplayMode", ExactSpelling = true)]
        public static extern int GetCurrentDisplayMode(int displayIndex, out SDL_DisplayMode mode);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetDisplayBounds", ExactSpelling = true)]
        public static extern int GetDisplayBounds(int displayIndex, out SDL_Rect rect);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_SetAttribute", ExactSpelling = true)]
        public static extern int GL_SetAttribute(int attr, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_GetAttribute", ExactSpelling = true)]
        public static extern int GL_GetAttribute(int attr, out int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_LogSetAllPriority", ExactSpelling = true)]
        public static extern void LogSetAllPriority(int priority);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_CreateContext", ExactSpelling = true)]
        public static extern IntPtr GL_CreateContext(IntPtr window);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_DeleteContext", ExactSpelling = true)]
        public static extern void GL_DeleteContext(IntPtr context);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_MakeCurrent", ExactSpelling = true)]
        public static extern int GL_MakeCurrent(IntPtr window, IntPtr context);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_SetSwapInterval", ExactSpelling = true)]
        public static extern int GL_SetSwapInterval(int interval);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_SwapWindow", ExactSpelling = true)]
        public static extern void GL_SwapWindow(IntPtr window);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_GetCurrentContext", ExactSpelling = true)]
        public static extern IntPtr GL_GetCurrentContext();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_GetCurrentWindow", ExactSpelling = true)]
        public static extern IntPtr GL_GetCurrentWindow();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_GetProcAddress", ExactSpelling = true)]
        public static extern IntPtr GL_GetProcAddress([MarshalAs(UnmanagedType.LPUTF8Str)] string proc);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GL_GetDrawableSize", ExactSpelling = true)]
        public static extern void GL_GetDrawableSize(IntPtr window, out int w, out int h);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_PollEvent", ExactSpelling = true)]
        public static extern int PollEvent(IntPtr ev);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_StartTextInput", ExactSpelling = true)]
        public static extern void StartTextInput();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_StopTextInput", ExactSpelling = true)]
        public static extern void StopTextInput();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_ShowCursor", ExactSpelling = true)]
        public static extern int ShowCursor(int toggle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_SetRelativeMouseMode", ExactSpelling = true)]
        public static extern int SetRelativeMouseMode(int enabled);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_WarpMouseInWindow", ExactSpelling = true)]
        public static extern void WarpMouseInWindow(IntPtr window, int x, int y);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_NumJoysticks", ExactSpelling = true)]
        public static extern int NumJoysticks();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_IsGameController", ExactSpelling = true)]
        public static extern int IsGameController(int joystickIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GameControllerOpen", ExactSpelling = true)]
        public static extern IntPtr GameControllerOpen(int joystickIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GameControllerClose", ExactSpelling = true)]
        public static extern void GameControllerClose(IntPtr controller);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GameControllerGetJoystick", ExactSpelling = true)]
        public static extern IntPtr GameControllerGetJoystick(IntPtr controller);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_JoystickInstanceID", ExactSpelling = true)]
        public static extern int JoystickInstanceID(IntPtr joystick);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_JoystickGetDeviceInstanceID", ExactSpelling = true)]
        public static extern int JoystickGetDeviceInstanceID(int deviceIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_JoystickUpdate", ExactSpelling = true)]
        public static extern void JoystickUpdate();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GameControllerGetAttached", ExactSpelling = true)]
        public static extern int GameControllerGetAttached(IntPtr controller);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GameControllerGetAxis", ExactSpelling = true)]
        public static extern short GameControllerGetAxis(IntPtr controller, int axis);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GameControllerGetButton", ExactSpelling = true)]
        public static extern byte GameControllerGetButton(IntPtr controller, int button);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GameControllerName", ExactSpelling = true)]
        private static extern IntPtr SDL_GameControllerNameNative(IntPtr controller);

        public static string GameControllerName(IntPtr controller) => Marshal.PtrToStringAnsi(SDL_GameControllerNameNative(controller)) ?? "";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GameControllerEventState", ExactSpelling = true)]
        public static extern int GameControllerEventState(int state);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_JoystickEventState", ExactSpelling = true)]
        public static extern int JoystickEventState(int state);
    }
}
