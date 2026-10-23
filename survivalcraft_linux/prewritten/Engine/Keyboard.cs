using System;
using OTKKey = OpenTK.Input.Key;

namespace Engine.Input;

public static class Keyboard
{
	private const double m_keyFirstRepeatTime = 0.3;
	private const double m_keyNextRepeatTime = 0.04;

	private static bool[] m_keysDownArray = new bool[Enum.GetValues(typeof(Key)).Length];
	private static bool[] m_keysDownOnceArray = new bool[Enum.GetValues(typeof(Key)).Length];
	private static double[] m_keysDownRepeatArray = new double[Enum.GetValues(typeof(Key)).Length];
	private static Key? m_lastKey;
	private static char? m_lastChar;

	public static Key? LastKey => m_lastKey;
	public static char? LastChar => m_lastChar;
	public static bool IsKeyboardVisible { get; private set; }
	public static bool BackButtonQuitsApp { get; set; }
	public static event Action<Key> KeyDown;
	public static event Action<Key> KeyUp;
	public static event Action<char> CharacterEntered;

	internal static void Initialize() { }
	internal static void Dispose() { }

	internal static void HandleKeyDown(OTKKey key)
	{
		if (key == OTKKey.Unknown) return;
		Key translated = TranslateKey(key);
		if ((int)translated >= 0) ProcessKeyDown(translated);
	}

	internal static void HandleKeyUp(OTKKey key)
	{
		if (key == OTKKey.Unknown) return;
		Key translated = TranslateKey(key);
		if ((int)translated >= 0) ProcessKeyUp(translated);
	}

	private static Key TranslateKey(OTKKey key)
	{
		return key switch
		{
			OTKKey.Up => Key.UpArrow,
			OTKKey.Down => Key.DownArrow,
			OTKKey.Left => Key.LeftArrow,
			OTKKey.Right => Key.RightArrow,
			OTKKey.Enter => Key.Enter,
			OTKKey.Escape => Key.Escape,
			OTKKey.Space => Key.Space,
			OTKKey.Tab => Key.Tab,
			OTKKey.BackSpace => Key.BackSpace,
			OTKKey.Insert => Key.Insert,
			OTKKey.Delete => Key.Delete,
			OTKKey.PageUp => Key.PageUp,
			OTKKey.PageDown => Key.PageDown,
			OTKKey.Home => Key.Home,
			OTKKey.End => Key.End,
			OTKKey.CapsLock => Key.CapsLock,
			OTKKey.F1 => Key.F1,
			OTKKey.F2 => Key.F2,
			OTKKey.F3 => Key.F3,
			OTKKey.F4 => Key.F4,
			OTKKey.F5 => Key.F5,
			OTKKey.F6 => Key.F6,
			OTKKey.F7 => Key.F7,
			OTKKey.F8 => Key.F8,
			OTKKey.F9 => Key.F9,
			OTKKey.F10 => Key.F10,
			OTKKey.F11 => Key.F11,
			OTKKey.F12 => Key.F12,
			OTKKey.A => Key.A,
			OTKKey.B => Key.B,
			OTKKey.C => Key.C,
			OTKKey.D => Key.D,
			OTKKey.E => Key.E,
			OTKKey.F => Key.F,
			OTKKey.G => Key.G,
			OTKKey.H => Key.H,
			OTKKey.I => Key.I,
			OTKKey.J => Key.J,
			OTKKey.K => Key.K,
			OTKKey.L => Key.L,
			OTKKey.M => Key.M,
			OTKKey.N => Key.N,
			OTKKey.O => Key.O,
			OTKKey.P => Key.P,
			OTKKey.Q => Key.Q,
			OTKKey.R => Key.R,
			OTKKey.S => Key.S,
			OTKKey.T => Key.T,
			OTKKey.U => Key.U,
			OTKKey.V => Key.V,
			OTKKey.W => Key.W,
			OTKKey.X => Key.X,
			OTKKey.Y => Key.Y,
			OTKKey.Z => Key.Z,
			OTKKey.Number0 => Key.Number0,
			OTKKey.Number1 => Key.Number1,
			OTKKey.Number2 => Key.Number2,
			OTKKey.Number3 => Key.Number3,
			OTKKey.Number4 => Key.Number4,
			OTKKey.Number5 => Key.Number5,
			OTKKey.Number6 => Key.Number6,
			OTKKey.Number7 => Key.Number7,
			OTKKey.Number8 => Key.Number8,
			OTKKey.Number9 => Key.Number9,
			OTKKey.Tilde => Key.Tilde,
			OTKKey.Minus => Key.Minus,
			OTKKey.Plus => Key.Plus,
			OTKKey.BracketLeft => Key.LeftBracket,
			OTKKey.BracketRight => Key.RightBracket,
			OTKKey.Semicolon => Key.Semicolon,
			OTKKey.Quote => Key.Quote,
			OTKKey.Comma => Key.Comma,
			OTKKey.Period => Key.Period,
			OTKKey.Slash => Key.Slash,
			OTKKey.BackSlash => (Key)(-1),
			OTKKey.ShiftLeft => Key.Shift,
			OTKKey.ShiftRight => Key.Shift,
			OTKKey.ControlLeft => Key.Control,
			OTKKey.ControlRight => Key.Control,
			OTKKey.AltLeft => Key.Alt,
			OTKKey.AltRight => Key.Alt,
			_ => (Key)(-1),
		};
	}

