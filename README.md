# MIF 平台结构概览

该仓库已按照“业务源码 / 工具脚本 / 文档资料”三层结构重整，便于并行演进 DOM 导出器与辅助工具链。

## 目录分层
- `docs/`：MIF 项目背景、决策记录与交易策略文档。
- `src/`：面向生产的 Indicator 代码。当前包含 DOM 导出器与 Cluster 空壳项目。
- `tests/`：预留的自动化测试入口（待补充）。
- `tools/`：历史导出器及类型转储等工具项目。
- `externals/`：第三方或闭源依赖（例如 ATAS 指标 DLL）。

## 模块说明
### `src/MIF.AtasIndicator.DOM`
- `Exporter/`：现行 DOM 导出核心实现（`DomExporterV14.cs`）。
- `Infrastructure/`：ATAS 绑定与运行时辅助逻辑。
- `OldVersions/`：按版本归档的历史实现（V12/17/18/19/20/21）与旧版绑定文件，便于回溯逻辑差异。
- `MIF.AtasIndicator.DOM.csproj`：DOM Indicator 项目文件，引用共享基础库。

### `src/MIF.Shared`
- `IO/JsonlWriter.cs`：提供 JSONL 批量写入封装，确保导出路径与命名一致。
- `Logging/FileLogger.cs`：统一处理 Alive 文件日志写入与异常记录。
- `MIF.Shared.csproj`：供 DOM、Cluster 与工具项目复用的基础库。

### `src/MIF.AtasIndicator.Cluster`
- `ClusterExporterV1.cs`：继承 `Indicator` 的占位实现，包含待接入 Footprint/簇导出逻辑的 TODO。
- `MIF.AtasIndicator.Cluster.csproj`：Cluster 指标工程文件，当前标记为“占位阶段”，待后续补全。

### `tools/`
- `MIF.AtasExporter.Legacy/`：早期导出器控制台程序，保留以兼容旧流程。
- `MIF.TypeDump/`：类型转储工具，用于快速获取 ATAS API 暴露的类型信息。

## 后续建议
1. 更新各 `.csproj` 的 `HintPath` 与 `ProjectReference`，确保指向新的相对路径布局。
2. 在 `tests/` 目录补充单元/集成测试，覆盖 DOM 导出边界场景。
3. 在 `src/MIF.AtasIndicator.Cluster/` 中实现最小可用功能，再与 DOM 项目共享公共组件。

## 构建与发布流程
- `dotnet build src/MIF.AtasIndicator.DOM/MIF.AtasIndicator.DOM.csproj -c Debug`：用于日常开发验证；若需要导出 Release 版 DLL，将 `-c Debug` 替换为 `-c Release`。
- `dotnet build src/MIF.Shared/MIF.Shared.csproj`：构建共享基础库，供 DOM/Cluster/工具项目引用。
- `dotnet build src/MIF.AtasIndicator.Cluster/MIF.AtasIndicator.Cluster.csproj`：当前为占位工程，可与 DOM 项目分离编译，待功能落地后纳入统一解决方案。
- `dotnet build tools/MIF.TypeDump/MIF.TypeDump.csproj`：调试 ATAS API 时按需运行；默认不纳入主构建。
- `dotnet build tools/MIF.AtasExporter.Legacy/MIF.AtasExporter.csproj`：仅在需要兼容旧导出流程时手动执行，建议在集成解决方案中默认取消勾选。

如果需要一次性编译/测试所有模块，可直接使用根目录已提供的 `MIF.sln`：
```bash
dotnet build MIF.sln -c Debug
dotnet test MIF.sln
```

### 测试与打包
- `dotnet test tests/MIF.AtasIndicator.DOM.Tests/MIF.AtasIndicator.DOM.Tests.csproj`：执行 DOM 导出器元数据与行为测试。
- `dotnet pack`（若需要发布 NuGet/内部包，可在对应项目中添加打包元数据后执行）。

### CI/CD 提示
- 在 CI 中固定使用 `dotnet restore` → `dotnet build -c Release` → `dotnet test` 的顺序，确保与本地一致。
- 通过 `DOTNET_CLI_TELEMETRY_OPTOUT=1` 等环境变量统一 CLI 行为，并在 CI 环境中缓存 `~/.nuget/packages` 以加速构建。
