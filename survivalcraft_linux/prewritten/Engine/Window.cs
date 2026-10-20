using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Engine.Audio;
using Engine.Graphics;
using Engine.Input;
using Engine.Media;
using OpenTK;
using OpenTK.Graphics;
using OTKInput = OpenTK.Input;

namespace Engine;

public static class Window
{
	private enum State { Uncreated, Inactive, Active }

	private static bool m_contextLost;
	internal static bool m_focusRegained;
	private static int m_presentationInterval = 1;
	private static double m_frameStartTime;
	private static float m_scale;
	private static State m_state = State.Uncreated;
	private static OTKInput.KeyboardState m_previousKeyboard;
	private static OTKInput.MouseState m_previousMouse;
	private static bool m_previousMouseValid;
	private static bool m_pointerCaptured;

	public static GameWindow View { get; private set; }
	public static Point2 ScreenSize => View != null ? new Point2(View.Width, View.Height) : Point2.Zero;

	public static WindowMode WindowMode { get { VerifyWindowOpened(); return WindowMode.Fullscreen; } set { VerifyWindowOpened(); } }
	public static Point2 Position { get { VerifyWindowOpened(); return Point2.Zero; } set { VerifyWindowOpened(); } }
	public static Point2 Size { get { VerifyWindowOpened(); return new Point2(View.Width, View.Height); } set { VerifyWindowOpened(); } }
	public static float Scale { get { VerifyWindowOpened(); return m_scale; } }
	public static string Title { get { VerifyWindowOpened(); return View.Title; } set { VerifyWindowOpened(); View.Title = value; } }
	public static Image Icon { get { VerifyWindowOpened(); return null; } set { VerifyWindowOpened(); } }

	public static int PresentationInterval { get { VerifyWindowOpened(); return m_presentationInterval; } set { VerifyWindowOpened(); m_presentationInterval = MathUtils.Clamp(value, 1, 4); } }

	public static string ClipboardString { get { VerifyWindowOpened(); return ""; } set { VerifyWindowOpened(); } }

	public static bool IsCreated => m_state != State.Uncreated;
	public static bool IsActive => m_state == State.Active;

	public static event Action Created; public static event Action Resized; public static event Action Activated; public static event Action Deactivated; public static event Action Closed; public static event Action BeforeFrame; public static event Action Frame; public static event Action AfterFrame; public static event Action<UnhandledExceptionInfo> UnhandledException; public static event Action<Uri> HandleUri; public static event Action LowMemory;

	public static void Run(int width = 0, int height = 0, WindowMode windowMode = WindowMode.Fullscreen, string title = "")
	{
		if (View != null) throw new InvalidOperationException("Window is already opened.");
		AppDomain.CurrentDomain.UnhandledException += (sender, args) => {
			if (UnhandledException != null) {
				Exception ex = args.ExceptionObject as Exception ?? new Exception($"Unknown exception: {args.ExceptionObject}");
				UnhandledException(new UnhandledExceptionInfo(ex));
			}
		};
		if (width <= 0) width = 1280;
		if (height <= 0) height = 720;
		View = new GameWindow(width, height, GraphicsMode.Default, title, GameWindowFlags.Default, DisplayDevice.Default, 3, 3, GraphicsContextFlags.ForwardCompatible);
		View.Load += OnLoad;
		View.Resize += OnResize;
		View.RenderFrame += OnRenderFrame;
		View.Closing += OnClosing;
		View.Run();
	}

	private static void OnLoad(object sender, EventArgs e) { InitializeAll(); Created?.Invoke(); m_state = State.Active; Activated?.Invoke(); }
	private static void OnResize(object sender, EventArgs e) { Display.Resize(); Resized?.Invoke(); }
	private static void OnRenderFrame(object sender, FrameEventArgs e) {
		if (m_state != State.Active) return;
		ProcessPlatformInput();
		ProcessFrame();
		View.SwapBuffers();
		if (m_presentationInterval >= 2) {
			double num = Time.RealTime - m_frameStartTime;
			int num2 = (int)(1000.0 * ((double)((float)m_presentationInterval / 60f) - num));
			if (num2 > 0) Task.Delay(num2).Wait();
		}
		m_frameStartTime = Time.RealTime;
		if (m_focusRegained) m_focusRegained = false;
	}
	private static void ProcessPlatformInput()
	{
		if (!View.Focused)
		{
			// Never re-centre the pointer or synthesise key events while the window
			// is in the background.
			if (m_pointerCaptured)
			{
				m_pointerCaptured = false;
				OTKInput.Mouse.SetPointerCapture(false);
			}
			m_previousMouseValid = false;
			m_previousKeyboard = default;
			return;
		}
		OTKInput.KeyboardState keyboard = OTKInput.Keyboard.GetState();
		if (keyboard.IsConnected)
		{
			bool hadPrevious = m_previousKeyboard.IsConnected;
			for (int i = 1; i < (int)OTKInput.Key.LastKey; i++)
			{
				OTKInput.Key key = (OTKInput.Key)i;
				bool isDown = keyboard.IsKeyDown(key);
				bool wasDown = hadPrevious && m_previousKeyboard.IsKeyDown(key);
				if (isDown && !wasDown) Keyboard.HandleKeyDown(key);
				else if (!isDown && wasDown) Keyboard.HandleKeyUp(key);
			}
		}
		m_previousKeyboard = keyboard;

		OTKInput.MouseState mouse = OTKInput.Mouse.GetState();
		if (mouse.IsConnected)
		{
			bool wantCapture = !Mouse.IsMouseVisible;
			if (wantCapture != m_pointerCaptured)
			{
				m_pointerCaptured = wantCapture;
				OTKInput.Mouse.SetPointerCapture(wantCapture);
				m_previousMouseValid = false;
			}
			if (!wantCapture)
			{
				if (!m_previousMouseValid || mouse.X != m_previousMouse.X || mouse.Y != m_previousMouse.Y)
				{
					Mouse.HandleMousePosition(mouse.X, mouse.Y);
				}
			}
			else
			{
				// First-person mouse look: report the offset from the window centre
				// and re-centre the pointer so it never hits a screen edge.
				int centerX = View.ClientSize.Width / 2;
				int centerY = View.ClientSize.Height / 2;
				if (m_previousMouseValid) Mouse.HandleMouseMove(mouse.X - centerX, mouse.Y - centerY);
				OTKInput.Mouse.SetPosition(centerX, centerY);
			}
			HandleMouseButton(OTKInput.MouseButton.Left, MouseButton.Left, mouse);
			HandleMouseButton(OTKInput.MouseButton.Right, MouseButton.Right, mouse);
			HandleMouseButton(OTKInput.MouseButton.Middle, MouseButton.Middle, mouse);
			if (m_previousMouseValid && mouse.WheelPrecise != m_previousMouse.WheelPrecise)
			{
				Mouse.HandleMouseWheel(mouse.WheelPrecise - m_previousMouse.WheelPrecise);
			}
			m_previousMouse = mouse;
			m_previousMouseValid = true;
		}
	}

