# MIF理论公式总结
## Market Information Field Theory v2.1 - 核心公式

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
Ψ(t) = {ε(p,s,t) | all p,s}
表示完整的DOM能量配置状态
```

### 1.2 序参量与熵

**结构熵 H_m**
```
H_m = -Σ P(p) log P(p)
其中: P(p) = ε(p)/Σε(p)
```

**买卖互信息 I_em**
```
I_em = H[ε_buy] + H[ε_sell] - H[ε_buy, ε_sell]
```

**相干序参量 Ω**
```
Ω = exp(-H_m) × (1 + I_em/H_max)
范围: [0,1]
含义: 市场可读性/有序度
```

### 1.3 动力学量

**信息通量散度**
```
div J = ∂H_m/∂t - ∂I_em/∂t
含义: 系统开放性度量
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

### 2.1 结构清晰度 I(Ψ)

```
I(Ψ) = I(PoC) × I(VA) × I(u)

其中:
I(PoC) = exp(-σ(PoC)/μ(PoC))  # PoC稳定性
I(VA) = 1/(1 + VAH-VAL)        # 盒子紧密度  
I(u) = exp(-|log(ρ)|)          # 紧迫度平衡
```

### 2.2 执行强度 E

**基于Cluster数据**:
```
E = Σ_p cluster_delta(p) × cluster_volume(p)
```

**DOM代理方案**(数据不全时):
```
E_proxy = Σ (realized_buy - realized_sell) × total_volume
```

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

### 3.1 测不准原理
```
ΔE × Δt ≥ ℏ_m
含义: 能量分辨率与时间分辨率的trade-off
```

### 3.2 信息守恒
```
dI/dt + div(J_I) = 0 (闭系统)
dI/dt + div(J_I) = S_ext (开系统)
```

### 3.3 可识别性判据
```
Fisher信息: I_Fisher(Ψ) = E[(∂logP/∂Ψ)²]
可识别条件: I(Ψ) > I_critical
```

### 3.4 最小结构识别(MSI)
```
MSI窗口 = 4-6 bars
含义: 需要最少4个时间单位才能识别稳定结构
```

---

## 4. 实现优先级

### Phase 1 (当前)
```python
# 核心指标
Ω = compute_coherence(H_m, I_em)
I(Ψ) = compute_clarity(PoC, VA, u)
ICI = compute_ici(I, E, Ω)

# 判断逻辑
if Ω < Ω_threshold:
    return "UIS: 市场不可读"
if I(Ψ) < I_threshold:  
    return "结构不清晰"
if abs(ICI) > ICI_threshold:
    return probability_distribution()
```

### Phase 2 (计划)
- 向量序参量 ∇Ω
- 多尺度I(Ψ)
- 动态阈值

### Phase 3 (未来)
- 完整矢量场
- 跨市场关联
- 自适应MSI

---

## 5. 关键依赖关系

```
DOM数据 ──→ ε(p,s,t) ──→ Ψ(t)
           ↓            ↓
         PoC/VA     H_m, I_em
           ↓            ↓
         I(Ψ)           Ω
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
| I(Ψ) | 结构清晰度 | [0,1] | 结构存在性判断 |
| ρ | 紧迫度比 | (0,∞) | 买卖压力比 |
| R | 共振强度 | ℝ | 结构-执行同向性 |
| D | 主导度 | [-1,1] | 谁在主导 |
| ICI | 相位一致度 | ℝ | 综合信号强度 |

**注意**: 
- 所有公式保持背景独立(不依赖绝对价格)
- 使用相对度量而非绝对值
- 优先概率输出而非确定预测
