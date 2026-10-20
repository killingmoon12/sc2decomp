using System;
using OpenTK.Input;

namespace OpenTK.Platform.Linux
{
    internal sealed class LinuxKeyboardDriver : IKeyboardDriver2
    {
        public void Poll() { }
        public KeyboardState GetState() => LinuxInput.GetKeyboardState();
        public KeyboardState GetState(int index) => LinuxInput.GetKeyboardState();
        public string GetDeviceName(int index) => "Linux Keyboard";
    }
}
