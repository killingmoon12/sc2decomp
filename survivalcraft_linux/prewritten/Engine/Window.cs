using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Engine.Audio;
using Engine.Graphics;
using Engine.Input;
using Engine.Media;

namespace Engine;

/// <summary>
/// Desktop window, OpenGL context and input pump. Replaces the Android
/// Activity/View lifecycle with SDL2; the engine itself is unchanged.
/// </summary>
public static class Window
{
	private enum State { Uncreated, Inactive, Active }

	private const int SDL_WINDOWPOS_CENTERED = 0x2FFF0000;

	private static bool m_focusRegained;
	private static int m_presentationInterval = 1;
	private static double m_frameStartTime;
	private static float m_scale = 1f;
	private static State m_state = State.Uncreated;
	private static bool m_pointerCaptured;
	private static bool m_relativeMouse;
	private static bool m_focused = true;
	private static bool m_running;
	private static string m_title = "";
	private static WindowMode m_windowMode = WindowMode.Resizable;
	private static Point2 m_size = new Point2(1280, 720);
	private static Point2 m_position = Point2.Zero;
	private static IntPtr m_window = IntPtr.Zero;
	private static IntPtr m_glContext = IntPtr.Zero;
	private static IntPtr m_eventBuffer = IntPtr.Zero;

	public static Point2 ScreenSize
	{
		get
		{
			if (m_window != IntPtr.Zero)
			{
				int index = Sdl.SDL_GetWindowDisplayIndex(m_window);
				if (index >= 0)
				{
					Point2 size = Sdl.GetDesktopSize(index);
					if (size.X > 0 && size.Y > 0) return size;
				}
			}
			return Sdl.GetDesktopSize(0);
		}
	}

	public static WindowMode WindowMode
	{
		get { VerifyWindowOpened(); return m_windowMode; }
		set { VerifyWindowOpened(); ApplyWindowMode(value); }
	}

	public static Point2 Position
	{
		get { VerifyWindowOpened(); return m_position; }
		set
		{
			VerifyWindowOpened();
			m_position = value;
			Sdl.SDL_SetWindowPosition(m_window, value.X, value.Y);
		}
	}

	public static Point2 Size
	{
		get { VerifyWindowOpened(); return m_size; }
		set
		{
			VerifyWindowOpened();
			if (value.X <= 0 || value.Y <= 0) return;
			m_size = value;
			Sdl.SDL_SetWindowSize(m_window, value.X, value.Y);
			Display.Resize();
			Resized?.Invoke();
		}
	}

	public static float Scale { get { VerifyWindowOpened(); return m_scale; } }

	public static string Title
	{
		get { VerifyWindowOpened(); return m_title; }
		set
		{
			VerifyWindowOpened();
			m_title = value ?? "";
			Sdl.SDL_SetWindowTitle(m_window, m_title);
		}
	}

	public static Image Icon { get { VerifyWindowOpened(); return null; } set { VerifyWindowOpened(); } }

	public static int PresentationInterval
	{
		get { VerifyWindowOpened(); return m_presentationInterval; }
		set { VerifyWindowOpened(); m_presentationInterval = MathUtils.Clamp(value, 1, 4); }
	}

	public static string ClipboardString
	{
		get { VerifyWindowOpened(); return Sdl.GetClipboardText(); }
		set { VerifyWindowOpened(); Sdl.SDL_SetClipboardText(value ?? ""); }
	}

	public static bool IsCreated => m_state != State.Uncreated;

	public static bool IsActive => m_state == State.Active;

	public static event Action Created;
	public static event Action Resized;
	public static event Action Activated;
	public static event Action Deactivated;
	public static event Action Closed;
	public static event Action BeforeFrame;
	public static event Action Frame;
	public static event Action AfterFrame;
	public static event Action<UnhandledExceptionInfo> UnhandledException;
	public static event Action<Uri> HandleUri;
	public static event Action LowMemory;

