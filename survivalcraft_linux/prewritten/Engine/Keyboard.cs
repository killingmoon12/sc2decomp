using System;

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

	internal static void HandleKeyDown(Key key)
	{
		if ((int)key >= 0 && (int)key < m_keysDownArray.Length) ProcessKeyDown(key);
	}

	internal static void HandleKeyUp(Key key)
	{
		if ((int)key >= 0 && (int)key < m_keysDownArray.Length) ProcessKeyUp(key);
	}

	internal static void HandleKeyPress(int unicodeCharacter) => ProcessCharacterEntered((char)unicodeCharacter);

	internal static void ShowKeyboardInternal(string title, string description, string defaultText, bool passwordMode, Action<string> enter, Action cancel) { }

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
		// Desktop: no on-screen keyboard; text input works via LastChar/LastKey.
		// Do not set IsKeyboardVisible = true, as that blocks character input.
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
