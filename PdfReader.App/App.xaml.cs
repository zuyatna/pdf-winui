using System;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using PdfReader.Core;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace PdfReader.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        _window = mainWindow;
        mainWindow.Activate();

        string? startupPdfPath = ResolveStartupPdfPath();
        if (!string.IsNullOrWhiteSpace(startupPdfPath))
        {
            mainWindow.OpenStartupDocument(startupPdfPath);
        }
    }

    private static string? ResolveStartupPdfPath()
    {
        try
        {
            AppActivationArguments activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activatedArgs.Kind == ExtendedActivationKind.File &&
                activatedArgs.Data is IFileActivatedEventArgs fileArgs &&
                fileArgs.Files is not null)
            {
                for (int i = 0; i < fileArgs.Files.Count; i++)
                {
                    if (fileArgs.Files[i] is IStorageItem item &&
                        !string.IsNullOrWhiteSpace(item.Path) &&
                        item.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        return item.Path;
                    }
                }
            }
        }
        catch
        {
            // Fall back to command-line argument parsing when unpackaged or activation context is unavailable.
        }

        return LaunchArgumentParser.TryResolvePdfFilePath(Environment.GetCommandLineArgs());
    }
}
