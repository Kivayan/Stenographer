namespace Stenographer.Models;

public class AppConfiguration
{
    public HotkeySettings Hotkey { get; set; } = HotkeySettings.CreateDefault();
    public string TranscriptionLanguage { get; set; } = string.Empty;
    public string SelectedModelFileName { get; set; } = string.Empty;
}
