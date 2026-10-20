using System;
using System.Collections.Generic;
using OpenTK;

namespace Engine.Input;

public static class GamePad
{
	private class State
	{
		public bool IsConnected;
		public Vector2[] Sticks = new Vector2[2];
		public float[] Triggers = new float[2];
		public bool[] Buttons = new bool[14];
		public bool[] LastButtons = new bool[14];
		public double[] ButtonsRepeat = new double[14];
	}

	private static Dictionary<int, int> m_deviceToIndex = new Dictionary<int, int>();
	private static List<int> m_toRemove = new List<int>();
	private const double m_buttonFirstRepeatTime = 0.2;
	private const double m_buttonNextRepeatTime = 0.04;
	private static State[] m_states = new State[4] { new State(), new State(), new State(), new State() };

	internal static void Initialize() { }
	internal static void Dispose() { }

	internal static void BeforeFrame()
	{
		if (!Time.PeriodicEvent(2.0, 0.0)) return;
		m_toRemove.Clear();
		foreach (int key in m_deviceToIndex.Keys)
		{
			// Device check would go here on Linux
		}
		foreach (int item in m_toRemove) Disconnect(item);
	}

	internal static void HandleButtonDown(int deviceId, int buttonIndex)
	{
		int num = TranslateDeviceId(deviceId);
		if (num < 0) return;
		GamePadButton? button = TranslateKey(buttonIndex);
		if (button.HasValue) m_states[num].Buttons[(int)button.Value] = true;
	}

	internal static void HandleButtonUp(int deviceId, int buttonIndex)
	{
		int num = TranslateDeviceId(deviceId);
		if (num < 0) return;
		GamePadButton? button = TranslateKey(buttonIndex);
		if (button.HasValue) m_states[num].Buttons[(int)button.Value] = false;
	}

	internal static void HandleTrigger(int deviceId, int triggerIndex, float value)
	{
		int num = TranslateDeviceId(deviceId);
		if (num >= 0) m_states[num].Triggers[triggerIndex] = value;
	}

	internal static void HandleMotionEvent(int deviceId, Vector2 leftStick, Vector2 rightStick, float leftTrigger, float rightTrigger)
	{
		int num = TranslateDeviceId(deviceId);
		if (num >= 0)
		{
			m_states[num].Sticks[0] = leftStick;
			m_states[num].Sticks[1] = rightStick;
			m_states[num].Triggers[0] = leftTrigger;
			m_states[num].Triggers[1] = rightTrigger;
		}
	}

	private static int TranslateDeviceId(int deviceId)
	{
		if (m_deviceToIndex.TryGetValue(deviceId, out var value)) return value;
		for (int i = 0; i < 4; i++)
		{
			bool found = false;
			foreach (var v in m_deviceToIndex.Values)
			{
				if (v == i) { found = true; break; }
			}
			if (!found)
			{
				Connect(deviceId, i);
				return i;
			}
		}
		return -1;
	}

	private static GamePadButton? TranslateKey(int keyIndex)
	{
		return keyIndex switch
		{
			0 => GamePadButton.A,
			1 => GamePadButton.B,
			2 => GamePadButton.X,
			3 => GamePadButton.Y,
			4 => GamePadButton.LeftShoulder,
			5 => GamePadButton.RightShoulder,
			6 => GamePadButton.Back,
			7 => GamePadButton.Start,
			8 => GamePadButton.LeftThumb,
			9 => GamePadButton.RightThumb,
			10 => GamePadButton.DPadUp,
			11 => GamePadButton.DPadRight,
			12 => GamePadButton.DPadDown,
			13 => GamePadButton.DPadLeft,
			_ => null,
		};
	}

	private static void Connect(int deviceId, int index)
	{
		m_deviceToIndex.Add(deviceId, index);
		m_states[index].IsConnected = true;
	}

	private static void Disconnect(int deviceId)
	{
		if (m_deviceToIndex.TryGetValue(deviceId, out var value))
		{
			m_deviceToIndex.Remove(deviceId);
			m_states[value].IsConnected = false;
		}
	}

	private static float ApplyDeadZone(float value, float deadZone)
	{
		if (value < deadZone) return 0f;
		return (value - deadZone) / (1f - deadZone);
	}