	public static void Run(int width = 0, int height = 0, WindowMode windowMode = WindowMode.Fullscreen, string title = "")
	{
		if (m_window != IntPtr.Zero) throw new InvalidOperationException("Window is already opened.");

		AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
		{
			if (UnhandledException != null)
			{
				Exception ex = args.ExceptionObject as Exception ?? new Exception($"Unknown exception: {args.ExceptionObject}");
				UnhandledException(new UnhandledExceptionInfo(ex));
			}
		};

		if (width <= 0) width = 1280;
		if (height <= 0) height = 720;
		m_title = title ?? "";
		m_windowMode = windowMode;
		m_size = new Point2(width, height);

		Sdl.Initialize();

		Sdl.SDL_GL_SetAttribute(Sdl.SDL_GL_RED_SIZE, 8);
		Sdl.SDL_GL_SetAttribute(Sdl.SDL_GL_GREEN_SIZE, 8);
		Sdl.SDL_GL_SetAttribute(Sdl.SDL_GL_BLUE_SIZE, 8);
		Sdl.SDL_GL_SetAttribute(Sdl.SDL_GL_ALPHA_SIZE, 8);
		Sdl.SDL_GL_SetAttribute(Sdl.SDL_GL_DOUBLEBUFFER, 1);
		Sdl.SDL_GL_SetAttribute(Sdl.SDL_GL_DEPTH_SIZE, 24);
		Sdl.SDL_GL_SetAttribute(Sdl.SDL_GL_STENCIL_SIZE, 8);

		uint flags = Sdl.SDL_WINDOW_OPENGL | Sdl.SDL_WINDOW_SHOWN;
		if (windowMode == WindowMode.Fullscreen)
		{
			flags |= Sdl.SDL_WINDOW_FULLSCREEN_DESKTOP;
		}
		else
		{
			if (windowMode == WindowMode.Borderless) flags |= Sdl.SDL_WINDOW_BORDERLESS;
			if (windowMode == WindowMode.Resizable) flags |= Sdl.SDL_WINDOW_RESIZABLE;
		}

		m_window = Sdl.SDL_CreateWindow(m_title, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED, width, height, flags);
		if (m_window == IntPtr.Zero)
		{
			throw new InvalidOperationException("SDL_CreateWindow failed: " + Sdl.GetErrorString());
		}

		m_glContext = Sdl.SDL_GL_CreateContext(m_window);
		if (m_glContext == IntPtr.Zero)
		{
			throw new InvalidOperationException("SDL_GL_CreateContext failed: " + Sdl.GetErrorString());
		}
		Sdl.SDL_GL_MakeCurrent(m_window, m_glContext);
		Sdl.SDL_GL_SetSwapInterval(1);
		Sdl.SDL_StartTextInput();

		Sdl.SDL_GetWindowSize(m_window, out int currentWidth, out int currentHeight);
		if (currentWidth > 0 && currentHeight > 0) m_size = new Point2(currentWidth, currentHeight);
		Sdl.SDL_GetWindowPosition(m_window, out int currentX, out int currentY);
		m_position = new Point2(currentX, currentY);

		m_eventBuffer = Marshal.AllocHGlobal(128);
		m_running = true;
		m_focused = true;
		m_pointerCaptured = false;

		InitializeAll();
		Created?.Invoke();
		m_state = State.Active;
		Activated?.Invoke();

		while (m_running)
		{
			PollEvents();
			if (!m_running) break;

			if (m_state == State.Active)
			{
				ProcessPlatformInput();
				ProcessFrame();
				Sdl.SDL_GL_SwapWindow(m_window);
				if (m_presentationInterval >= 2)
				{
					double elapsed = Time.RealTime - m_frameStartTime;
					int delay = (int)(1000.0 * ((double)((float)m_presentationInterval / 60f) - elapsed));
					if (delay > 0) Task.Delay(delay).Wait();
				}
				m_frameStartTime = Time.RealTime;
				if (m_focusRegained) m_focusRegained = false;
			}
			else
			{
				System.Threading.Thread.Sleep(10);
			}
		}

		m_state = State.Uncreated;
		Closed?.Invoke();
		DisposeAll();

		Sdl.SDL_StopTextInput();
		if (m_relativeMouse)
		{
			Sdl.SDL_SetRelativeMouseMode(0);
			m_relativeMouse = false;
		}
		Sdl.SDL_ShowCursor(1);
		Sdl.SDL_GL_MakeCurrent(m_window, IntPtr.Zero);
		Sdl.SDL_GL_DeleteContext(m_glContext);
		Sdl.SDL_DestroyWindow(m_window);
		Sdl.Shutdown();

		if (m_eventBuffer != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(m_eventBuffer);
			m_eventBuffer = IntPtr.Zero;
		}

		m_window = IntPtr.Zero;
		m_glContext = IntPtr.Zero;
		m_running = false;
	}

