using System;
using OpenTK;
using OTKController = OpenTK.Input.Controller;

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

	private const double m_buttonFirstRepeatTime = 0.2;
	private const double m_buttonNextRepeatTime = 0.04;
	private static State[] m_states = new State[4] { new State(), new State(), new State(), new State() };

	internal static void Initialize() { }
	internal static void Dispose() { }

	internal static void BeforeFrame()
	{
		for (int i = 0; i < m_states.Length; i++)
		{
			State state = m_states[i];
			OpenTK.Input.ControllerState controller = OTKController.GetState(i);
			if (!controller.IsConnected)
			{
				if (state.IsConnected) ResetState(state);
				continue;
			}

			state.IsConnected = true;
			// SDL reports up as negative; the engine uses up as positive for
			// both movement and first-person look.
			state.Sticks[0] = new Vector2(controller.LeftStick.X, -controller.LeftStick.Y);
			state.Sticks[1] = new Vector2(controller.RightStick.X, -controller.RightStick.Y);
			state.Triggers[0] = controller.LeftTrigger;
			state.Triggers[1] = controller.RightTrigger;

			Array.Clear(state.Buttons, 0, state.Buttons.Length);
			for (int buttonIndex = 0; buttonIndex < 15; buttonIndex++)
			{
				GamePadButton? button = TranslateKey(buttonIndex);
				if (button.HasValue)
				{
					state.Buttons[(int)button.Value] = controller.IsButtonDown(buttonIndex);
				}
			}
		}
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
		for (int i = 0; i < m_states.Length; i++)
		{
			if (m_states[i].IsConnected) return i;
		}
		return -1;
	}

	private static GamePadButton? TranslateKey(int keyIndex)
	{
		// SDL_GameControllerButton indices.
		return keyIndex switch
		{
			0 => GamePadButton.A,
			1 => GamePadButton.B,
			2 => GamePadButton.X,
			3 => GamePadButton.Y,
			4 => GamePadButton.Back,
			6 => GamePadButton.Start,
			7 => GamePadButton.LeftThumb,
			8 => GamePadButton.RightThumb,
			9 => GamePadButton.LeftShoulder,
			10 => GamePadButton.RightShoulder,
			11 => GamePadButton.DPadUp,
			12 => GamePadButton.DPadDown,
			13 => GamePadButton.DPadLeft,
			14 => GamePadButton.DPadRight,
			_ => null,
		};
	}

	private static void ResetState(State state)
	{
		state.IsConnected = false;
		state.Sticks[0] = Vector2.Zero;
		state.Sticks[1] = Vector2.Zero;
		state.Triggers[0] = 0f;
		state.Triggers[1] = 0f;
		Array.Clear(state.Buttons, 0, state.Buttons.Length);
		Array.Clear(state.LastButtons, 0, state.LastButtons.Length);
		Array.Clear(state.ButtonsRepeat, 0, state.ButtonsRepeat.Length);
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
			if (!m_states[gamePadIndex].Buttons[(int)button])
			{
				return false;
			}
			if (!m_states[gamePadIndex].LastButtons[(int)button])
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
				else if (!m_states[i].Buttons[j])
				{
					m_states[i].ButtonsRepeat[j] = 0.0;
				}
				m_states[i].LastButtons[j] = m_states[i].Buttons[j];
			}
		}
	}
}
