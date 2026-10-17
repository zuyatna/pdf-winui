using System.Text.Json.Serialization;

namespace PdfReader.App.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ReaderSettings))]
internal sealed partial class ReaderSettingsJsonContext : JsonSerializerContext;
