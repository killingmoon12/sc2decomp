using System;
using System.Runtime.InteropServices;
using OpenTK.Input;

namespace OpenTK.Platform.Linux
{
    /// <summary>
    /// SDL2 keyboard, mouse and game controller input. Mouse position is kept
    /// as a virtual cursor and fed from relative motion, so the engine's
    /// mouse-look code works on KMS/DRM where there is no desktop cursor.
    /// </summary>
    internal static class LinuxInput
    {
        private static readonly bool[] KeysDown = new bool[256];
        private static readonly bool[] ButtonsDown = new bool[(int)MouseButton.LastButton];

        private static int MouseX;
        private static int MouseY;
        private static float Wheel;
        private static int _width = 1;
        private static int _height = 1;
        private static IntPtr _window;
        private static bool _captured;

        private static readonly IntPtr[] Controllers = new IntPtr[Controller.MaxControllers];
        private static readonly int[] ControllerInstances = new int[Controller.MaxControllers];

        public static event Action<string> OnKeyPress;

        static LinuxInput()
        {
            for (int i = 0; i < ControllerInstances.Length; i++) ControllerInstances[i] = -1;
        }

        /// <summary>Registers the SDL window used for pointer warping.</summary>
        public static void SetWindow(IntPtr window, int width, int height)
        {
            _window = window;
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);
            MouseX = _width / 2;
            MouseY = _height / 2;
            _captured = false;
        }

