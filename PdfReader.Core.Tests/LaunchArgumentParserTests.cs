using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class LaunchArgumentParserTests
{
    private static string GetSamplePdfPath()
    {
        string outputSamplePath = Path.Combine(AppContext.BaseDirectory, "samples", "text.pdf");
        if (File.Exists(outputSamplePath))
        {
            return outputSamplePath;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "text.pdf"));
    }

    [TestMethod]
    public void TryResolvePdfFilePath_WithValidPdfArgument_ReturnsNormalizedFullPath()
    {
        // Arrange
        string samplePath = GetSamplePdfPath();
        string[] args = ["PdfReader.App.exe", "--flag", $"\"{samplePath}\""];

        // Act
        string? resolved = LaunchArgumentParser.TryResolvePdfFilePath(args);

        // Assert
        Assert.AreEqual(Path.GetFullPath(samplePath), resolved);
    }

    [TestMethod]
    public void TryResolvePdfFilePath_WithMissingOrNonPdfArguments_ReturnsNull()
    {
        // Arrange
        string[] noArgs = ["PdfReader.App.exe"];
        string[] nonExistentArgs = ["PdfReader.App.exe", @"C:\nonexistent\missing_file_12345.pdf"];
        string[] nonPdfArgs = ["PdfReader.App.exe", @"C:\Windows\win.ini"];

        // Act & Assert
        Assert.IsNull(LaunchArgumentParser.TryResolvePdfFilePath(null));
        Assert.IsNull(LaunchArgumentParser.TryResolvePdfFilePath(noArgs));
        Assert.IsNull(LaunchArgumentParser.TryResolvePdfFilePath(nonExistentArgs));
        Assert.IsNull(LaunchArgumentParser.TryResolvePdfFilePath(nonPdfArgs));
    }
}
