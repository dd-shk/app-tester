// Win32SendInput.cs
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FlowRunner
{
    internal static class Win32SendInput
    {
        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // ===== Mouse =====
        public static void Move(int x, int y) => SetCursorPos(x, y);

        public static void ClickLeft(int x, int y)
        {
            SetCursorPos(x, y);
            SendMouseNoAbs(MOUSEEVENTF_LEFTDOWN, 0);
            SendMouseNoAbs(MOUSEEVENTF_LEFTUP, 0);
        }

        public static void ClickRight(int x, int y)
        {
            SetCursorPos(x, y);
            SendMouseNoAbs(MOUSEEVENTF_RIGHTDOWN, 0);
            SendMouseNoAbs(MOUSEEVENTF_RIGHTUP, 0);
        }

        public static void Wheel(int delta)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = unchecked((uint)delta),
                        dwFlags = MOUSEEVENTF_WHEEL,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        public static void WheelAt(int x, int y, int delta)
        {
            SetCursorPos(x, y);
            Wheel(delta);
        }

        private static void SendMouseNoAbs(uint flags, uint mouseData)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = mouseData,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        // ===== Keyboard =====
        public static void KeyDown(Keys k) => SendKey((ushort)k, 0, 0);
        public static void KeyUp(Keys k) => SendKey((ushort)k, 0, KEYEVENTF_KEYUP);

        public static void KeyPress(Keys k)
        {
            KeyDown(k);
            KeyUp(k);
        }

        public static void CtrlA()
        {
            KeyDown(Keys.ControlKey);
            KeyPress(Keys.A);
            KeyUp(Keys.ControlKey);
        }

        public static void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            foreach (char ch in text)
            {
                // key down unicode
                SendKey(0, (ushort)ch, KEYEVENTF_UNICODE);
                SendKey(0, (ushort)ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
            }
        }

        private static void SendKey(ushort vk, ushort scan, uint flags)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = scan,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        // ===== Native structs =====
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }
}