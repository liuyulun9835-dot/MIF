# MIF理论公式总结
## Market Information Field Theory v2.4 - 核心公式

> **底数约定**：全文Shannon熵使用log₂（单位：bit）。

---

## 1. 核心公式体系

### 1.1 基础场变量

**DOM能量场**
```
ε(p,s,t) = DOM层级能量
其中:
  p: price level (仅作标签)
  s: side ∈ {buy, sell}
  t: time
```

**Configuration**
```
Γ(t) = {ε(p,s,t) | all p,s}
表示完整的DOM能量配置状态
```

### 1.2 序参量与熵

**结构熵 H_m**
```
H_m = -Σ P(p) log P(p)
其中: P(p) = ε(p)/Σε(p)
```

**买卖侧轮廓相似度 S_bs**
```
S_bs = cos_sim(ε_buy[0:20], ε_sell[0:20])
    = dot(ε_buy, ε_sell) / (‖ε_buy‖ × ‖ε_sell‖)
范围: [0, 1]（因ε≥0）
含义: 买卖侧DOM形状的对齐程度
```

> Phase 1替代原I_em（联合熵在20样本下不可靠）。详见 MIF_理论哲学_v2_4.md Level 2 计算可行性注。

**相干序参量 Ω**（三分量几何平均形式）
```
Ω = [ω_s × ω_t × ω_p]^(1/3)

ω_s = 1 - H_m / log₂(n_levels)       # 空间相干度
ω_t = Σ w_k × cos_sim(Γ(t),Γ(t-k))  # 时间相干度
ω_p = α × S_bs + (1-α) × [(1+cosφ)/2] # 相位相干度

范围: [0,1]
含义: 市场可读性/有序度（几何平均，任一→0则Ω→0）
```

### 1.3 动力学量

**信息通量散度**
```
div J = ∂H_m/∂t - ∂S_bs/∂t
含义: 信息凝聚/扩散方向
  div J < 0: 凝聚（H_m降 且/或 S_bs升）
  div J > 0: 扩散（H_m升 且/或 S_bs降）
```

**紧迫度比 ρ**
```
ρ = u_buy / u_sell
其中:
  u_buy = realizedBuy / BestAskVolume
  u_sell = realizedSell / BestBidVolume
```

**相位差 φ**
```
φ = arctan2(Σp·ε_buy(p), Σp·ε_sell(p))
含义: 买卖能量质心的相对位置
```

---

## 2. MIF_CE_ICI新指标体系

### 2.1 结构清晰度 I(Γ)

```
I(Γ) = I(PoC) × I(VA) × I(u)

其中:
I(PoC) = exp(-σ(PoC)/μ(PoC))  # PoC稳定性
I(VA) = 1/(1 + VAH-VAL)        # 盒子紧密度  
I(u) = exp(-|log(ρ)|)          # 紧迫度平衡
```

> **值域注**：I(Γ)由z-score构成，值域无界（非[0,1]）。I_critical阈值使用历史分位数。

### 2.2 执行强度 E

> （当前阶段（V14_Final DOM-only）`quality` 暂为占位，待 Cluster 字段落地后启用；不影响 Ω/I/ρ 的计算与门控。）

**基于Cluster数据**:
```
E = Σ_p cluster_delta(p) × cluster_volume(p)
```

**DOM代理方案**(数据不全时):
```
E_proxy = Σ (realized_buy - realized_sell) × total_volume
```

### 2.2.1 防守弹性 κ（DOM Resilience）

> Phase 1（V14_Final DOM-only）即可计算，不依赖 Cluster。

```
κ_ask = autocorr(Σ ask_volumes[0:20], lag=1, W=12)
κ_bid = autocorr(Σ bid_volumes[0:20], lag=1, W=12)
κ = min(κ_ask, κ_bid)

含义: DOM 存量在 bar 间的自相关性
      高 κ = 被吃单后快速回填（高弹性）
      低 κ = 流动性撤退（低弹性）
```

**E.quality 分阶段定义**:
```
Phase 1 (DOM-only):
  E.quality_proxy = κ  （唯一可用的质量度量）

Phase 2 (Cluster 就绪后):
  E.quality = 0.3×CVD_slope + 0.2×DEPIN + 0.2×large_trade_pct + 0.3×κ
```

**注意**：κ 与 Ω 的 ω_t 分量测量不同对象。ω_t 测 Γ 形态的全局相似度；κ 测单侧存量的恢复速度。两者可以不一致（ω_t 高但 κ 低 = 形状没变但弹性下降）。

### 2.3 共振与主导

**标准化**:
```
z(I) = (I - μ_I) / σ_I
z(E) = (E - μ_E) / σ_E
```

**共振强度 R**:
```
R = z(I) × z(E)
含义: 结构与执行的同向程度
```

**主导度 D**:
```
D = (|z(I)| - |z(E)|) / (|z(I)| + |z(E)|)
范围: [-1, 1]
含义: >0结构主导, <0执行主导
```

**相位一致度 ICI_new**:
```
ICI_new = R × exp(-|D|) × Ω
整合: 共振 × 平衡 × 市场可读性
```

---

## 3. 理论约束

### 3.1 信息-时间权衡原理
```
ΔE_emergence × Δt_prediction ≥ C_m
含义: 能量分辨率与时间分辨率的trade-off
```

