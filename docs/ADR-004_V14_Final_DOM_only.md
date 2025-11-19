# ADR-004：V14_Final 封装（DOM-only）与 v18 路线弃用

**状态**: ✅ Accepted

## 决策
当前阶段以 DOM-only 的 V14_Final 为主线，禁用 cluster_fallback；Cluster 导出迁移为独立工程 MifClusterExporter。

## 理由
- Level-2 回放仅提供 DOM 快照，直接推动 DOM 估算迭代更易落地。
- v18 将 Cluster 与导出耦合导致工程摩擦与进度阻塞。
- 先固化稳定 DOM 估算（含 ρ 的 20 层总量归一），再以独立工程提供精确 Cluster 字段，便于验证 E.quality 和精确 ρ。

## 后果
- 文档、示例文件名统一为 `export_dom_v14.jsonl`。
- 凡是“修复 v18 Cluster”的任务标记为 Deprecated（已弃用，见 ADR-004）。

## 验证
- V14_Final 一日样本导出 ≥90% 覆盖率，ρ ∈ [0,1] 异常为极少数且可解释。
- 随后在 MifClusterExporter 中进行 ρ(dom_proxy) vs ρ(cluster_true) 的偏差评估。
