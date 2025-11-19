using System;
using System.IO;

namespace MIF.Shared.Logging;

public static class FileLogger
{
    public static void AppendBlock(string? path, string content)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        File.AppendAllText(path, content);
    }

    public static void LogError(string? path, string message, Exception ex)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var block = $"[ERROR] {DateTime.UtcNow:o} - {message}\n" +
                    $"  {ex.GetType().Name}: {ex.Message}\n" +
                    $"  Stack: {ex.StackTrace}\n\n";
        File.AppendAllText(path, block);
    }
}
