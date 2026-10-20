using System;
using OpenTK;
using OpenTK.Input;

namespace Engine.Input;

public static class Mouse
{
	private static Vector2 m_queuedMouseMovement;
	private static float m_queuedMouseWheelMovement;
	private static bool m_pointerCaptureRequested;
	private static bool[] m_mouseButtonsDownArray;
	private static int[] m_mouseButtonsDownFrameArray;
	private static bool[] m_mouseButtonsDelayedUpArray;
	private static bool[] m_mouseButtonsDownOnceArray;
	private static bool[] m_mouseButtonsUpOnceArray;

	public static Point2 MouseMovement { get; private set; }
	public static int MouseWheelMovement { get; private set; }
	public static Point2? MousePosition { get; private set; }
	public static bool IsMouseVisible { get; set; }
	public static event Action<MouseEvent> MouseMove;
	public static event Action<MouseButtonEvent> MouseDown;
	public static event Action<MouseButtonEvent> MouseUp;

	public static void SetMousePosition(int x, int y) { }
	internal static void Initialize() {
		m_mouseButtonsDownArray = new bool[Enum.GetValues(typeof(MouseButton)).Length];
		m_mouseButtonsDownFrameArray = new int[Enum.GetValues(typeof(MouseButton)).Length];
		m_mouseButtonsDelayedUpArray = new bool[Enum.GetValues(typeof(MouseButton)).Length];
		m_mouseButtonsDownOnceArray = new bool[Enum.GetValues(typeof(MouseButton)).Length];
		m_mouseButtonsUpOnceArray = new bool[Enum.GetValues(typeof(MouseButton)).Length];
		IsMouseVisible = true;
	}
	internal static void Dispose() { }
	internal static void BeforeFrame() {
		if (IsMouseVisible) { if (m_pointerCaptureRequested) { m_pointerCaptureRequested = false; } }
		else { if (!m_pointerCaptureRequested) { m_pointerCaptureRequested = true; } MouseMovement = Round(m_queuedMouseMovement.X, m_queuedMouseMovement.Y); m_queuedMouseMovement = Vector2.Zero; }
		MouseWheelMovement = (int)MathUtils.Round(m_queuedMouseWheelMovement) * 120; m_queuedMouseWheelMovement = 0f;
	}
	internal static void HandleMouseMove(double x, double y) => m_queuedMouseMovement += new Vector2((float)x, (float)y);
	internal static void HandleMousePosition(double x, double y) => ProcessMouseMove(Round((float)x, (float)y));
	internal static void HandleMouseButton(MouseButton button, bool pressed, double x, double y) { if (pressed) ProcessMouseDown(button, Round((float)x, (float)y)); else ProcessMouseUp(button, Round((float)x, (float)y)); }
	internal static void HandleMouseWheel(double delta) => m_queuedMouseWheelMovement += (float)delta;
	private static MouseButton TranslateMouseButton(MouseButton state) => state;
	private static Point2 Round(float x, float y) => new Point2((int)MathUtils.Round(x), (int)MathUtils.Round(y));
	public static bool IsMouseButtonDown(MouseButton b) => m_mouseButtonsDownArray[(int)b];
	public static bool IsMouseButtonDownOnce(MouseButton b) => m_mouseButtonsDownOnceArray[(int)b];
	public static bool IsMouseButtonUpOnce(MouseButton b) => m_mouseButtonsUpOnceArray[(int)b];
	public static void Clear() { for (int i = 0; i < m_mouseButtonsDownArray.Length; i++) { m_mouseButtonsDownArray[i] = false; m_mouseButtonsDownFrameArray[i] = 0; m_mouseButtonsDelayedUpArray[i] = false; m_mouseButtonsDownOnceArray[i] = false; m_mouseButtonsUpOnceArray[i] = false; } }
	internal static void AfterFrame() {
		for (int i = 0; i < m_mouseButtonsDownArray.Length; i++) {
			m_mouseButtonsDownOnceArray[i] = false;
			if (m_mouseButtonsDelayedUpArray[i]) { m_mouseButtonsDelayedUpArray[i] = false; m_mouseButtonsDownArray[i] = false; m_mouseButtonsUpOnceArray[i] = true; } else m_mouseButtonsUpOnceArray[i] = false;
		}
		if (!IsMouseVisible) MousePosition = null;
	}
	private static void ProcessMouseDown(MouseButton button, Point2 position) {
		if (Window.IsActive && !Keyboard.IsKeyboardVisible) {
			if (!MousePosition.HasValue) ProcessMouseMove(position);
			m_mouseButtonsDownArray[(int)button] = true; m_mouseButtonsDownFrameArray[(int)button] = Time.FrameIndex; m_mouseButtonsDownOnceArray[(int)button] = true;
			if (IsMouseVisible && MouseDown != null) MouseDown(new MouseButtonEvent { Button = button, Position = position });
		}
	}
	private static void ProcessMouseUp(MouseButton button, Point2 position) {
		if (Window.IsActive && !Keyboard.IsKeyboardVisible) {
			if (!MousePosition.HasValue) ProcessMouseMove(position);
			if (m_mouseButtonsDownArray[(int)button] && Time.FrameIndex == m_mouseButtonsDownFrameArray[(int)button]) m_mouseButtonsDelayedUpArray[(int)button] = true;
			else { m_mouseButtonsDownArray[(int)button] = false; m_mouseButtonsUpOnceArray[(int)button] = true; }
			if (IsMouseVisible && MouseUp != null) MouseUp(new MouseButtonEvent { Button = button, Position = position });
		}
	}
	private static void ProcessMouseMove(Point2 position) { if (Window.IsActive && !Keyboard.IsKeyboardVisible && IsMouseVisible) { MousePosition = position; MouseMove?.Invoke(new MouseEvent { Position = position }); } }
}