	public static void Close()
	{
		VerifyWindowOpened();
		m_running = false;
	}

	private static void VerifyWindowOpened()
	{
		if (m_window == IntPtr.Zero) throw new InvalidOperationException("Window is not opened.");
	}

	private static void ApplyWindowMode(WindowMode mode)
	{
		m_windowMode = mode;
		switch (mode)
		{
			case WindowMode.Resizable:
				Sdl.SDL_SetWindowFullscreen(m_window, 0);
				Sdl.SDL_SetWindowBordered(m_window, 1);
				Sdl.SDL_SetWindowResizable(m_window, 1);
				break;
			case WindowMode.Fixed:
				Sdl.SDL_SetWindowFullscreen(m_window, 0);
				Sdl.SDL_SetWindowBordered(m_window, 1);
				Sdl.SDL_SetWindowResizable(m_window, 0);
				break;
			case WindowMode.Borderless:
				Sdl.SDL_SetWindowFullscreen(m_window, 0);
				Sdl.SDL_SetWindowBordered(m_window, 0);
				Sdl.SDL_SetWindowResizable(m_window, 0);
				break;
			case WindowMode.Fullscreen:
				Sdl.SDL_SetWindowFullscreen(m_window, Sdl.SDL_WINDOW_FULLSCREEN_DESKTOP);
				break;
		}
	}