> （C_m：市场信息约束常数，由 Fisher 信息量 / Cramér-Rao 下界推导，须按品种经验校准。C_m 为 regime-dependent 的经验量，不是普适常数。此不等式为经验性原理，非定理。详见 MIF_理论哲学_v2_4.md Section VI.4。）

### 3.2 信息守恒
```
dI/dt + div(J_I) = 0 (闭系统)
dI/dt + div(J_I) = S_ext (开系统)
```

### 3.3 可识别性判据
```
Fisher信息: I_Fisher(Γ) = E[(∂logP/∂Γ)²]
可识别条件: I(Γ) > I_critical
```

### 3.4 最小结构识别(MSI) - 动态版本

**旧版本（已过时）**：
```
MSI窗口 = 4-6 bars（假设为常数）
```

**新版本（当前）**：
```
T*(S,tf,Γ) = argmax_t {∫[0,t] [Alpha(τ|S,Γ) - Cost(τ|tf)] dτ}

简化形式：
- 强趋势(S>0.7): T* ≈ 6-10 bars
- 中等趋势(0.3<S<0.7): T* ≈ 4-6 bars
- 弱趋势(S<0.3): T* ≈ 2-4 bars

实时判据：
继续持有 ⟺ ∂Alpha/∂t > ∂Cost/∂t
```

**核心思想**：
```
最优响应时间不是常数，而是边际均衡的动态结果
∂(Position)/∂(Alpha) = ∂(Position)/∂(Cost)
```

### （新增）多尺度相干与延续（MSI 量）

- `C_ms = corr([Ω_i])`，`i ∈ {5m,15m,30m}`  
  解释：跨尺度相位一致性，衡量“结构在不同时间层是否同向同步”

- `ΔΩ_multi = mean(dΩ_i/dt)`  
  解释：多尺度延续速率，衡量整体相干度的持续推进

- `MSI_weight = sigmoid((C_ms - 0.5)/0.1)`  
  解释：将一致性映射为置信权重，用于入场确认与持仓缩放

### 3.5 突破判别（Hawkes 过程）

**Hawkes 强度函数**：
```
λ(t) = μ + Σ_{t_i < t} α · exp(−β(t − t_i))

μ：基线强度（鞅态下的随机交易概率）
α：激发跳跃（吃单事件拉升后续交易概率）
β：指数衰减率（跟风情绪冷却速度）
```

**分支比（核心统计量）**：
```
n = α / β

n ≈ 0：无自激（齐次泊松/鞅态）→ UIS
n < 1：次临界（假阳性，动能衰减）→ IS 候选被否决
n ≥ 1：超临界（真阳性，自激级联）→ IS 确认
```

**IS 双重确认**：
```
IS确认 = I(Γ) > I_critical AND n ≥ n_threshold
任一条件不满足 → 回退 UIS
```

**离场信号**：
```
λ_excite(t) = Σ α·exp(−β(t−t_i))
λ_excite → μ 且 ∂²λ/∂t² < 0 → 动能耗尽 → 离场
```

---

## 4. 实现优先级

> **顺序声明**：以下判断逻辑的顺序（先Ω门控→再I(Γ)→再ICI）是当前工程默认值，不是理论必然。所有指标可并行计算，门控顺序待empirical验证后可能调整。

### Phase 1 (当前)
```python
# 核心指标
Ω = compute_coherence(H_m, I_em)
I(Γ) = compute_clarity(PoC, VA, u)
ICI = compute_ici(I, E, Ω)

# 判断逻辑
if Ω < Ω_threshold:
    return "UIS: 市场不可读"
if I(Γ) < I_threshold:  
    return "结构不清晰"
if abs(ICI) > ICI_threshold:
    return probability_distribution()
```

### Phase 2 (计划)
- 向量序参量 ∇Ω
- 多尺度I(Γ)
- 动态阈值

### Phase 3 (未来)
- 完整矢量场
- 跨市场关联
- 自适应MSI

---

## 5. 关键依赖关系

```
DOM数据 ──→ ε(p,s,t) ──→ Γ(t)
           ↓            ↓
         PoC/VA     H_m, S_bs
           ↓            ↓
         I(Γ)           Ω
           ↓            ↓
           └──→ ICI ←──┘
                 ↓
            概率分布P_i
```

---

## 快速参考

| 符号 | 名称 | 范围 | 用途 |
|-----|------|------|------|
| Ω | 相干度 | [0,1] | 市场可读性判断 |
| S_bs | 买卖侧轮廓相似度 | [0,1] | 买卖侧DOM对齐度 |
| I(Γ) | 结构清晰度 | [0,1] | 结构存在性判断 |
| ρ | 紧迫度比 | (0,∞) | 买卖压力比 |
| R | 共振强度 | ℝ | 结构-执行同向性 |
| D | 主导度 | [-1,1] | 谁在主导 |
| ICI | 相位一致度 | ℝ | 综合信号强度 |
| κ | DOM弹性 | [-1,1] | 防守侧回复能力 |
| n | Hawkes分支比 | [0,∞) | 突破自持性判别 |
| λ(t) | Hawkes强度 | ℝ⁺ | 订单到达率 |
| μ | 泊松基线 | ℝ⁺ | 鞅态基线强度 |

**注意**: 
- 所有公式保持背景独立(不依赖绝对价格)
- 使用相对度量而非绝对值
- 优先概率输出而非确定预测