	private static void HandleMouseButton(OTKInput.MouseButton openTkButton, MouseButton button, OTKInput.MouseState mouse)
	{
		bool isDown = mouse.IsButtonDown(openTkButton);
		bool wasDown = m_previousMouseValid && m_previousMouse.IsButtonDown(openTkButton);
		if (isDown != wasDown) Mouse.HandleMouseButton(button, isDown, mouse.X, mouse.Y);
	}

	private static void OnClosing(object sender, CancelEventArgs e) { m_state = State.Uncreated; Closed?.Invoke(); DisposeAll(); }

	public static void Close() { VerifyWindowOpened(); View.Close(); }

	private static void VerifyWindowOpened() { if (View == null) throw new InvalidOperationException("Window is not opened."); }

	private static void PausedHandler() { if (m_state == State.Active) { m_state = State.Inactive; Engine.Input.Keyboard.Clear(); Mixer.Deactivate(); Deactivated?.Invoke(); } }
	private static void ResumedHandler() { if (m_state == State.Inactive) { m_state = State.Active; Mixer.Activate(); Activated?.Invoke(); } }
	private static void DestroyedHandler() { if (m_state == State.Active) { m_state = State.Inactive; Deactivated?.Invoke(); } m_state = State.Uncreated; Closed?.Invoke(); DisposeAll(); }
	private static void NewIntentHandler(object intent) { }
	private static void TrimMemoryHandler(int level) { }
	private static void ResizeHandler(object sender, EventArgs args) { if (m_state != State.Uncreated) { Display.Resize(); Resized?.Invoke(); } }
	private static void ContextSetHandler(object sender, EventArgs args) { if (m_contextLost) { m_contextLost = false; Display.HandleDeviceReset(); } }
	private static void ContextLostHandler(object sender, EventArgs args) { m_contextLost = true; Display.HandleDeviceLost(); }
	private static Uri GetUriFromIntent(object intent) => null;

	private static void InitializeAll() { Dispatcher.Initialize(); Display.Initialize(); Engine.Input.Keyboard.Initialize(); Engine.Input.MultiKeyboard.Initialize(); Engine.Input.Mouse.Initialize(); Engine.Input.MultiMouse.Initialize(); Engine.Input.Touch.Initialize(); Engine.Input.GamePad.Initialize(); Mixer.Initialize(); }
	private static void DisposeAll() { Display.Dispose(); Engine.Input.Keyboard.Dispose(); Engine.Input.MultiKeyboard.Dispose(); Engine.Input.Mouse.Dispose(); Engine.Input.MultiMouse.Dispose(); Engine.Input.Touch.Dispose(); Engine.Input.GamePad.Dispose(); Mixer.Dispose(); }

	private static void BeforeFrameAll() { Time.BeforeFrame(); Dispatcher.BeforeFrame(); Display.BeforeFrame(); Engine.Input.Keyboard.BeforeFrame(); Engine.Input.MultiKeyboard.BeforeFrame(); Engine.Input.Mouse.BeforeFrame(); Engine.Input.MultiMouse.BeforeFrame(); Engine.Input.Touch.BeforeFrame(); Engine.Input.GamePad.BeforeFrame(); Mixer.BeforeFrame(); }
	private static void AfterFrameAll() { Time.AfterFrame(); Dispatcher.AfterFrame(); Display.AfterFrame(); Engine.Input.Keyboard.AfterFrame(); Engine.Input.MultiKeyboard.AfterFrame(); Engine.Input.Mouse.AfterFrame(); Engine.Input.MultiMouse.AfterFrame(); Engine.Input.Touch.AfterFrame(); Engine.Input.GamePad.AfterFrame(); Mixer.AfterFrame(); }

	private static void ProcessFrame() { BeforeFrame?.Invoke(); BeforeFrameAll(); try { Frame?.Invoke(); } finally { AfterFrameAll(); AfterFrame?.Invoke(); } }
}
