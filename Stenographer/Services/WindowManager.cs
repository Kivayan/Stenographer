using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Stenographer.Services;

public class WindowManager
{
    private const int MaxWindowTextLength = 512;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public IntPtr GetForegroundWindowHandle()
    {
        var handle = GetForegroundWindow();
        return handle == GetShellWindow() ? IntPtr.Zero : handle;
    }

    public string GetActiveWindowTitle()
    {
        return GetWindowTitle(GetForegroundWindowHandle());
    }

    public string GetActiveProcessName()
    {
        return GetProcessName(GetForegroundWindowHandle());
    }

    public string GetWindowTitle(IntPtr hWnd)
    {
        if (!IsWindowHandleValid(hWnd))
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(MaxWindowTextLength);
        var length = GetWindowText(hWnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    public string GetProcessName(IntPtr hWnd)
    {
        if (!IsWindowHandleValid(hWnd))
        {
            return string.Empty;
        }

        try
        {
            GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == 0)
            {
                return string.Empty;
            }

            using var process = Process.GetProcessById((int)processId);
            return process?.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public bool TryFocusWindow(IntPtr hWnd)
    {
        if (!IsWindowHandleValid(hWnd))
        {
            return false;
        }

        var currentForeground = GetForegroundWindowHandle();
        if (currentForeground == hWnd)
        {
            return true;
        }

        var currentThreadId = GetCurrentThreadId();
        var targetThreadId = GetWindowThreadProcessId(hWnd, out _);
        var attached = false;

        try
        {
            if (currentThreadId != targetThreadId && targetThreadId != 0)
            {
                attached = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            ShowWindow(hWnd, SW_RESTORE);
            BringWindowToTop(hWnd);

            if (!SetForegroundWindow(hWnd))
            {
                return false;
            }

            SetFocus(hWnd);
            return true;
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }
    }

    public bool IsWindowHandleValid(IntPtr hWnd)
    {
        return hWnd != IntPtr.Zero && IsWindow(hWnd);
    }
}
