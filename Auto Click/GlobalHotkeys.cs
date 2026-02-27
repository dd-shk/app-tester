using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FlowRunner
{
    public sealed class GlobalHotkeys : IDisposable
    {
        public event Action<Keys>? KeyPressed;

        public string CheckpointHotkeyText { get; private set; } = "F12";
        public string StopHotkeyText { get; private set; } = "ESC";

        private HotkeyWindow? _window;
        private readonly Dictionary<int, Keys> _map = new();

        private const int WM_HOTKEY = 0x0312;

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int ERR_HOTKEY_ALREADY_REGISTERED = 1409;

        // ---- ESC hook ----
        private IntPtr _kbHook = IntPtr.Zero;
        private LowLevelKeyboardProc? _kbProc;
        private bool _escDown;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_ESCAPE = 0x1B;

        public void Start()
        {
            if (_window != null) return;

            _window = new HotkeyWindow();
            _window.Hotkey += OnHotkeyMessage;

            // F9/F10/F11
            TryRegister(Keys.F9, 1, 0, "F9");
            TryRegister(Keys.F10, 2, 0, "F10");
            TryRegister(Keys.F11, 3, 0, "F11");
            TryRegister(Keys.F7, 6, 0, "F7");

            // F12 -> Ctrl+F12 -> Ctrl+Shift+F12
            if (TryRegister(Keys.F12, 4, 0, "F12")) CheckpointHotkeyText = "F12";
            else if (TryRegister(Keys.F12, 4, MOD_CONTROL, "Ctrl+F12")) CheckpointHotkeyText = "Ctrl+F12";
            else if (TryRegister(Keys.F12, 4, MOD_CONTROL | MOD_SHIFT, "Ctrl+Shift+F12")) CheckpointHotkeyText = "Ctrl+Shift+F12";
            else CheckpointHotkeyText = "F12 (unavailable)";

            // ✅ ESC با hook (نه RegisterHotKey)
            StartEscHook();
        }

        private void StartEscHook()
        {
            StopHotkeyText = "ESC";

            _kbProc = KeyboardHookCallback;
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, GetModuleHandle(null), 0);

            if (_kbHook == IntPtr.Zero)
            {
                // اگر hook نشد، fallback: F8 به عنوان Stop
                AppLog.Warn($"ESC hook failed. Win32Error={Marshal.GetLastWin32Error()} - fallback Stop=F8");
                StopHotkeyText = "F8";
                TryRegister(Keys.F8, 5, 0, "F8");
            }
            else
            {
                AppLog.Info("ESC hook installed.");
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    int vkCode = Marshal.ReadInt32(lParam);

                    if (vkCode == VK_ESCAPE)
                    {
                        if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && !_escDown)
                        {
                            _escDown = true;
                            KeyPressed?.Invoke(Keys.Escape);
                        }
                        else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                        {
                            _escDown = false;
                        }
                    }
                }
            }
            catch { }

            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private bool TryRegister(Keys key, int id, uint modifiers, string displayText)
        {
            if (_window == null) return false;

            try { UnregisterHotKey(_window.Handle, id); } catch { }

            bool ok = RegisterHotKey(_window.Handle, id, modifiers | MOD_NOREPEAT, (uint)key);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERR_HOTKEY_ALREADY_REGISTERED)
                {
                    AppLog.Warn($"Hotkey already registered elsewhere: {displayText} (id={id})");
                    return false;
                }

                AppLog.Warn($"RegisterHotKey failed: {displayText} (id={id}) Win32Error={err}");
                return false;
            }

            _map[id] = key;
            AppLog.Info($"Hotkey registered: {displayText} (id={id})");
            return true;
        }

        private void OnHotkeyMessage(int id)
        {
            if (_map.TryGetValue(id, out var k))
                KeyPressed?.Invoke(k);
        }

        public void Dispose()
        {
            try
            {
                if (_window != null)
                {
                    foreach (var kv in _map)
                    {
                        try { UnregisterHotKey(_window.Handle, kv.Key); } catch { }
                    }
                    _map.Clear();

                    _window.Hotkey -= OnHotkeyMessage;
                    _window.DestroyHandle();
                    _window = null;
                }

                if (_kbHook != IntPtr.Zero)
                {
                    try { UnhookWindowsHookEx(_kbHook); } catch { }
                    _kbHook = IntPtr.Zero;
                }

                _kbProc = null;
            }
            catch { }
        }

        private sealed class HotkeyWindow : NativeWindow
        {
            public event Action<int>? Hotkey;

            public HotkeyWindow() => CreateHandle(new CreateParams());

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    Hotkey?.Invoke(id);
                }
                base.WndProc(ref m);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // keyboard hook
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}