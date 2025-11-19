using System;
using System.Collections.Generic;
using System.IO;

namespace MIF.Shared.IO;

public static class JsonlWriter
{
    public static void AppendRecords(string baseDirectory, string filePrefix, DateTime utcTimestamp, IEnumerable<string> records)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("Base directory is required", nameof(baseDirectory));
        }

        if (!Directory.Exists(baseDirectory))
        {
            Directory.CreateDirectory(baseDirectory);
        }

        var dateKey = utcTimestamp.ToString("yyyyMMdd");
        var fileName = string.IsNullOrWhiteSpace(filePrefix)
            ? $"{dateKey}.jsonl"
            : $"{filePrefix}_{dateKey}.jsonl";

        var destination = Path.Combine(baseDirectory, fileName);
        File.AppendAllLines(destination, records);
    }
}