	private static void PollEvents()
	{
		if (m_eventBuffer == IntPtr.Zero) return;

		while (Sdl.SDL_PollEvent(m_eventBuffer) != 0)
		{
			uint type = (uint)Marshal.ReadInt32(m_eventBuffer, Sdl.EventTypeOffset);
			switch (type)
			{
				case Sdl.SDL_QUIT:
					m_running = false;
					break;

				case Sdl.SDL_WINDOWEVENT:
					HandleWindowEvent();
					break;

				case Sdl.SDL_KEYDOWN:
				{
					byte repeat = Marshal.ReadByte(m_eventBuffer, Sdl.KeyRepeatOffset);
					if (repeat == 0)
					{
						Key? key = TranslateKey(Marshal.ReadInt32(m_eventBuffer, Sdl.KeySymOffset));
						if (key.HasValue) Keyboard.HandleKeyDown(key.Value);
					}
					break;
				}

				case Sdl.SDL_KEYUP:
				{
					Key? key = TranslateKey(Marshal.ReadInt32(m_eventBuffer, Sdl.KeySymOffset));
					if (key.HasValue) Keyboard.HandleKeyUp(key.Value);
					break;
				}

				case Sdl.SDL_TEXTINPUT:
				{
					string text = Marshal.PtrToStringUTF8(IntPtr.Add(m_eventBuffer, Sdl.TextInputOffset));
					if (!string.IsNullOrEmpty(text))
					{
						for (int i = 0; i < text.Length; i++) Keyboard.HandleKeyPress(text[i]);
					}
					break;
				}

				case Sdl.SDL_MOUSEMOTION:
					HandleMouseMotion();
					break;

				case Sdl.SDL_MOUSEBUTTONDOWN:
					HandleMouseButton(pressed: true);
					break;

				case Sdl.SDL_MOUSEBUTTONUP:
					HandleMouseButton(pressed: false);
					break;

				case Sdl.SDL_MOUSEWHEEL:
				{
					float preciseY = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(m_eventBuffer, Sdl.MouseWheelPreciseYOffset));
					if (preciseY == 0f) preciseY = Marshal.ReadInt32(m_eventBuffer, 20);
					if (preciseY != 0f) Mouse.HandleMouseWheel(preciseY);
					break;
				}
			}
		}
	}

	private static void HandleWindowEvent()
	{
		byte evt = (byte)Marshal.ReadByte(m_eventBuffer, Sdl.WindowEventOffset);
		switch (evt)
		{
			case Sdl.SDL_WINDOWEVENT_FOCUS_GAINED:
				if (!m_focused)
				{
					m_focused = true;
					m_focusRegained = true;
				}
				break;

			case Sdl.SDL_WINDOWEVENT_FOCUS_LOST:
				if (m_focused)
				{
					m_focused = false;
					ReleasePointer();
					Keyboard.Clear();
					Mouse.Clear();
					Touch.Clear();
				}
				break;

			case Sdl.SDL_WINDOWEVENT_CLOSE:
				m_running = false;
				break;

			case Sdl.SDL_WINDOWEVENT_SIZE_CHANGED:
			case Sdl.SDL_WINDOWEVENT_RESIZED:
			{
				Sdl.SDL_GetWindowSize(m_window, out int w, out int h);
				if (w > 0 && h > 0 && (w != m_size.X || h != m_size.Y))
				{
					m_size = new Point2(w, h);
					Display.Resize();
					Resized?.Invoke();
				}
				break;
			}

			case Sdl.SDL_WINDOWEVENT_MOVED:
			{
				Sdl.SDL_GetWindowPosition(m_window, out int x, out int y);
				m_position = new Point2(x, y);
				break;
			}
		}
	}

	private static void HandleMouseMotion()
	{
		if (!m_focused) return;

		int x = Marshal.ReadInt32(m_eventBuffer, Sdl.MouseXOffset);
		int y = Marshal.ReadInt32(m_eventBuffer, Sdl.MouseYOffset);

		if (m_pointerCaptured)
		{
			if (m_relativeMouse)
			{
				int xrel = Marshal.ReadInt32(m_eventBuffer, Sdl.MouseXRelOffset);
				int yrel = Marshal.ReadInt32(m_eventBuffer, Sdl.MouseYRelOffset);
				Mouse.HandleMouseMove(xrel, yrel);
			}
			else
			{
				// Relative mode is unavailable; emulate it by warping back to
				// the window centre and feeding the offset to the camera.
				int centerX = m_size.X / 2;
				int centerY = m_size.Y / 2;
				Mouse.HandleMouseMove(x - centerX, y - centerY);
				Sdl.SDL_WarpMouseInWindow(m_window, centerX, centerY);
			}
		}
		else
		{
			Mouse.HandleMousePosition(x, y);
		}
	}

	private static void HandleMouseButton(bool pressed)
	{
		int button = Marshal.ReadByte(m_eventBuffer, Sdl.MouseButtonOffset);
		int x = Marshal.ReadInt32(m_eventBuffer, Sdl.MouseXOffset);
		int y = Marshal.ReadInt32(m_eventBuffer, Sdl.MouseYOffset);
		MouseButton? translated = button switch
		{
			1 => MouseButton.Left,
			2 => MouseButton.Middle,
			3 => MouseButton.Right,
			_ => null,
		};
		if (translated.HasValue) Mouse.HandleMouseButton(translated.Value, pressed, x, y);
	}

	private static void ProcessPlatformInput()
	{
		if (!m_focused)
		{
			if (m_pointerCaptured) ReleasePointer();
			return;
		}

		// The widget system hides the cursor while the player is in the world.
		// Follow that state: lock the pointer for mouse look, release it for menus.
		bool wantCapture = !Mouse.IsMouseVisible;
		if (wantCapture == m_pointerCaptured) return;

		m_pointerCaptured = wantCapture;
		if (wantCapture)
		{
			m_relativeMouse = Sdl.SDL_SetRelativeMouseMode(1) == 0;
			Sdl.SDL_ShowCursor(0);
		}
		else
		{
			ReleasePointer();
		}
	}

	private static void ReleasePointer()
	{
		if (m_pointerCaptured) m_pointerCaptured = false;
		if (m_relativeMouse)
		{
			Sdl.SDL_SetRelativeMouseMode(0);
			m_relativeMouse = false;
		}
		Sdl.SDL_ShowCursor(1);
	}

	private static Key? TranslateKey(int sym)
	{
		if (sym >= 'a' && sym <= 'z') return Key.A + (sym - 'a');
		if (sym >= 'A' && sym <= 'Z') return Key.A + (sym - 'A');
		if (sym >= '0' && sym <= '9') return Key.Number0 + (sym - '0');
		if (sym >= 0x4000003A && sym <= 0x40000045) return Key.F1 + (sym - 0x4000003A);

		return sym switch
		{
			0x08 => Key.BackSpace,
			0x09 => Key.Tab,
			0x0D => Key.Enter,
			0x1B => Key.Escape,
			0x20 => Key.Space,
			0x7F => Key.Delete,
			0x2D => Key.Minus,
			0x3D => Key.Plus,
			0x2C => Key.Comma,
			0x2E => Key.Period,
			0x2F => Key.Slash,
			0x3B => Key.Semicolon,
			0x27 => Key.Quote,
			0x5B => Key.LeftBracket,
			0x5D => Key.RightBracket,
			0x60 => Key.Tilde,
			0x40000039 => Key.CapsLock,
			0x40000049 => Key.Insert,
			0x4000004A => Key.Home,
			0x4000004B => Key.PageUp,
			0x4000004D => Key.End,
			0x4000004E => Key.PageDown,
			0x4000004F => Key.RightArrow,
			0x40000050 => Key.LeftArrow,
			0x40000051 => Key.DownArrow,
			0x40000052 => Key.UpArrow,
			0x400000E0 => Key.Control,
			0x400000E4 => Key.Control,
			0x400000E1 => Key.Shift,
			0x400000E5 => Key.Shift,
			0x400000E2 => Key.Alt,
			0x400000E6 => Key.Alt,
			_ => null,
		};
	}

	private static void InitializeAll()
	{
		Dispatcher.Initialize();
		Display.Initialize();
		Engine.Input.Keyboard.Initialize();
		Engine.Input.MultiKeyboard.Initialize();
		Engine.Input.Mouse.Initialize();
		Engine.Input.MultiMouse.Initialize();
		Engine.Input.Touch.Initialize();
		Engine.Input.GamePad.Initialize();
		Mixer.Initialize();
	}

	private static void DisposeAll()
	{
		Display.Dispose();
		Engine.Input.Keyboard.Dispose();
		Engine.Input.MultiKeyboard.Dispose();
		Engine.Input.Mouse.Dispose();
		Engine.Input.MultiMouse.Dispose();
		Engine.Input.Touch.Dispose();
		Engine.Input.GamePad.Dispose();
		Mixer.Dispose();
	}

	private static void BeforeFrameAll()
	{
		Time.BeforeFrame();
		Dispatcher.BeforeFrame();
		Display.BeforeFrame();
		Engine.Input.Keyboard.BeforeFrame();
		Engine.Input.MultiKeyboard.BeforeFrame();
		Engine.Input.Mouse.BeforeFrame();
		Engine.Input.MultiMouse.BeforeFrame();
		Engine.Input.Touch.BeforeFrame();
		Engine.Input.GamePad.BeforeFrame();
		Mixer.BeforeFrame();
	}

	private static void AfterFrameAll()
	{
		Time.AfterFrame();
		Dispatcher.AfterFrame();
		Display.AfterFrame();
		Engine.Input.Keyboard.AfterFrame();
		Engine.Input.MultiKeyboard.AfterFrame();
		Engine.Input.Mouse.AfterFrame();
		Engine.Input.MultiMouse.AfterFrame();
		Engine.Input.Touch.AfterFrame();
		Engine.Input.GamePad.AfterFrame();
		Mixer.AfterFrame();
	}

	private static void ProcessFrame()
	{
		BeforeFrame?.Invoke();
		BeforeFrameAll();
		try
		{
			Frame?.Invoke();
		}
		finally
		{
			AfterFrameAll();
			AfterFrame?.Invoke();
		}
	}
}
