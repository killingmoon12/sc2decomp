using System;
using OpenTK.Input;

namespace OpenTK.Platform.Linux
{
    internal sealed class LinuxMouseDriver : IMouseDriver2, IMouseCursorController
    {
        public void Poll() { }
        public MouseState GetState() => LinuxInput.GetMouseState();
        public MouseState GetState(int index) => LinuxInput.GetMouseState();
        public void SetPosition(double x, double y) => LinuxInput.SetMousePosition(x, y);
        public void SetPointerCapture(bool captured) => LinuxInput.SetPointerCapture(captured);
    }
}
