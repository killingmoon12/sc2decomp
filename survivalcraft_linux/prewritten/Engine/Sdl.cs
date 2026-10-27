using System;
using System.Runtime.InteropServices;

namespace Engine;

/// <summary>
/// Minimal SDL2 bindings used by <see cref="Window"/>. Only the pieces the
/// desktop port needs are declared here: video/window management, the OpenGL
/// context and clipboard access. Text/input events are polled through
/// <see cref="SDL_PollEvent"/> and decoded in <c>Window</c>.
/// </summary>
internal static class Sdl
{
	private const string Lib = "libSDL2-2.0.so.0";

	// SDL_Init flags
	public const uint SDL_INIT_VIDEO = 0x00000020u;
	public const uint SDL_INIT_EVENTS = 0x00004000u;

	// SDL_WindowFlags
	public const uint SDL_WINDOW_FULLSCREEN = 0x00000001u;
	public const uint SDL_WINDOW_OPENGL = 0x00000002u;
	public const uint SDL_WINDOW_SHOWN = 0x00000004u;
	public const uint SDL_WINDOW_BORDERLESS = 0x00000010u;
	public const uint SDL_WINDOW_RESIZABLE = 0x00000020u;
	public const uint SDL_WINDOW_FULLSCREEN_DESKTOP = 0x00001001u;

	// SDL_GLattr
	public const int SDL_GL_RED_SIZE = 0;
	public const int SDL_GL_GREEN_SIZE = 1;
	public const int SDL_GL_BLUE_SIZE = 2;
	public const int SDL_GL_ALPHA_SIZE = 3;
	public const int SDL_GL_DOUBLEBUFFER = 5;
	public const int SDL_GL_DEPTH_SIZE = 6;
	public const int SDL_GL_STENCIL_SIZE = 7;

	// SDL_EventType
	public const uint SDL_QUIT = 0x100u;
	public const uint SDL_WINDOWEVENT = 0x200u;
	public const uint SDL_KEYDOWN = 0x300u;
	public const uint SDL_KEYUP = 0x301u;
	public const uint SDL_TEXTINPUT = 0x303u;
	public const uint SDL_MOUSEMOTION = 0x400u;
	public const uint SDL_MOUSEBUTTONDOWN = 0x401u;
	public const uint SDL_MOUSEBUTTONUP = 0x402u;
	public const uint SDL_MOUSEWHEEL = 0x403u;

	// SDL_WindowEventID
	public const byte SDL_WINDOWEVENT_SHOWN = 1;
	public const byte SDL_WINDOWEVENT_HIDDEN = 2;
	public const byte SDL_WINDOWEVENT_EXPOSED = 3;
	public const byte SDL_WINDOWEVENT_MOVED = 4;
	public const byte SDL_WINDOWEVENT_RESIZED = 5;
	public const byte SDL_WINDOWEVENT_SIZE_CHANGED = 6;
	public const byte SDL_WINDOWEVENT_MINIMIZED = 7;
	public const byte SDL_WINDOWEVENT_MAXIMIZED = 8;
	public const byte SDL_WINDOWEVENT_RESTORED = 9;
	public const byte SDL_WINDOWEVENT_ENTER = 10;
	public const byte SDL_WINDOWEVENT_LEAVE = 11;
	public const byte SDL_WINDOWEVENT_FOCUS_GAINED = 12;
	public const byte SDL_WINDOWEVENT_FOCUS_LOST = 13;
	public const byte SDL_WINDOWEVENT_CLOSE = 14;

	// SDL_Event field offsets (SDL2 SDL_Event is a 56-byte union).
	public const int EventTypeOffset = 0;
	public const int KeyRepeatOffset = 13;
	public const int KeySymOffset = 20;
	public const int TextInputOffset = 12;
	public const int MouseXOffset = 20;
	public const int MouseYOffset = 24;
	public const int MouseXRelOffset = 28;
	public const int MouseYRelOffset = 32;
	public const int MouseButtonOffset = 16;
	public const int MouseWheelPreciseYOffset = 32;
	public const int WindowEventOffset = 12;

	[StructLayout(LayoutKind.Sequential)]
	public struct SDL_DisplayMode
	{
		public uint format;
		public int w;
		public int h;
		public int refresh_rate;
		public IntPtr driverdata;
	}

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_Init(uint flags);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_Quit();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int x, int y, int w, int h, uint flags);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_DestroyWindow(IntPtr window);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr SDL_GetError();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_GL_SetAttribute(int attr, int value);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr SDL_GL_CreateContext(IntPtr window);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_GL_MakeCurrent(IntPtr window, IntPtr context);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_GL_SwapWindow(IntPtr window);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_GL_SetSwapInterval(int interval);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_GL_DeleteContext(IntPtr context);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_SetWindowTitle(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_SetWindowSize(IntPtr window, int w, int h);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_SetWindowPosition(IntPtr window, int x, int y);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_GetWindowSize(IntPtr window, out int w, out int h);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_GetWindowPosition(IntPtr window, out int x, out int y);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_SetWindowBordered(IntPtr window, int bordered);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_SetWindowResizable(IntPtr window, int resizable);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_SetWindowFullscreen(IntPtr window, uint flags);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_GetWindowDisplayIndex(IntPtr window);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_GetDesktopDisplayMode(int displayIndex, out SDL_DisplayMode mode);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_PollEvent(IntPtr ev);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_ShowCursor(int toggle);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_WarpMouseInWindow(IntPtr window, int x, int y);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_SetRelativeMouseMode(int enabled);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_StartTextInput();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_StopTextInput();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr SDL_GetClipboardText();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int SDL_SetClipboardText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void SDL_free(IntPtr mem);

	private static bool s_initialized;

	public static bool IsInitialized => s_initialized;

	public static void Initialize()
	{
		if (s_initialized) return;
		if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_EVENTS) != 0)
		{
			throw new InvalidOperationException("SDL_Init failed: " + GetErrorString());
		}
		s_initialized = true;
	}

	public static void Shutdown()
	{
		if (!s_initialized) return;
		SDL_Quit();
		s_initialized = false;
	}

	public static string GetErrorString()
	{
		IntPtr error = SDL_GetError();
		return error == IntPtr.Zero ? "" : (Marshal.PtrToStringUTF8(error) ?? "");
	}

	public static Point2 GetDesktopSize(int displayIndex)
	{
		if (!s_initialized) return new Point2(1280, 720);
		if (SDL_GetDesktopDisplayMode(displayIndex, out SDL_DisplayMode mode) == 0 && mode.w > 0 && mode.h > 0)
		{
			return new Point2(mode.w, mode.h);
		}
		return new Point2(1280, 720);
	}

	public static string GetClipboardText()
	{
		IntPtr text = SDL_GetClipboardText();
		if (text == IntPtr.Zero) return string.Empty;
		try
		{
			return Marshal.PtrToStringUTF8(text) ?? string.Empty;
		}
		finally
		{
			SDL_free(text);
		}
	}
}
