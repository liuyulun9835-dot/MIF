# MIF交易策略项目 - 快速导航

> **Market Information Field** - 基于信息场论的加密货币交易策略  
> **项目状态**: v2.4理论重写完成 | 数据管线迁移中（ATAS → Tardis.dev 过渡期）
> **NOTE**: 本阶段禁用 cluster_fallback；Cluster 字段由后续独立项目提供（ADR-004）

---

## 核心文件导航

### 理论基础
- `MIF_理论哲学_v2_4.md` - MIF理论框架（v2.4） ⭐⭐⭐
- `MIF_Strategy_Relationalism_Supplement_v2.md` - 策略信条（v2，含 Hawkes 信条扩展） ⭐⭐⭐⭐

### 策略实现
- `MIF_Strategy_t2.md` - **完整策略定义（鞅/Hawkes 修正版）** ⭐⭐⭐⭐⭐
- `MIF_Architecture_Decision_Records.md` - 架构决策记录 ⭐⭐⭐⭐

### 工程实现
- `MIF_ATAS_Data_Requirements_v1.md` - 数据需求与实现 ⭐⭐⭐⭐⭐

### 精简版文档(新)
- `MIF_1_公式总结_精简版_v2_4.md` - 核心公式速查（v2.4）
- `MIF_2_概念澄清备案_精简版_v2_4.md` - 理论边界澄清（v2.4）

---

## 快速启动

### 理论讨论
1. 读 `MIF_理论哲学_v2_4.md` (理论基础)
2. 读 `MIF_Strategy_Relationalism_Supplement_v2.md` (策略信条)
3. 参考 `MIF_1_公式总结_精简版_v2_4.md` (公式速查)

### 策略优化
1. 读 `MIF_Strategy_t2.md` (核心策略)
2. 读 `MIF_Architecture_Decision_Records.md` (决策背景)

### 代码实现
1. 读 `MIF_ATAS_Data_Requirements_v1.md` Section 4 (修改指令)
2. 注意术语澄清部分(文档附录)
> 数据输出命名示例：`export_dom_v14.jsonl`

---

## 核心概念速览

### MIF三层过滤器
```
Layer 1 (环境层): Ω (相干度) - 市场是否可读?
Layer 2 (存在层): I(Γ) (结构清晰度) - 是否存在稳定结构?
Layer 3 (相位层): R/D (共振/主导) - 结构如何被推进?
```

### 突破判别（v2.4 新增）
```
Layer 3+: n (Hawkes分支比) - 突破是否自持?
  n < 1: 假阳性（动能衰减）→ IS 候选被否决
  n ≥ 1: 真阳性（自激级联）→ IS 确认
  IS 确认 = I(Γ) > I_critical AND n ≥ n_threshold
```

### 六大策略信条
1. 关系优于点 - 用quantile而非hardcode
2. 动态优于静态 - 用导数而非静态值
3. 矢量优于标量 - E=(direction, magnitude, quality)
4. 分布优于期望 - 输出概率而非0/1
5. 窗口优于瞬时 - 用滑动窗口而非单点
6. 相位优于幅度 - 用内积而非简单和

### E三维化(核心创新)
- `direction`: 推动方向 = sgn(delta_trade)
- `magnitude`: 推动强度 = |z(delta_trade)|
- `quality`: 推动质量 = f(CVD_slope, DEPIN, large_trade, **κ**)
  - Phase 1 (DOM-only): quality ≈ κ

---

## 当前任务

**P0**: 数据管线迁移
- ATAS indicator修改（输出Tardis-compatible book snapshot格式）
- 与合作者的数据可视化工具接口对齐
- 验证ATAS导出 ↔ 工具输入的格式兼容性

**P1**: Hawkes MLE工程化（依赖P0数据管线就绪）

**P2**: Strategy t2端到端回测

**搁置**: Macro MIF v0.1（待重构）、MifClusterExporter（待数据管线评估）

---

## 常见陷阱

- **术语混淆**: ATAS中Ask/Bid在DOM和Cluster含义相反
- **点判断**: 避免硬阈值，使用分位数
- **E的使用**: E是三维矢量，不是标量
- **数组维度**: DOM/Cluster固定20层

---

**维护者**: Severi + Claude AI  
**项目开始**: 2024-10
