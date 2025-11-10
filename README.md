# MIF ATAS Exporter

This repository contains the custom ATAS indicators that generate JSONL market data exports for the MIF strategy stack. The latest indicator (`MIF Exporter V20`) keeps its export logic inside the indicator class (same approach as the stable v18 release) so ATAS only needs to load a single DLL.

## Default export location (V20)
The v20 indicator automatically resolves the output path when the `Output Directory` input is left blank:

- Directory: `%USERPROFILE%\Documents\MIF\atas_export`
- File name pattern: `bars_YYYYMMDD.jsonl` (rotates daily inside the export folder)

The indicator ensures the directory exists, then appends one JSON object per bar to the file.

### Custom output directory
Set the `Output Directory` parameter in the indicator’s property panel if you need the JSONL file in a different folder. The indicator will create the directory (if required) and write the export using the same filename pattern inside that folder.

### Ensuring data is flushed
Exports happen automatically when a bar closes (and again during indicator disposal for any buffered bars). The writer keeps its stream open with `AutoFlush = true`, so records should appear on disk immediately after each write.
