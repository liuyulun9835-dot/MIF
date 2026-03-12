# MIF 平台结构概览

该仓库已按照“业务源码 / 工具脚本 / 文档资料”三层结构重整，便于并行演进 DOM 导出器与辅助工具链。

## 目录分层
- `docs/`：MIF 项目背景、决策记录与交易策略文档。详见下方文档索引。
- `src/`：面向生产的 Indicator 代码。当前包含 DOM 导出器与 Cluster 空壳项目。
- `tests/`：预留的自动化测试入口（待补充）。
- `tools/`：历史导出器及类型转储等工具项目。
- `externals/`：第三方或闭源依赖（例如 ATAS 指标 DLL）。

## 文档索引（`docs/`）

| 文件名 | 说明 |
|--------|------|
| `MIF_理论哲学_v2_4.md` | MIF 理论哲学主文档（v2.4：QM 形式化完备脱钩；演化方程重写为 Fokker-Planck；Hawkes 突破判别协议集成；七场景分析） |
| `MIF_1_公式总结_精简版_v2_4.md` | 核心公式速查（v2.4：信息-时间权衡原理；新增 Hawkes 公式节） |
| `MIF_2_概念澄清备案_精简版_v2_4.md` | 理论边界与概念辨析（v2.4：新增鞅/自激概念辨析；IS 双重确认） |
| `MIF_Strategy_t2.md` | MIF 交易策略（v2，鞅/Hawkes 修正版，替代 t1） |
| `MIF_Architecture_Decision_Records.md` | 架构决策记录（ADR） |
| `MIF_Trading_Metacognition_Whitepaper.md` | 交易元认知白皮书 |
| `MIF_MSI_Dynamic_Theory.md` | MSI 动态理论 |
| `MIF_Strategy_Relationalism_Supplement_v2.md` | 策略关系论补充（v2，含 Hawkes 信条扩展） |
| `ADR-004_V14_Final_DOM_only.md` | V14 Final DOM-only 决策记录 |
| `MIF_Work_Status_Snapshot_v3.md` | 工作状态快照（v3：v2.4理论重写 + 数据管线迁移） |

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

## 更新日志

### v2.3（2026-03-11）
- **全局符号替换**：配置态符号 Ψ → Γ，与量子力学波函数彻底脱钩
- **不确定性界**：ℏ_m → C_m，推导路径改为 Fisher 信息量 / Cramér-Rao 下界，删除"类比 Heisenberg"的表述
- **坍缩语言清除**：Dirac 括号记法、"波函数坍缩"等量子力学术语全部替换为概率质量集中 / 结构锁定语言
- **演化方程**：薛定谔形式 i∂U/∂t = H_eff U → 随机过程生成元形式 ∂U/∂t = −H_eff U
- **传播子**：Feynman 路径积分 → 马尔可夫转移概率核 K(Γ_f, Γ_i; Δt)
- **新增**：第三章附节"IS/UIS 判别层：Hawkes 过程接口定义"，含四阶段状态机与三个接口变量的精确定义

### v2.4（2026-03-12）
- **定理降级**："测不准原理"降级为"信息-时间权衡原理"（经验性原理，非定理）
- **演化方程重写**：H_eff 哈密顿量分解 → Fokker-Planck 算子 + 转移概率核
- **Hilbert 空间声明移除**：替换为"有限维实内积空间"
- **坍缩/锁定术语清除**："结构锁定"→"regime transition"
- **Hawkes 提升**：从 Section III 附件层移至 Section V.6，定义完整判别流水线和 I(Γ)-n 双重确认接口
- **状态/预测分离**：Section 5.1 显式区分 Γ(t)（可观测状态）和 {P_i(t)}（预测分布）
- **符号冲突修复**：Γ_critical → γ_rate
- **exp(iθ) 注释**：明确 Kuramoto 序参量的复指数是 Fourier 相位表示，非量子概率幅
- **场景分析**：新增 Section 8.3，七个做市商-散户博弈场景的完整 MIF 映射
- **策略重写**：MIF_Strategy_t1 → t2（引入 Hawkes 判别节点、λ 衰减离场、OP 密集区检测）
- **关系论补充重写**：新增信条 7（双重确认）和信条 8（动能衰减离场）
- **ADR-007**：记录 v2.3 不完备手术和 v2.4 完备重写

### v3-snapshot（2026-03-12）
- **工作快照更新**：v2 → v3，反映v2.4理论重写完成和数据管线迁移启动
- **数据管线**：从ATAS独立导出转向Tardis.dev兼容格式，与合作者共建数据处理工具
- **优先级调整**：P0转为数据管线迁移，MifClusterExporter和Macro MIF搁置