	internal static void HandleKeyPress(int unicodeCharacter) => ProcessCharacterEntered((char)unicodeCharacter);

	private static void ShowKeyboardInternal(string title, string description, string defaultText, bool passwordMode, Action<string> enter, Action cancel) { }

	public static bool IsKeyDown(Key key) => m_keysDownArray[(int)key];
	public static bool IsKeyDownOnce(Key key) => m_keysDownOnceArray[(int)key];
	public static bool IsKeyDownRepeat(Key key) {
		double num = m_keysDownRepeatArray[(int)key];
		if (num < 0.0) return true;
		if (num != 0.0) return Time.FrameStartTime >= num;
		return false;
	}

	public static void ShowKeyboard(string title, string description, string defaultText, bool passwordMode, Action<string> enter, Action cancel)
	{
		if (title == null) throw new ArgumentNullException(nameof(title));
		if (description == null) throw new ArgumentNullException(nameof(description));
		if (defaultText == null) throw new ArgumentNullException(nameof(defaultText));
		// Desktop: no on-screen keyboard; text input works via LastChar/LastKey
		// Do not set IsKeyboardVisible = true, as that blocks character input
	}

	public static void Clear() {
		m_lastKey = null; m_lastChar = null;
		for (int i = 0; i < m_keysDownArray.Length; i++) {
			m_keysDownArray[i] = false; m_keysDownOnceArray[i] = false; m_keysDownRepeatArray[i] = 0.0;
		}
	}

	internal static void BeforeFrame() { }
	internal static void AfterFrame() {
		if (BackButtonQuitsApp && IsKeyDownOnce(Key.Escape)) Window.Close();
		m_lastKey = null; m_lastChar = null;
		for (int i = 0; i < m_keysDownOnceArray.Length; i++) m_keysDownOnceArray[i] = false;
		for (int j = 0; j < m_keysDownRepeatArray.Length; j++) {
			if (m_keysDownArray[j]) {
				if (m_keysDownRepeatArray[j] < 0.0) m_keysDownRepeatArray[j] = Time.FrameStartTime + 0.3;
				else if (Time.FrameStartTime >= m_keysDownRepeatArray[j]) m_keysDownRepeatArray[j] = MathUtils.Max(Time.FrameStartTime, m_keysDownRepeatArray[j] + 0.04);
			} else m_keysDownRepeatArray[j] = 0.0;
		}
	}

	private static bool ProcessKeyDown(Key key) {
		if (!Window.IsActive || IsKeyboardVisible) return false;
		m_lastKey = key;
		if (!m_keysDownArray[(int)key]) { m_keysDownArray[(int)key] = true; m_keysDownOnceArray[(int)key] = true; m_keysDownRepeatArray[(int)key] = -1.0; }
		KeyDown?.Invoke(key); return true;
	}
	private static bool ProcessKeyUp(Key key) {
		if (!Window.IsActive || IsKeyboardVisible) return false;
		if (m_keysDownArray[(int)key]) m_keysDownArray[(int)key] = false;
		KeyUp?.Invoke(key); return true;
	}
	private static bool ProcessCharacterEntered(char ch) {
		if (!Window.IsActive || IsKeyboardVisible) return false;
		m_lastChar = ch; CharacterEntered?.Invoke(ch); return true;
	}
}
