# MIF ATAS Exporter

This repository contains the custom ATAS indicators that generate JSONL market data exports for the MIF strategy stack. The latest indicator (`MIF Exporter V19`) bundles its data models and exporter logic into the same DLL that ATAS loads.

## Default export location (V19)
The v19 indicator automatically resolves the output path when the `Output Directory` input is left blank:

- Directory: `%USERPROFILE%\Documents\MIF\atas_export`
- File name pattern: `<symbol>_<timeframe>_v19.jsonl`

The indicator ensures the directory exists, then appends one JSON object per bar to the file. The implementation lives in `MifExporterV19.EnsureExporter()`, which builds the file path and passes it to `JSONLExporter` for writing.

### Custom output directory
Set the `Output Directory` parameter in the indicator’s property panel if you need the JSONL file in a different folder. The indicator will create the directory (if required) and write the export using the same filename pattern inside that folder.

### Ensuring data is flushed
Exports happen automatically when a bar closes (and again during indicator disposal for any buffered bars). The `JSONLExporter` keeps the stream open with `AutoFlush = true`, so records should appear on disk immediately after each write.
