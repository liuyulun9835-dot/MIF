# MIF ATAS数据需求与实现指令 v2.0（Snapshot 对齐）

estimation_method: "dom_proxy_v14"

## 1. 输出格式总览
- 格式：JSONL（一行一条记录）
- 命名示例：`export_dom_v14.jsonl`
- 覆盖率目标：≥ 90% 有效 bar

## 2. 必填字段
| 字段 | 类型 | 说明 |
|------|------|------|
| `ts_utc` | ISO8601 string | Bar 收盘 UTC 时间戳 |
| `bar_index` | int | 从 0 递增的序号，用于完整性检查 |
| `version` | string | 导出版本标识（建议 `v14_final`） |
| `estimation_method` | string | 固定填入 `dom_proxy_v14` |
| `dom_levels` | object | DOM 20 层聚合字段，结构见下 |
| `rho_buy` | decimal | `realized_buy / sum(ask_volumes[0:20])`，空缺填 0 |
| `rho_sell` | decimal | `realized_sell / sum(bid_volumes[0:20])`，空缺填 0 |

### 2.1 `dom_levels` 结构
- `ask_volumes`: decimal[20]
- `bid_volumes`: decimal[20]
- `price_levels`: decimal[20]（可选，仅作标签）

## 3. Cluster 字段（占位说明）
- `cluster_levels`: 由后续独立工程 **MifClusterExporter** 产出。
- `trades_count`、`CVD`、`large_trade_pct` 等精确字段在 MifClusterExporter 完成后回填。
- 当前阶段不在 DOM-only 导出中出现此字段；如需兼容，可保留 `cluster` 键但填空对象。

## 4. ρ 定义与校验
- `rho_buy = realized_buy / sum(ask_volumes[0:20])`
- `rho_sell = realized_sell / sum(bid_volumes[0:20])`
- 取值约束：ρ ∈ [0, 1]，若分母为 0 则回填 0 并记录异常。
- 校验：每日抽样检查 ρ 分布，异常点须附解释。

## 5. 验证清单
- [ ] JSONL 每行包含所有必填字段
- [ ] `estimation_method` 恒为 `dom_proxy_v14`
- [ ] `dom_levels` 数组固定 20 层，缺失补 0
- [ ] `rho_buy`、`rho_sell` 在允许范围内，异常点附带日志
- [ ] 导出文件命名遵循 `export_dom_v14.jsonl`
