using System.Collections.Generic;
using System.Windows.Input;

namespace Stenographer.Models;

public class HotkeySettings
{
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Shift;
    public Key Key { get; set; } = Key.Space;

    public static HotkeySettings CreateDefault()
    {
        return new HotkeySettings();
    }

    public HotkeySettings Clone()
    {
        return new HotkeySettings { Modifiers = Modifiers, Key = Key };
    }

    public override string ToString()
    {
        return Format(this);
    }

    public static string Format(HotkeySettings settings)
    {
        return Format(settings.Modifiers, settings.Key);
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyDisplay(key));

        return string.Join("+", parts);
    }

    private static string GetKeyDisplay(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return key.ToString();
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return $"Num{key - Key.NumPad0}";
        }

        return key switch
        {
            Key.None => "None",
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.Tab => "Tab",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemTilde => "`",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.Left => "Left Arrow",
            Key.Right => "Right Arrow",
            Key.Up => "Up Arrow",
            Key.Down => "Down Arrow",
            _ => key.ToString(),
        };
    }
}
