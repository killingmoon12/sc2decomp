using System;
using System.Collections.Generic;

namespace Engine.Input;

public static class Touch
{
	private static List<TouchLocation> m_touchLocations = new List<TouchLocation>();
	public static ReadOnlyList<TouchLocation> TouchLocations => new ReadOnlyList<TouchLocation>(m_touchLocations);
	public static event Action<TouchLocation> TouchPressed;
	public static event Action<TouchLocation> TouchReleased;
	public static event Action<TouchLocation> TouchMoved;

	internal static void Initialize() { }
	internal static void Dispose() { }

	internal static void HandleTouchDown(int id, Vector2 position) => ProcessTouchPressed(id, position);
	internal static void HandleTouchMove(int id, Vector2 position) => ProcessTouchMoved(id, position);
	internal static void HandleTouchUp(int id, Vector2 position) => ProcessTouchReleased(id, position);

	public static void Clear() => m_touchLocations.Clear();

	internal static void BeforeFrame() { }

	internal static void AfterFrame()
	{
		for (int i = 0; i < m_touchLocations.Count; )
		{
			if (m_touchLocations[i].State == TouchLocationState.Released) m_touchLocations.RemoveAt(i);
			else if (m_touchLocations[i].ReleaseQueued)
			{
				m_touchLocations[i] = new TouchLocation { Id = m_touchLocations[i].Id, Position = m_touchLocations[i].Position, State = TouchLocationState.Released };
				i++;
			}
			else if (m_touchLocations[i].State == TouchLocationState.Pressed)
			{
				m_touchLocations[i] = new TouchLocation { Id = m_touchLocations[i].Id, Position = m_touchLocations[i].Position, State = TouchLocationState.Moved };
				i++;
			}
			else i++;
		}
	}

	private static int FindTouchLocationIndex(int id)
	{
		for (int i = 0; i < m_touchLocations.Count; i++) if (m_touchLocations[i].Id == id) return i;
		return -1;
	}

	private static void ProcessTouchPressed(int id, Vector2 position) => ProcessTouchMoved(id, position);

	private static void ProcessTouchMoved(int id, Vector2 position)
	{
		if (!Window.IsActive || Keyboard.IsKeyboardVisible) return;
		int index = FindTouchLocationIndex(id);
		if (index >= 0)
		{
			if (m_touchLocations[index].State == TouchLocationState.Moved)
				m_touchLocations[index] = new TouchLocation { Id = id, Position = position, State = TouchLocationState.Moved };
			TouchMoved?.Invoke(m_touchLocations[index]);
		}
		else
		{
			m_touchLocations.Add(new TouchLocation { Id = id, Position = position, State = TouchLocationState.Pressed });
			TouchPressed?.Invoke(m_touchLocations[^1]);
		}
	}

	private static void ProcessTouchReleased(int id, Vector2 position)
	{
		if (!Window.IsActive || Keyboard.IsKeyboardVisible) return;
		int index = FindTouchLocationIndex(id);
		if (index >= 0)
		{
			if (m_touchLocations[index].State == TouchLocationState.Pressed)
				m_touchLocations[index] = new TouchLocation { Id = id, Position = position, State = TouchLocationState.Pressed, ReleaseQueued = true };
			else m_touchLocations[index] = new TouchLocation { Id = id, Position = position, State = TouchLocationState.Released };
			TouchReleased?.Invoke(m_touchLocations[index]);
		}
	}
}
