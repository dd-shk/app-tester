// Win32Hooks.cs
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FlowRunner
{
    internal static class Win32Hooks
    {
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MOUSEWHEEL = 0x020A;

        private const int WH_MOUSE_LL = 14;

        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelMouseProc? _proc;

        // x, y, msg, data (for wheel: delta)
        public static event Action<int, int, int, int>? MouseEvent;

        public static void Start()
        {
            if (_hookId != IntPtr.Zero) return;

            _proc = HookCallback;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);

            if (_hookId == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx(WH_MOUSE_LL) failed");
        }

        public static void Stop()
        {
            if (_hookId == IntPtr.Zero) return;

            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _proc = null;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();

                if (msg == WM_MOUSEMOVE ||
                    msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP ||
                    msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP ||
                    msg == WM_MOUSEWHEEL)
                {
                    var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                    int data = 0;
                    if (msg == WM_MOUSEWHEEL)
                    {
                        // wheel delta is in HIWORD(mouseData) as signed short
                        data = (short)((hs.mouseData >> 16) & 0xFFFF);
                    }
                    else
                    {
                        data = unchecked((int)hs.mouseData);
                    }

                    MouseEvent?.Invoke(hs.pt.x, hs.pt.y, msg, data);
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    }
}