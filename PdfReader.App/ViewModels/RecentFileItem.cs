using PdfReader.Core;

namespace PdfReader.App.ViewModels;

public sealed record RecentFileItem(
    string FilePath,
    string FileName,
    string DirectoryPath,
    int LastPageNumber,
    double Zoom,
    ZoomMode ZoomMode)
{
    public string SummaryText => $"Page {LastPageNumber} • {System.Math.Round(Zoom * 100):0}%";
}
