using System;

namespace OpenTK.Input
{
    /// <summary>
    /// Snapshot of one SDL game controller. Button indices are the raw SDL
    /// <c>SDL_GameControllerButton</c> values; the engine maps them to its own
    /// <c>Engine.Input.GamePadButton</c> enum.
    /// </summary>
    public sealed class ControllerState
    {
        internal readonly bool[] Buttons = new bool[15];

        public bool IsConnected { get; internal set; }

        public string Name { get; internal set; } = string.Empty;

        /// <summary>Left stick, SDL convention (up is negative).</summary>
        public Vector2 LeftStick { get; internal set; }

        /// <summary>Right stick, SDL convention (up is negative).</summary>
        public Vector2 RightStick { get; internal set; }

        /// <summary>Left trigger in the 0..1 range.</summary>
        public float LeftTrigger { get; internal set; }

        /// <summary>Right trigger in the 0..1 range.</summary>
        public float RightTrigger { get; internal set; }

        public bool IsButtonDown(int sdlButton)
        {
            return sdlButton >= 0 && sdlButton < Buttons.Length && Buttons[sdlButton];
        }

        internal void Reset()
        {
            IsConnected = false;
            Name = string.Empty;
            LeftStick = Vector2.Zero;
            RightStick = Vector2.Zero;
            LeftTrigger = 0f;
            RightTrigger = 0f;
            Array.Clear(Buttons, 0, Buttons.Length);
        }
    }

    /// <summary>
    /// Game controller state fed by the SDL2 platform layer. This is separate
    /// from the unused OpenTK <see cref="GamePad"/> stub.
    /// </summary>
    public static class Controller
    {
        public const int MaxControllers = 4;

        private static readonly ControllerState[] States =
        {
            new ControllerState(),
            new ControllerState(),
            new ControllerState(),
            new ControllerState(),
        };

        public static ControllerState GetState(int index)
        {
            if (index < 0 || index >= States.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return States[index];
        }

        public static bool IsConnected(int index)
        {
            return GetState(index).IsConnected;
        }

        public static int ConnectedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < States.Length; i++)
                {
                    if (States[i].IsConnected) count++;
                }
                return count;
            }
        }

        internal static void ResetAll()
        {
            for (int i = 0; i < States.Length; i++)
            {
                States[i].Reset();
            }
        }
    }
}