        public static void SetWindowSize(int width, int height)
        {
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);
            MouseX = Math.Clamp(MouseX, 0, _width - 1);
            MouseY = Math.Clamp(MouseY, 0, _height - 1);
        }

        public static void SetMousePosition(double x, double y)
        {
            MouseX = Math.Clamp((int)Math.Round(x), 0, _width - 1);
            MouseY = Math.Clamp((int)Math.Round(y), 0, _height - 1);
            if (!_captured && _window != IntPtr.Zero)
            {
                SDL2.WarpMouseInWindow(_window, MouseX, MouseY);
            }
        }

        public static void SetPointerCapture(bool captured)
        {
            if (_captured == captured) return;
            _captured = captured;
            // The game always draws its own soft cursor, so the OS cursor is
            // never shown, whether or not relative mode is active.
            SDL2.ShowCursor(0);
            if (_window == IntPtr.Zero) return;
            SDL2.SetRelativeMouseMode(captured ? 1 : 0);
            if (!captured)
            {
                SDL2.WarpMouseInWindow(_window, MouseX, MouseY);
            }
        }

        /// <summary>Drops keys/buttons after focus loss so nothing sticks down.</summary>
        public static void ResetInput()
        {
            Array.Clear(KeysDown, 0, KeysDown.Length);
            Array.Clear(ButtonsDown, 0, ButtonsDown.Length);
            Wheel = 0f;
        }

        public static void Shutdown()
        {
            for (int i = 0; i < Controllers.Length; i++)
            {
                if (Controllers[i] != IntPtr.Zero)
                {
                    SDL2.GameControllerClose(Controllers[i]);
                    Controllers[i] = IntPtr.Zero;
                    ControllerInstances[i] = -1;
                }
            }
            Controller.ResetAll();
            _window = IntPtr.Zero;
        }

        public static void OpenGameControllers()
        {
            SDL2.JoystickUpdate();
            int count = SDL2.NumJoysticks();
            for (int i = 0; i < count; i++)
            {
                if (SDL2.IsGameController(i) != 0)
                {
                    OpenControllerByDeviceIndex(i);
                }
            }
        }

        public static void ProcessEvent(uint type, IntPtr ev)
        {
            switch (type)
            {
                case SDL2.SDL_KEYDOWN:
                {
                    int sym = Marshal.ReadInt32(ev, 20);
                    Key key = TranslateKey(sym);
                    if (key != Key.Unknown) KeysDown[(int)key] = true;
                    break;
                }
                case SDL2.SDL_KEYUP:
                {
                    int sym = Marshal.ReadInt32(ev, 20);
                    Key key = TranslateKey(sym);
                    if (key != Key.Unknown) KeysDown[(int)key] = false;
                    break;
                }
                case SDL2.SDL_TEXTINPUT:
                {
                    string text = Marshal.PtrToStringUTF8(ev + 12);
                    if (!string.IsNullOrEmpty(text)) OnKeyPress?.Invoke(text);
                    break;
                }
                case SDL2.SDL_MOUSEMOTION:
                {
                    int xrel = Marshal.ReadInt32(ev, 28);
                    int yrel = Marshal.ReadInt32(ev, 32);
                    MouseX = Math.Clamp(MouseX + xrel, 0, _width - 1);
                    MouseY = Math.Clamp(MouseY + yrel, 0, _height - 1);
                    break;
                }
                case SDL2.SDL_MOUSEBUTTONDOWN:
                case SDL2.SDL_MOUSEBUTTONUP:
                {
                    int button = Marshal.ReadByte(ev, 16);
                    bool down = type == SDL2.SDL_MOUSEBUTTONDOWN;
                    MouseButton mapped = TranslateButton(button);
                    if (mapped != MouseButton.LastButton)
                    {
                        ButtonsDown[(int)mapped] = down;
                    }
                    if (!_captured)
                    {
                        MouseX = Math.Clamp(Marshal.ReadInt32(ev, 20), 0, _width - 1);
                        MouseY = Math.Clamp(Marshal.ReadInt32(ev, 24), 0, _height - 1);
                    }
                    break;
                }
                case SDL2.SDL_MOUSEWHEEL:
                {
                    int y = Marshal.ReadInt32(ev, 20);
                    int direction = Marshal.ReadInt32(ev, 24);
                    Wheel += (direction == SDL2.SDL_MOUSEWHEEL_FLIPPED) ? -y : y;
                    break;
                }
                case SDL2.SDL_CONTROLLERDEVICEADDED:
                {
                    OpenControllerByDeviceIndex(Marshal.ReadInt32(ev, 8));
                    break;
                }
                case SDL2.SDL_CONTROLLERDEVICEREMOVED:
                {
                    CloseControllerByInstance(Marshal.ReadInt32(ev, 8));
                    break;
                }
                case SDL2.SDL_CONTROLLERAXISMOTION:
                {
                    int instance = Marshal.ReadInt32(ev, 8);
                    int axis = Marshal.ReadByte(ev, 12);
                    short value = Marshal.ReadInt16(ev, 16);
                    int slot = FindControllerSlot(instance);
                    if (slot >= 0) ApplyAxis(Controller.GetState(slot), axis, value);
                    break;
                }
                case SDL2.SDL_CONTROLLERBUTTONDOWN:
                case SDL2.SDL_CONTROLLERBUTTONUP:
                {
                    int instance = Marshal.ReadInt32(ev, 8);
                    int button = Marshal.ReadByte(ev, 12);
                    int slot = FindControllerSlot(instance);
                    if (slot >= 0 && button >= 0 && button < 15)
                    {
                        Controller.GetState(slot).Buttons[button] = type == SDL2.SDL_CONTROLLERBUTTONDOWN;
                    }
                    break;
                }
            }
        }

        public static KeyboardState GetKeyboardState()
        {
            KeyboardState state = new KeyboardState();
            state.IsConnected = true;
            for (int i = 1; i < KeysDown.Length && i < (int)Key.LastKey; i++)
            {
                if (KeysDown[i]) state[(Key)i] = true;
            }
            return state;
        }

        public static MouseState GetMouseState()
        {
            MouseState state = new MouseState();
            state.IsConnected = true;
            state.X = MouseX;
            state.Y = MouseY;
            state.WheelPrecise = Wheel;
            for (int i = 0; i < ButtonsDown.Length; i++)
            {
                if (ButtonsDown[i]) state[(MouseButton)i] = true;
            }
            return state;
        }

        private static int FindControllerSlot(int instance)
        {
            for (int i = 0; i < ControllerInstances.Length; i++)
            {
                if (ControllerInstances[i] == instance) return i;
            }
            return -1;
        }

        private static void OpenControllerByDeviceIndex(int deviceIndex)
        {
            if (deviceIndex < 0 || SDL2.IsGameController(deviceIndex) == 0) return;

            int instance = SDL2.JoystickGetDeviceInstanceID(deviceIndex);
            if (instance >= 0 && FindControllerSlot(instance) >= 0) return;

            int slot = -1;
            for (int i = 0; i < Controllers.Length; i++)
            {
                if (Controllers[i] == IntPtr.Zero)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0) return;

            IntPtr controller = SDL2.GameControllerOpen(deviceIndex);
            if (controller == IntPtr.Zero) return;

            IntPtr joystick = SDL2.GameControllerGetJoystick(controller);
            instance = joystick != IntPtr.Zero ? SDL2.JoystickInstanceID(joystick) : -1;
            if (instance < 0 || FindControllerSlot(instance) >= 0)
            {
                SDL2.GameControllerClose(controller);
                return;
            }

            Controllers[slot] = controller;
            ControllerInstances[slot] = instance;

            ControllerState state = Controller.GetState(slot);
            state.Reset();
            state.IsConnected = true;
            state.Name = SDL2.GameControllerName(controller);
            ReadControllerState(controller, state);
        }

        private static void CloseControllerByInstance(int instance)
        {
            int slot = FindControllerSlot(instance);
            if (slot < 0) return;
            SDL2.GameControllerClose(Controllers[slot]);
            Controllers[slot] = IntPtr.Zero;
            ControllerInstances[slot] = -1;
            Controller.GetState(slot).Reset();
        }

        private static void ReadControllerState(IntPtr controller, ControllerState state)
        {
            state.LeftStick = new Vector2(
                NormalizeAxis(SDL2.GameControllerGetAxis(controller, SDL2.SDL_CONTROLLER_AXIS_LEFTX)),
                NormalizeAxis(SDL2.GameControllerGetAxis(controller, SDL2.SDL_CONTROLLER_AXIS_LEFTY)));
            state.RightStick = new Vector2(
                NormalizeAxis(SDL2.GameControllerGetAxis(controller, SDL2.SDL_CONTROLLER_AXIS_RIGHTX)),
                NormalizeAxis(SDL2.GameControllerGetAxis(controller, SDL2.SDL_CONTROLLER_AXIS_RIGHTY)));
            state.LeftTrigger = NormalizeTrigger(SDL2.GameControllerGetAxis(controller, SDL2.SDL_CONTROLLER_AXIS_TRIGGERLEFT));
            state.RightTrigger = NormalizeTrigger(SDL2.GameControllerGetAxis(controller, SDL2.SDL_CONTROLLER_AXIS_TRIGGERRIGHT));
            for (int i = 0; i < 15; i++)
            {
                state.Buttons[i] = SDL2.GameControllerGetButton(controller, i) != 0;
            }
        }

        private static void ApplyAxis(ControllerState state, int axis, short value)
        {
            switch (axis)
            {
                case SDL2.SDL_CONTROLLER_AXIS_LEFTX:
                    state.LeftStick = new Vector2(NormalizeAxis(value), state.LeftStick.Y);
                    break;
                case SDL2.SDL_CONTROLLER_AXIS_LEFTY:
                    state.LeftStick = new Vector2(state.LeftStick.X, NormalizeAxis(value));
                    break;
                case SDL2.SDL_CONTROLLER_AXIS_RIGHTX:
                    state.RightStick = new Vector2(NormalizeAxis(value), state.RightStick.Y);
                    break;
                case SDL2.SDL_CONTROLLER_AXIS_RIGHTY:
                    state.RightStick = new Vector2(state.RightStick.X, NormalizeAxis(value));
                    break;
                case SDL2.SDL_CONTROLLER_AXIS_TRIGGERLEFT:
                    state.LeftTrigger = NormalizeTrigger(value);
                    break;
                case SDL2.SDL_CONTROLLER_AXIS_TRIGGERRIGHT:
                    state.RightTrigger = NormalizeTrigger(value);
                    break;
            }
        }

        private static float NormalizeAxis(short value)
        {
            return Math.Clamp(value / 32767f, -1f, 1f);
        }

        private static float NormalizeTrigger(short value)
        {
            return Math.Clamp(value / 32767f, 0f, 1f);
        }

        private static MouseButton TranslateButton(int button)
        {
            switch (button)
            {
                case SDL2.SDL_BUTTON_LEFT: return MouseButton.Left;
                case SDL2.SDL_BUTTON_MIDDLE: return MouseButton.Middle;
                case SDL2.SDL_BUTTON_RIGHT: return MouseButton.Right;
                case 4: return MouseButton.Button1;
                case 5: return MouseButton.Button2;
                default: return MouseButton.LastButton;
            }
        }

        private static Key TranslateKey(int sym)
        {
            if (sym >= 'a' && sym <= 'z') return Key.A + (sym - 'a');
            if (sym >= '0' && sym <= '9') return Key.Number0 + (sym - '0');
            switch (sym)
            {
                case 8: return Key.BackSpace;
                case 9: return Key.Tab;
                case 13: return Key.Enter;
                case 27: return Key.Escape;
                case 32: return Key.Space;
                case 39: return Key.Quote;
                case 44: return Key.Comma;
                case 45: return Key.Minus;
                case 46: return Key.Period;
                case 47: return Key.Slash;
                case 59: return Key.Semicolon;
                case 61: return Key.Plus;
                case 91: return Key.BracketLeft;
                case 92: return Key.BackSlash;
                case 93: return Key.BracketRight;
                case 96: return Key.Tilde;
                case 127: return Key.Delete;

                case 0x40000039: return Key.CapsLock;
                case 0x4000003A: return Key.F1;
                case 0x4000003B: return Key.F2;
                case 0x4000003C: return Key.F3;
                case 0x4000003D: return Key.F4;
                case 0x4000003E: return Key.F5;
                case 0x4000003F: return Key.F6;
                case 0x40000040: return Key.F7;
                case 0x40000041: return Key.F8;
                case 0x40000042: return Key.F9;
                case 0x40000043: return Key.F10;
                case 0x40000044: return Key.F11;
                case 0x40000045: return Key.F12;
                case 0x40000046: return Key.PrintScreen;
                case 0x40000047: return Key.ScrollLock;
                case 0x40000048: return Key.Pause;
                case 0x40000049: return Key.Insert;
                case 0x4000004A: return Key.Home;
                case 0x4000004B: return Key.PageUp;
                case 0x4000004C: return Key.Delete;
                case 0x4000004D: return Key.End;
                case 0x4000004E: return Key.PageDown;
                case 0x4000004F: return Key.Right;
                case 0x40000050: return Key.Left;
                case 0x40000051: return Key.Down;
                case 0x40000052: return Key.Up;
                case 0x40000053: return Key.NumLock;

                case 0x40000054: return Key.KeypadDivide;
                case 0x40000055: return Key.KeypadMultiply;
                case 0x40000056: return Key.KeypadSubtract;
                case 0x40000057: return Key.KeypadAdd;
                case 0x40000058: return Key.KeypadEnter;
                case 0x40000059: return Key.Keypad1;
                case 0x4000005A: return Key.Keypad2;
                case 0x4000005B: return Key.Keypad3;
                case 0x4000005C: return Key.Keypad4;
                case 0x4000005D: return Key.Keypad5;
                case 0x4000005E: return Key.Keypad6;
                case 0x4000005F: return Key.Keypad7;
                case 0x40000060: return Key.Keypad8;
                case 0x40000061: return Key.Keypad9;
                case 0x40000062: return Key.Keypad0;
                case 0x40000063: return Key.KeypadDecimal;

                case 0x400000E0: return Key.ControlLeft;
                case 0x400000E1: return Key.ShiftLeft;
                case 0x400000E2: return Key.AltLeft;
                case 0x400000E3: return Key.WinLeft;
                case 0x400000E4: return Key.ControlRight;
                case 0x400000E5: return Key.ShiftRight;
                case 0x400000E6: return Key.AltRight;
                case 0x400000E7: return Key.WinRight;
                default: return Key.Unknown;
            }
        }
    }
}
