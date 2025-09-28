using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Stenographer.Services;

public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xBEEF;
    private const int WmHotkey = 0x0312;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;
    private const int WhKeyboardLl = 13;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly LowLevelKeyboardProc _keyboardProc;
    private IntPtr _keyboardHookHandle = IntPtr.Zero;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private IntPtr _windowHandle = IntPtr.Zero;
    private bool _isRegistered;
    private bool _isHotkeyActive;
    private ModifierKeys _currentModifiers;
    private Key _currentKey;

    public HotkeyManager()
    {
        _keyboardProc = KeyboardHookCallback;
    }

    public event Action HotkeyPressed;
    public event Action HotkeyReleased;

    public bool RegisterHotkey(IntPtr hwnd, ModifierKeys modifiers, Key key)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException(
                "Window handle must be set before registering a hotkey.",
                nameof(hwnd)
            );
        }

        if (key == Key.None)
        {
            return false;
        }

        if (!EnsureKeyboardHook())
        {
            return false;
        }

        UnregisterHotkeys();

        var modifierFlags = ConvertModifiers(modifiers) | ModNoRepeat;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (!RegisterHotKey(hwnd, HotkeyId, modifierFlags, virtualKey))
        {
            return false;
        }

        _windowHandle = hwnd;
        _isRegistered = true;
        _isHotkeyActive = false;
        _currentModifiers = modifiers;
        _currentKey = key;
        return true;
    }

    public void UnregisterHotkeys()
    {
        if (_isRegistered && _windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
        }

        _isRegistered = false;
        _isHotkeyActive = false;
        _windowHandle = IntPtr.Zero;
    }

    public IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;

            if (!_isHotkeyActive)
            {
                _isHotkeyActive = true;
                HotkeyPressed?.Invoke();
            }
        }

        return IntPtr.Zero;
    }

    public (ModifierKeys Modifiers, Key Key)? CurrentHotkey =>
        _isRegistered ? (_currentModifiers, _currentKey) : null;

    public void Dispose()
    {
        UnregisterHotkeys();

        if (_keyboardHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    private bool EnsureKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            return true;
        }

        var moduleHandle = GetModuleHandle(null);
        _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);

        return _keyboardHookHandle != IntPtr.Zero;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRegistered && _isHotkeyActive)
        {
            var message = wParam.ToInt32();

            if (message is WmKeyup or WmSyskeyup)
            {
                var data = Marshal.PtrToStructure<KbdllHookStruct>(lParam);
                var key = KeyInterop.KeyFromVirtualKey(data.vkCode);

                if (IsMainHotkeyKey(key) || IsModifierForCurrentHotkey(key))
                {
                    _isHotkeyActive = false;
                    HotkeyReleased?.Invoke();
                }
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private bool IsMainHotkeyKey(Key key)
    {
        return key == _currentKey;
    }

    private bool IsModifierForCurrentHotkey(Key key)
    {
        return key switch
        {
            Key.LeftCtrl or Key.RightCtrl => (_currentModifiers & ModifierKeys.Control) != 0,
            Key.LeftShift or Key.RightShift => (_currentModifiers & ModifierKeys.Shift) != 0,
            Key.LeftAlt or Key.RightAlt => (_currentModifiers & ModifierKeys.Alt) != 0,
            Key.LWin or Key.RWin => (_currentModifiers & ModifierKeys.Windows) != 0,
            _ => false,
        };
    }

    private static uint ConvertModifiers(ModifierKeys modifiers)
    {
        uint result = 0;

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            result |= ModAlt;
        }

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            result |= ModControl;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            result |= ModShift;
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            result |= ModWin;
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
}
