using System;
using System.IO;
using System.Text.Json;

namespace PdfReader.App.Services;

public sealed class ReaderSettingsService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsFilePath;

    public ReaderSettingsService(string? settingsFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(settingsFilePath))
        {
            _settingsFilePath = settingsFilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsFilePath = Path.Combine(appData, "PdfReader", "settings.json");
        }
    }

    public ReaderSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new ReaderSettings();
            }

            string json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<ReaderSettings>(json, s_jsonOptions) ?? new ReaderSettings();
        }
        catch
        {
            return new ReaderSettings();
        }
    }

    public void Save(ReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            string? directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, s_jsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignore persistence errors on restricted filesystems.
        }
    }
}