	public static bool IsConnected(int gamePadIndex)
	{
		if (gamePadIndex < 0 || gamePadIndex >= m_states.Length) throw new ArgumentOutOfRangeException("gamePadIndex");
		return m_states[gamePadIndex].IsConnected;
	}

	public static Vector2 GetStickPosition(int gamePadIndex, GamePadStick stick, float deadZone = 0f)
	{
		if (deadZone < 0f || deadZone >= 1f) throw new ArgumentOutOfRangeException("deadZone");
		if (IsConnected(gamePadIndex))
		{
			Vector2 result = m_states[gamePadIndex].Sticks[(int)stick];
			if (deadZone > 0f)
			{
				float num = (float)Math.Sqrt(result.X * result.X + result.Y * result.Y);
				if (num > 0f)
				{
					float num2 = ApplyDeadZone(num, deadZone);
					result *= num2 / num;
				}
			}
			return result;
		}
		return Vector2.Zero;
	}

	public static float GetTriggerPosition(int gamePadIndex, GamePadTrigger trigger, float deadZone = 0f)
	{
		if (deadZone < 0f || deadZone >= 1f) throw new ArgumentOutOfRangeException("deadZone");
		if (IsConnected(gamePadIndex))
		{
			return ApplyDeadZone(m_states[gamePadIndex].Triggers[(int)trigger], deadZone);
		}
		return 0f;
	}

	public static bool IsButtonDown(int gamePadIndex, GamePadButton button)
	{
		if (IsConnected(gamePadIndex)) return m_states[gamePadIndex].Buttons[(int)button];
		return false;
	}

	public static bool IsButtonDownOnce(int gamePadIndex, GamePadButton button)
	{
		if (IsConnected(gamePadIndex))
		{
			if (m_states[gamePadIndex].Buttons[(int)button])
			{
				return !m_states[gamePadIndex].LastButtons[(int)button];
			}
			return false;
		}
		return false;
	}

	public static bool IsButtonUpOnce(int gamePadIndex, GamePadButton button)
	{
		if (IsConnected(gamePadIndex))
		{
			if (!m_states[gamePadIndex].Buttons[(int)button])
			{
				return m_states[gamePadIndex].LastButtons[(int)button];
			}
			return false;
		}
		return false;
	}

	public static bool IsButtonDownRepeat(int gamePadIndex, GamePadButton button)
	{
		if (IsConnected(gamePadIndex))
		{
			if (m_states[gamePadIndex].Buttons[(int)button] && !m_states[gamePadIndex].LastButtons[(int)button])
			{
				return true;
			}
			double num = m_states[gamePadIndex].ButtonsRepeat[(int)button];
			if (num != 0.0) return Time.FrameStartTime >= num;
			return false;
		}
		return false;
	}

	public static void Clear()
	{
		for (int i = 0; i < m_states.Length; i++)
		{
			for (int j = 0; j < m_states[i].Sticks.Length; j++) m_states[i].Sticks[j] = Vector2.Zero;
			for (int k = 0; k < m_states[i].Triggers.Length; k++) m_states[i].Triggers[k] = 0f;
			for (int l = 0; l < m_states[i].Buttons.Length; l++)
			{
				m_states[i].Buttons[l] = false;
				m_states[i].LastButtons[l] = false;
				m_states[i].ButtonsRepeat[l] = 0.0;
			}
		}
	}

	internal static void AfterFrame()
	{
		for (int i = 0; i < m_states.Length; i++)
		{
			for (int j = 0; j < m_states[i].Buttons.Length; j++)
			{
				if (m_states[i].Buttons[j] && !m_states[i].LastButtons[j])
				{
					m_states[i].ButtonsRepeat[j] = Time.FrameStartTime + m_buttonFirstRepeatTime;
				}
				else if (m_states[i].Buttons[j] && Time.FrameStartTime >= m_states[i].ButtonsRepeat[j])
				{
					m_states[i].ButtonsRepeat[j] = Time.FrameStartTime + m_buttonNextRepeatTime;
				}
				m_states[i].LastButtons[j] = m_states[i].Buttons[j];
			}
		}
	}
}
