using System;
using OpenTK.Platform;

namespace OpenTK.Input;

public static class Mouse
{
	private static readonly IMouseDriver2 driver = Factory.Default.CreateMouseDriver();

	private static readonly object SyncRoot = new object();

	public static MouseState GetState()
	{
		lock (SyncRoot)
		{
			return driver.GetState();
		}
	}

	public static MouseState GetState(int index)
	{
		if (index < 0)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		lock (SyncRoot)
		{
			return driver.GetState(index);
		}
	}

	public static void SetPosition(double x, double y)
	{
		lock (SyncRoot)
		{
			driver.SetPosition(x, y);
		}
	}

	/// <summary>
	/// Locks the pointer for first-person mouse look (hidden cursor, confined
	/// pointer) or releases it. Implemented by platforms that support it.
	/// </summary>
	public static void SetPointerCapture(bool captured)
	{
		lock (SyncRoot)
		{
			if (driver is IMouseCursorController controller) controller.SetPointerCapture(captured);
		}
	}
}
