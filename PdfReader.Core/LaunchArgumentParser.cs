using System;
using System.Collections.Generic;
using System.IO;

namespace PdfReader.Core;

public static class LaunchArgumentParser
{
    public static string? TryResolvePdfFilePath(IReadOnlyList<string>? commandLineArgs)
    {
        if (commandLineArgs is null || commandLineArgs.Count <= 1)
        {
            return null;
        }

        for (int i = 1; i < commandLineArgs.Count; i++)
        {
            string? candidate = commandLineArgs[i];
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string trimmed = candidate.Trim().Trim('"');
            if (trimmed.Length == 0 || trimmed.StartsWith('-'))
            {
                continue;
            }

            if (!trimmed.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                string fullPath = Path.GetFullPath(trimmed);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignore malformed command-line path arguments.
            }
        }

        return null;
    }
}
