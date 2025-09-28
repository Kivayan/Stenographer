using System;
using System.IO;
using System.Text.Json;
using Stenographer.Models;

namespace Stenographer.Services;

public class ConfigurationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    private readonly string _configDirectory;
    private readonly string _configPath;

    public ConfigurationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var newDir = Path.Combine(appData, "Stenographer");
        var legacyDir = Path.Combine(appData, "Stengorapher");

        // Prefer new directory name; migrate legacy if present.
        try
        {
            if (!Directory.Exists(newDir) && Directory.Exists(legacyDir))
            {
                Directory.CreateDirectory(newDir);
                var legacyConfig = Path.Combine(legacyDir, "config.json");
                if (File.Exists(legacyConfig))
                {
                    File.Copy(legacyConfig, Path.Combine(newDir, "config.json"), overwrite: true);
                }
            }
        }
        catch
        {
            // Non-fatal; fall back to whichever exists
        }

        _configDirectory = Directory.Exists(newDir) ? newDir : legacyDir;
        _configPath = Path.Combine(_configDirectory, "config.json");
    }

    public AppConfiguration Configuration { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(_configPath))
        {
            Configuration = new AppConfiguration();
            return;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var configuration = JsonSerializer.Deserialize<AppConfiguration>(
                json,
                SerializerOptions
            );

            Configuration = configuration ?? new AppConfiguration();
        }
        catch
        {
            Configuration = new AppConfiguration();
        }

        Configuration.Hotkey ??= HotkeySettings.CreateDefault();
        Configuration.TranscriptionLanguage ??= string.Empty;
        Configuration.SelectedModelFileName ??= string.Empty;
    }

    public void Save()
    {
        Directory.CreateDirectory(_configDirectory);

        Configuration.Hotkey ??= HotkeySettings.CreateDefault();
        Configuration.TranscriptionLanguage ??= string.Empty;
        Configuration.SelectedModelFileName ??= string.Empty;

        var json = JsonSerializer.Serialize(Configuration, SerializerOptions);
        File.WriteAllText(_configPath, json);
    }
}
