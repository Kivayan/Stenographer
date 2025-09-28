using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace Stenographer.Core;

public class TextInsertion
{
    public enum InsertionMethod
    {
        None,
        UiAutomation,
        Clipboard,
    }

    public string LastDiagnosticMessage { get; private set; } = string.Empty;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
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
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    public InsertionMethod InsertText(string text)
    {
        LastDiagnosticMessage = string.Empty;

        if (string.IsNullOrEmpty(text))
        {
            return InsertionMethod.None;
        }

        if (TryInsertWithUIAutomation(text))
        {
            return InsertionMethod.UiAutomation;
        }

        if (InsertViaClipboard(text))
        {
            return InsertionMethod.Clipboard;
        }

        if (string.IsNullOrEmpty(LastDiagnosticMessage))
        {
            LastDiagnosticMessage = "UI Automation and clipboard insertion failed.";
        }

        return InsertionMethod.None;
    }

    private static bool TryInsertWithUIAutomation(string text)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null)
            {
                return false;
            }

            if (
                element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObj)
                && valuePatternObj is ValuePattern valuePattern
            )
            {
                if (
                    !valuePattern.Current.IsReadOnly
                    && string.IsNullOrEmpty(valuePattern.Current.Value)
                )
                {
                    valuePattern.SetValue(text);
                    return true;
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            // Target element disappeared between focus capture and insertion
        }
        catch (InvalidOperationException)
        {
            // Element does not support the requested pattern operation
        }
        catch (UnauthorizedAccessException)
        {
            // UIA denied access (e.g., protected window)
        }
        catch (COMException)
        {
            // UIA infrastructure error, fall back to simulated input
        }

        return false;
    }

    private bool InsertViaClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            LastDiagnosticMessage = "Clipboard insertion skipped due to empty text.";
            return false;
        }

        var reservation = PrepareClipboardForInsertion(text);
        if (!reservation.ClipboardReady)
        {
            LastDiagnosticMessage = reservation.Diagnostics;
            return false;
        }

        var inputs = new[]
        {
            CreateVirtualKeyInput(VK_CONTROL, keyUp: false),
            CreateVirtualKeyInput(VK_V, keyUp: false),
            CreateVirtualKeyInput(VK_V, keyUp: true),
            CreateVirtualKeyInput(VK_CONTROL, keyUp: true),
        };

        var sent = SendInput((uint)inputs.Length, inputs, InputSize);
        var lastError = Marshal.GetLastWin32Error();
        System.Diagnostics.Debug.WriteLine(
            $"[Clipboard] SendInput Ctrl+V -> sent={sent}, lastError={lastError}"
        );

        if (sent != inputs.Length)
        {
            LastDiagnosticMessage =
                sent == 0
                    ? $"SendInput failed (error {lastError})."
                    : $"SendInput only injected {sent} of {inputs.Length} events.";
            return false;
        }

        ScheduleClipboardRestore(reservation, text);
        return true;
    }

    private static INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        var flags = keyUp ? KEYEVENTF_KEYUP : 0u;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            Union = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }

    private ClipboardReservation PrepareClipboardForInsertion(string text)
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        var previousClipboard = string.Empty;
        var hadClipboardText = false;
        var diagnostics = string.Empty;
        var clipboardReady = false;

        dispatcher.Invoke(() =>
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    previousClipboard = Clipboard.GetText();
                    hadClipboardText = true;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[Clipboard] Before set - hadText={hadClipboardText}, prev='{previousClipboard}'"
                );
            }
            catch (Exception ex)
            {
                previousClipboard = string.Empty;
                hadClipboardText = false;
                System.Diagnostics.Debug.WriteLine(
                    $"[Clipboard] Contains/Get failed: {ex.Message}"
                );
            }

            try
            {
                Clipboard.SetDataObject(text, true);
                clipboardReady =
                    Clipboard.ContainsText()
                    && string.Equals(Clipboard.GetText(), text, StringComparison.Ordinal);

                System.Diagnostics.Debug.WriteLine(
                    $"[Clipboard] SetDataObject succeeded={clipboardReady}. new='{text}'"
                );

                if (!clipboardReady)
                {
                    diagnostics = "Clipboard verification failed after SetDataObject.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Clipboard] SetText failed: {ex.Message}");
                diagnostics = $"Clipboard.SetText failed: {ex.Message}";
                clipboardReady = false;
            }
        });

        if (!clipboardReady && string.IsNullOrEmpty(diagnostics))
        {
            diagnostics = "Clipboard not ready after SetText.";
        }

        return new ClipboardReservation(
            dispatcher,
            previousClipboard,
            hadClipboardText,
            clipboardReady,
            diagnostics
        );
    }

    private static void ScheduleClipboardRestore(
        ClipboardReservation reservation,
        string insertedText
    )
    {
        var dispatcher = reservation.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _ = dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(2000).ConfigureAwait(true);

            try
            {
                if (!Clipboard.ContainsText())
                {
                    return;
                }

                var currentClipboard = Clipboard.GetText();

                if (!string.Equals(currentClipboard, insertedText, StringComparison.Ordinal))
                {
                    return;
                }

                if (reservation.HadClipboardText)
                {
                    Clipboard.SetText(reservation.PreviousClipboard);
                }
                else
                {
                    Clipboard.Clear();
                }
            }
            catch
            {
                // ignore restoration failures
            }
        });
    }

    private readonly struct ClipboardReservation
    {
        public ClipboardReservation(
            Dispatcher dispatcher,
            string previousClipboard,
            bool hadClipboardText,
            bool clipboardReady,
            string diagnostics
        )
        {
            Dispatcher = dispatcher;
            PreviousClipboard = previousClipboard;
            HadClipboardText = hadClipboardText;
            ClipboardReady = clipboardReady;
            Diagnostics = diagnostics;
        }

        public Dispatcher Dispatcher { get; }
        public string PreviousClipboard { get; }
        public bool HadClipboardText { get; }
        public bool ClipboardReady { get; }
        public string Diagnostics { get; }
    }
}
