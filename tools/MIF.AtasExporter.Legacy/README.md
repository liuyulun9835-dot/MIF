# MIF.AtasExporter.Legacy

该控制台项目保留旧版导出流程，仅在兼容早期部署或回溯历史行为时启用。

## 构建建议
- 默认情况下不纳入聚合解决方案的构建配置；需要时可手动执行：
  ```bash
  dotnet build tools/MIF.AtasExporter.Legacy/MIF.AtasExporter.csproj -c Release
  ```
- 若将其加入根级 `MIF.sln`，建议在 Visual Studio/CI 中取消「Build」复选框，避免增加日常编译时间。

## 依赖
- 与 DOM 项目共用的外部 DLL（如 ATAS.Indicators.dll）应同样从 `externals/ATAS/` 引用，并在 `.csproj` 中使用相对 `HintPath`。

## 后续思路
- 当 Legacy 功能完全由 DOM 导出器取代后，可考虑将该项目从解决方案中移除，仅作为仓库历史存档存在。
