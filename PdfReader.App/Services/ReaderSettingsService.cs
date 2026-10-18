using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PdfReader.App.Services;

public sealed class ReaderSettingsService
{
    private readonly string _settingsFilePath;
    private readonly object _saveLock = new();
    private Task _pendingSave = Task.CompletedTask;
    private Exception? _lastSaveError;

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
            ReaderSettings settings = JsonSerializer.Deserialize(json, ReaderSettingsJsonContext.Default.ReaderSettings)
                ?? new ReaderSettings();
            // Source-generated init-only setters can receive null for fields absent in older JSON.
            return (settings with
            {
                RecentDocuments = settings.RecentDocuments ?? [],
                BookmarkedDocuments = settings.BookmarkedDocuments ?? [],
            }).WithLegacyPalettesMigrated();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load reader settings: {ex.Message}");
            return new ReaderSettings();
        }
    }

    public void Save(ReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_saveLock)
        {
            Task previous = _pendingSave;
            _pendingSave = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                await WriteSettingsAsync(settings).ConfigureAwait(false);
            });
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (_saveLock)
        {
            pending = _pendingSave;
        }

        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_lastSaveError is Exception error)
        {
            throw new IOException("Could not save reader settings.", error);
        }
    }

    private async Task WriteSettingsAsync(ReaderSettings settings)
    {
        string temporaryPath = _settingsFilePath + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, ReaderSettingsJsonContext.Default.ReaderSettings);
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);
            File.Move(temporaryPath, _settingsFilePath, overwrite: true);
            _lastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _lastSaveError = ex;
            System.Diagnostics.Debug.WriteLine($"Could not save reader settings: {ex.Message}");
        }
    }
}
