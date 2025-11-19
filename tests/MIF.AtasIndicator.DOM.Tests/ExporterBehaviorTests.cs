using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MIF.AtasIndicator.DOM.Exporter;
using MIF.Shared.IO;
using Xunit;

namespace MIF.AtasIndicator.DOM.Tests;

public class ExporterBehaviorTests
{
    [Fact]
    public void JsonWriter_ShouldAppendJsonlRecords()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var records = new List<string>
            {
                JsonSerializer.Serialize(new { version = "v14", value = 1 }),
                JsonSerializer.Serialize(new { version = "v14", value = 2 })
            };

            JsonlWriter.AppendRecords(tempDir, "bars", DateTime.UtcNow, records);

            var files = Directory.GetFiles(tempDir, "bars_*.jsonl");
            Assert.Single(files);

            var lines = File.ReadAllLines(files[0]);
            Assert.Equal(2, lines.Length);

            var doc = JsonDocument.Parse(lines[0]);
            Assert.Equal("v14", doc.RootElement.GetProperty("version").GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
