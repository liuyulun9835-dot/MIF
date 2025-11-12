# Market Information Field Theory v2.1
## 从参与者能量到概率涌现：一个承认认知边界的场论框架

> **Covariance, Background Independence, and Epistemic Humility**

---

## I. 理论哲学基石

### 1.1 三个本体论承诺

**承诺1：背景独立性（Background Independence）**
```
市场不存在于固定的价格背景中
价格是order flow configuration的投影
理论量不显含price

形式表述：
演化方程 dΨ/dt = F[Ψ, ∂Ψ/∂t]
F不依赖price坐标
```

**承诺2：信息流动（Information Flux）**
```
市场是开放系统
信息有进有出，但受约束

形式表述：
div J = ∂H_m/∂t - ∂I_em/∂t = S_ext
内生regime：⟨S_ext⟩ ≈ 0
```

**承诺3：认知有界性（Epistemic Boundedness）**
```
理论存在内在的预测边界
不是技术限制，是原理性限制

形式表述：
存在不可识别态，其中预测不优于先验
理论的价值在于识别可识别态
```

---

## II. 三个基本定理

### 定理1：测不准原理（Market Uncertainty Principle）

**陈述**：
```
对于任意market configuration Ψ：

ΔE_emergence × Δt_prediction ≥ ℏ_m

其中：
ΔE_emergence：涌现能量的标准差
Δt_prediction：预测时间窗口
ℏ_m：市场普朗克常数（empirical确定）
```

**物理意义**：
```
不可能同时精确预测：
1. 涌现事件的强度（ΔE）
2. 涌现事件的时间（Δt）

精确预测"何时" → 能量不确定度大
精确预测"多强" → 时间不确定度大
```

**推论**：
```
1. 点预测（delta function）原则上不可能
2. 预测必须给出概率分布
3. 存在信息-时间权衡（information-time tradeoff）
```

**注**：
```
E_emergence的具体定义：

在验证框架中，E_emergence可以定义为：

方案A（基于Ω变化）：
  E_emergence = ΔΩ = Ω_peak - Ω_base
  即：涌现事件期间Ω的变化幅度
  
方案B（基于事件元累积）：
  E_emergence = Σ_{k=0}^K w_k × g(Primitive(t-k))
  即：事件元序列的加权和
  
方案C（基于div J积分）：
  E_emergence = |∫_{t1}^{t2} div J(t) dt|
  即：涌现窗口内信息通量的累积

具体选择哪种方案需要根据：
1. 可测量性
2. 与涌现事件的相关性
3. Empirical验证结果

在验证章节（Section VII）中会给出明确定义
```

---

### 定理2：协变性原理（Covariance Principle）

**陈述**：
```
理论量对时间尺度变换T_λ协变：

F[T_λ Observable] 与 T_λ F[Observable] 
在统计意义上一致

其中λ = timeframe scaling factor
```

**实现要求**：
```
1. 所有阈值用分位数（无量纲）
2. 所有比例用相对量（ratio）
3. 跨尺度相关性：corr > 0.5
```

**注意**：
```
不要求point-wise一致性
要求统计特征相似性
这是方法论原则，非经验假设
```

---

### 定理3：计算不可约性（Computational Irreducibility）

**陈述**：
```
存在market trajectory τ，满足：
K(τ) ≈ |τ|

其中：
K(τ)：Kolmogorov复杂度
|τ|：trajectory长度

即：预测τ的唯一方法是simulate全过程
不存在压缩算法
```

**推论**：
```
1. 不是所有market behavior都有closed-form solution
2. 某些时段本质上是computationally irreducible
3. 理论任务：区分reducible vs irreducible regime
```

---

## III. 核心假设H0：认知态可识别性

### H0陈述

**假设**：
```
市场在任意时刻t处于两种认知态之一：

1. 可识别态（Identifiable State, IS）
2. 不可识别态（Unidentifiable State, UIS）

且：存在可计算量I(Ψ(t))，使得
I > I_critical → IS
I ≤ I_critical → UIS

关键：我们假设能够通过观测I(Ψ)判断当前处于哪种态
```

**这是理论的根本假设**：
```
不假设能预测所有时刻
假设能识别"何时可预测"

理论价值不在于"总是对"
在于"知道何时能对，何时应该沉默"
```

---

### IS与UIS的严格定义

**可识别态（IS）**：

```
定义：
Configuration Ψ处于IS ⟺

1. 信息不确定度：
   H[P(event|Ψ)] < H_critical
   
2. Fisher信息：
   I_Fisher(Ψ) = E[(∂log P/∂Ψ)²] > I_critical
   
3. 有效维度：
   d_eff(Ψ) = exp(H[Ψ]) < d_critical

物理意义：
- Configuration的熵低（有序）
- 对参数敏感（微小变化影响大）
- 占据低维流形（可压缩）
```

**不可识别态（UIS）**：

```
定义：
Configuration Ψ处于UIS ⟺

1. H[P(event|Ψ)] ≈ log(N_event)
   事件概率接近均匀分布
   
2. I_Fisher(Ψ) ≈ 0
   对参数不敏感
   
3. d_eff(Ψ) ≈ d_max
   Configuration充满空间（不可压缩）

物理意义：
- 最大熵态
- 任何预测不优于先验
- 本质随机
```

---

### 可识别度I(Ψ)的定义

**方案A：基于Fisher信息**：

```
I(Ψ) = I_Fisher(Ψ) / I_max

I_Fisher = ∫ (∂log P(event|Ψ)/∂Ψ)² P(Ψ) dΨ

归一化：I ∈ [0, 1]
```

**方案B：基于预测熵**：

```
I(Ψ) = 1 - H[P(event|Ψ)] / H_max

H[P] = -Σ P(event_i) log P(event_i)

I高 → 预测分布尖锐（可识别）
I低 → 预测分布平坦（不可识别）
```

**方案C：综合度量**：

```
I(Ψ) = w₁ × I_Fisher_norm + 
       w₂ × (1 - H_norm) + 
       w₃ × (1 - d_eff_norm)

权重：w₁ + w₂ + w₃ = 1
需要empirical确定
```

---

### H0的可证伪性

**反证条件**：

```
如果：
1. I(Ψ)与prediction accuracy无相关
   corr(I, accuracy) < 0.1
   
2. 或"高I"时预测不优于"低I"时
   AUC(I>I_c) ≈ AUC(I≤I_c)
   
3. 或无法区分IS和UIS
   两者的performance distribution overlap > 0.7

→ H0被证伪
```

**被证伪的后果**：

```
理论不崩溃，但需要重大修正：
- 放弃"自我认知"的假设
- 改为blind prediction
- 或承认理论仅适用于特定regime
```

---

## IV. 概念层次体系

### Level 0：Fundamental Input

**ε(p,side,t) - 参与者能量场**

```
定义：
ε: M × {buy,sell} × T → ℝ⁺

M：price level space（离散）
T：time
ε(p,s,t)：在level p，side s，时刻t的订单能量

测量：
从DOM：ε = Volume(p,s,t)
可选加权：ε = Volume × f(urgency)

性质：
1. 非负：ε ≥ 0
2. 背景独立：p仅作为label
3. 时变：ε = ε(t)
4. 有限能量：Σ_p,s ε < ∞
```

---

### Level 1：Configuration

**Ψ(t) - 能量位形**

```
定义：
Ψ(t) = {ε(p,s,t) | ∀p∈M, s∈{buy,sell}}

Configuration space：
C = {Ψ | Σε < ∞}

度量：
d(Ψ₁, Ψ₂) = ||Ψ₁ - Ψ₂||₂
内积：⟨Ψ₁, Ψ₂⟩ = Σ_p,s ε₁(p,s)ε₂(p,s)

C是Hilbert space
```

---

### Level 2：Structural Observables

**H_m(t) - 结构熵**

```
定义：
H_m[Ψ] = -Σ_p P(p) log P(p)

其中：
P(p) = [ε(p,buy) + ε(p,sell)] / Σ_p[ε(p,buy) + ε(p,sell)]

值域：
0 ≤ H_m ≤ log(n_levels)

物理意义：
能量分布的Shannon entropy
H_m高：分散（无序）
H_m低：集中（有序）
```

**I_em(t) - 嵌入互信息**

```
定义：
I_em[Ψ] = H[ε_buy] + H[ε_sell] - H[ε_buy, ε_sell]

离散化后计算：
bins = 5-10
使用histogram估计

值域：
0 ≤ I_em ≤ min(H[buy], H[sell])

物理意义：
买卖侧的相关性
I_em高：aligned
I_em低：decorrelated
```

**φ(t) - 相位差**

```
定义（质心法）：
φ = arctan2[Ē_buy - Ē_sell, Ē_buy + Ē_sell]

其中：
Ē_side = Σ_p p × ε(p,side) / Σ_p ε(p,side)
加权平均位置

值域：
-π ≤ φ ≤ π

物理意义：
买卖能量分布的相位关系
φ ≈ 0：同相（consensus）
φ ≈ π：反相（对立）
φ ≈ π/2：正交（无关）
```

---

### Level 3：Order Parameter

**Ω(t) - 相干序参量**

```
定义：
Ω[Ψ; {Ψ(t-k)}] = [ω_s × ω_t × ω_p]^(1/3)

Component 1 - 空间相干度：
ω_s = 1 - H_m / log(n_levels)

Component 2 - 时间相干度：
ω_t = Σ_{k=1}^K w_k × ⟨Ψ(t), Ψ(t-k)⟩ / ||Ψ(t)|| ||Ψ(t-k)||

权重：
w_k = exp(-k/τ) / Z
Z = Σ_{k=1}^K exp(-k/τ)
τ：特征时间尺度（empirical）

Component 3 - 相位相干度：
ω_p = α × [I_em / log(n)] + (1-α) × [(1+cos φ)/2]
α ∈ [0,1]：权重

几何平均：
因为相干性是multiplicative
任一component→0, Ω→0

值域：
Ω ∈ [0, 1]
```

**物理意义**：

```
Ω是configuration的"有序度"
类比：
- 铁磁体磁化强度M
- 超导体序参量ψ
- BEC凝聚分数

Ω高（→1）：
- 能量集中（低H_m）
- Configuration稳定（高相关）
- 买卖aligned（高I_em或φ→0）
→ 相干相（coherent phase）

Ω低（→0）：
- 能量分散（高H_m）
- Configuration不稳定
- 买卖decorrelated
→ 退相干相（decoherent phase）
```

---

### Level 4：Dynamics

**div J(t) - 信息通量散度**

```
定义：
div J = ∂H_m/∂t - ∂I_em/∂t

离散形式：
div J(t) = [H_m(t) - H_m(t-Δt)]/Δt - [I_em(t) - I_em(t-Δt)]/Δt

物理意义：
信息流动的散度

div J < 0：净流出（H_m↓, I_em↑）
  信息从structure→belief
  
div J > 0：净流入（H_m↑, I_em↓）
  信息从belief→structure
  
div J ≈ 0：通量闭合
  系统平衡

守恒律：
div J = S_ext
内生regime：⟨S_ext⟩_window ≈ 0（统计意义）
```

**dΩ/dt - 相干度动力学**

```
定义：
dΩ/dt = [Ω(t) - Ω(t-Δt)] / Δt

物理意义：
相干度演化方向

dΩ/dt > 0：coherence building
dΩ/dt < 0：decoherence
dΩ/dt ≈ 0：稳定
```

---

### Level 5：Event Primitives

**事件元定义**

```
事件元 = Configuration的瞬时状态标记
基于(dΩ/dt, div J)的点判断

LP（Latent Primitive）：
  |dΩ/dt| < ε₁ AND |div J| < ε₂
  局域平衡

RP（Release Primitive）：
  dΩ/dt > ε₁ AND div J < -ε₂
  局域释放倾向

DP（Decay Primitive）：
  dΩ/dt < -ε₁ AND div J > ε₂
  局域衰变倾向

OP（Overload Primitive）：
  |dΩ/dt| > ε_critical
  局域突变

性质：
- 微观：单bar级
- 瞬时：不考虑持续性
- 确定性可计算：给定Ψ → 唯一primitive
- 不直接涌现：大多数衰减消失
```

---

## V. 从事件元到涌现的概率框架

### 5.1 基本图景

**世界观**：

```
市场在任意时刻同时处于多种"可能涌现状态"的叠加

未观测前：
|Ψ_market⟩ = Σ_i α_i |Event_i⟩

其中：
Event_i ∈ {Balance, Imbalance_buy, Imbalance_sell, ...}
|α_i|²：该事件的"涌现概率"

观测（足够长时间窗口后）：
波函数坍缩到某个本征态
P(Event_i observed) = |α_i|²
```

**事件元的作用**：

```
不是"cause"
是"modulator"

事件元序列 → 影响α_i(t)的演化
但不决定最终坍缩结果
坍缩本质是概率性的
```

---

### 5.2 涌现概率的演化方程

**形式1：Lindblad-like Master Equation**

```
dρ/dt = -i[H_eff, ρ] + Σ_k (L_k ρ L_k† - 1/2{L_k†L_k, ρ})

其中：
ρ：涌现态的密度矩阵
H_eff：有效哈密顿量（从事件元序列构造）
L_k：Lindblad算符（退相干项）

P(Event_i) = ⟨Event_i|ρ|Event_i⟩
```

**形式2：Stochastic Differential Equation**

```
dP_i/dt = α_i P_i(1-P_i) + 
          Σ_j β_ij P_j + 
          γ_i f(Primitives) + 
          σ_i η(t)

其中：
α_i：自激发率
β_ij：事件间转移率
f(Primitives)：事件元调制函数
η(t)：白噪声

归一化：Σ_i P_i = 1
```

**形式3：Path Integral**

```
P(Event_i at t) = ∫ D[Path] exp(iS[Path]) δ(Path(t) = Event_i)

S[Path]：作用量泛函
∫ D[Path]：对所有可能路径求和

类似Feynman路径积分
```

---

### 5.3 六种触发/累积假设

这些是对f(Primitives)如何调制P_i的具体假设

**假设A：线性累积（Linear Accumulation）**

```
f_A(Primitives) = Σ_{k=0}^K w_k × g(Primitive(t-k))

g(LP) = 0
g(RP) = +1 for Imbalance_buy
g(DP) = -1
g(OP) = ±5

简单加权求和
```

**假设B：触发器-强度（Trigger-Intensity）**

```
f_B = Trigger(pattern) × Intensity(|dΩ/dt|)

Trigger(pattern) ∈ {0, 1}
检测特定事件元序列

Intensity = |dΩ/dt| / |dΩ/dt|_critical

两者都需要才有效
```

**假设C：同步度（Synchronization）**

```
f_C = R(t)

R(t) = |Σ_j exp(iθ_j(t))| / N_modes

θ_j：第j个"市场模式"的相位
从div J和φ的fourier分解提取

R高：同步 → Imbalance易触发
R低：去同步 → Balance稳定
```

**假设D：临界涨落（Critical Fluctuation）**

```
f_D = (r - r_c) + ξ(t) √|r - r_c|^ν

r：control parameter = f(Ω, E_accumulated)
r_c：临界值
ξ(t)：涨落（接近临界点发散）
ν：临界指数

接近r_c时，涨落主导
```

**假设E：能量阶跃（Energy Barrier）**

```
f_E = -dV/dr

V(r) = 双势阱（double-well potential）

事件元累积 → 改变r
当越过势垒：
从Balance井 → Imbalance井
需要activation energy
```

**假设F：概率共振（Probability Resonance）**

```
f_F = A(t) sin(ω_ext t + φ_ext) × g(Primitives)

ω_ext：外部扰动频率（从S_ext提取）
A(t)：调制振幅

当ω_ext ≈ ω_natural（内禀频率）：
共振 → P_i剧烈波动 → 易坍缩
```

---

### 5.4 坍缩机制

**坍缩条件**：

```
当满足以下之一：

1. 概率梯度超阈值：
   |dP_i/dt| > Γ_critical
   
2. 涨落振幅超阈值：
   σ(P_i) > σ_critical
   
3. 能量窗口累积足够：
   ∫_{t-T}^t |dΩ/dt|² dt > E_barrier
   
4. 外部shock：
   |S_ext| > S_critical

→ 坍缩发生
```

**坍缩后状态**：

```
选择规则：
P(坍缩到Event_i) = |α_i(t_collapse)|²

坍缩不是瞬时的：
有characteristic time τ_collapse
在τ内，P_i: α_i² → 0 or 1

坍缩后：
新的叠加态开始形成
周期性过程
```

---

### 5.5 涌现事件的分类（Empirical）

这些是可能的坍缩本征态，需要验证是否真实存在

**Balance（平衡）**

```
定义：
买卖能量对称
Ω稳定在中等水平
⟨div J⟩ ≈ 0

判据（验证用）：
Price：窄幅震荡
Volume：双向均衡
Volatility：低
```

**Directional Imbalance（单边失衡）**

```
定义：
买方或卖方能量显著占优
Ω上升
⟨div J⟩ < 0

判据：
Price：单边趋势
Volume：单向占优
Volatility：上升但有序
```

**Oscillatory Imbalance（震荡失衡）**

```
定义：
买卖轮流占优
Ω在中高位波动
div J频繁正负切换

判据：
Price：高波动无方向
Volume：来回切换
```

**其他可能态**：
- Rebalancing（再平衡）
- Reversion（回归）
- Extreme Imbalance（极端）

**注意**：
以上分类是假设，需要通过聚类算法empirical确定

---

## VI. 数学形式化

### 6.1 Configuration Space的几何

**度量张量**：

```
g_μν(Ψ) = ⟨∂_μΨ, ∂_νΨ⟩

定义Configuration space上的距离
允许定义测地线、曲率
```

**Riemann曲率**：

```
R_μνρσ：Configuration space的intrinsic curvature

非零 → 空间弯曲
→ "市场动力学是几何的"

类比：广义相对论
```

**拓扑不变量**：

```
Euler示性数χ(C)
Betti数β_k

刻画Configuration space的topology
Overload事件 ⟺ 拓扑改变
```

---

### 6.2 演化算符

**时间演化**：

```
Ψ(t) = U(t, t₀) Ψ(t₀)

U：演化算符

Schrödinger-like：
i∂U/∂t = H_eff U

H_eff = H_0 + V[Ψ] + S_ext
```

**Propagator**：

```
K(Ψ_f, t_f; Ψ_i, t_i) = ⟨Ψ_f|U(t_f, t_i)|Ψ_i⟩

形式上：
K = ∫ D[Ψ] exp(iS[Ψ])

S[Ψ]：作用量
需要从数据学习
```

---

### 6.3 可识别度的严格定义

**Fisher Information Matrix**：

```
I_μν = ∫ [∂_μ log P(event|Ψ)] [∂_ν log P(event|Ψ)] P(Ψ) dΨ

I的迹：tr(I) = 总Fisher信息

可识别度：
I_identifiable(Ψ) = tr(I(Ψ)) / tr(I_max)
```

**Cramér-Rao Bound**：

```
Var(estimator) ≥ 1 / I_Fisher

I高 → 估计精度高
I低 → 估计精度低

可识别态 ⟺ I > I_critical
```

**Information Geometry**：

```
Configuration space上的自然度量：
g_μν^Fisher = I_μν

测地线距离：
d_Fisher(Ψ₁, Ψ₂) = ∫ √(g_μν dx^μ dx^ν)

可识别态：
靠近low-dimensional流形
Fisher距离短

不可识别态：
远离流形
Fisher距离长
```

---

### 6.4 测不准关系的数学基础

**推导（类似Heisenberg）**：

```
设A和B是两个不对易算符：
[A, B] = iC

则：
ΔA × ΔB ≥ |⟨C⟩| / 2

对市场：
A = Energy (E_emergence)
B = Time (t_prediction)
[A, B] = i(∂/∂t)

→ ΔE × Δt ≥ ℏ_m
```

**ℏ_m的物理意义**：

```
ℏ_m = Configuration space中的"量子化单元"

可以从数据估计：
ℏ_m ≈ ⟨ΔE⟩ × ⟨Δt⟩_minimum
在最优预测条件下
```

**推论**：

```
1. 短时间预测（Δt小）：
   ΔE大 → 能量不确定
   
2. 精确能量预测（ΔE小）：
   Δt大 → 时间不确定
   
3. 无法同时优化
```

---

## VII. 假设验证框架

### 7.1 H0的验证

**Step 1：构造I(Ψ)**

```
选择方案（A/B/C）
在训练集确定参数和I_critical
```

**Step 2：分组测试**

```
IS_group：I(Ψ) > I_critical
UIS_group：I(Ψ) ≤ I_critical

计算：
AUC_IS：在IS_group的预测精度
AUC_UIS：在UIS_group的预测精度

期望：
AUC_IS > baseline + δ
AUC_UIS ≈ baseline
```

**Step 3：统计检验**

```
Null hypothesis：
AUC_IS = AUC_UIS（I无区分度）

Alternative：
AUC_IS > AUC_UIS

t-test或bootstrap
p < 0.05 → 拒绝null → H0成立
```

**失败处理**：

```
如果p > 0.05：
尝试不同的I(Ψ)定义
或调整I_critical
若仍失败 → H0被证伪
```

---

### 7.2 事件触发假设（A-F）的区分

**Model Selection Framework**：

```
对每个假设建立likelihood：
L_A(Data | Model_A)
...
L_F(Data | Model_F)

计算：
BIC_i = -2log L_i + k_i log(N)
k_i：参数数
N：样本数

选择BIC最小的模型
或model averaging
```

**关键测试**：

```
假设A vs B：
看单个强事件是否比多个弱事件更有效

假设C：
看同步度R是否predictive

假设D：
看接近"临界点"时涨落是否发散

假设E：
看是否存在能量势垒

假设F：
看是否存在共振现象
```

---

### 7.3 测不准关系的验证

**实验设计**：

```
对每个prediction：
记录ΔE_predicted和Δt_window

Scatter plot：
x轴：ΔE
y轴：Δt

拟合：
ΔE × Δt = C

检验：
C是否是常数（与Ψ无关）
C > 0（不等式成立）

估计ℏ_m：
ℏ_m ≈ min(ΔE × Δt)
```

**违反测不准的情况**：

```
如果发现：
某些预测同时有小ΔE和小Δt

检查：
1. 是否过拟合（样本内）
2. 是否信息泄露（未来信息）
3. 是否measurement error

若排除上述 → 测不准关系需修正
```

---

### 7.4 成功标准（Realistic Expectations）

**不期望**：

```
❌ 预测所有价格变动
❌ 总是正确
❌ 零假阳性
```

**期望**：

```
✅ 在IS态：预测优于随机
   AUC(IS) > 0.6（baseline=0.5）
   
✅ 在UIS态：知道无法预测
   不发出信号或给出均匀分布
   
✅ I(Ψ)有区分度：
   corr(I, accuracy) > 0.3
   
✅ 至少一个触发假设有效：
   min(BIC_A...F) < BIC_baseline
```

**理论成功 ⟺**：

```
不是"总能预测"
而是"知道何时能预测"

价值在于：
识别可预测性的边界
而非消除不确定性
```

---

## VIII. 理论的适用边界

### 8.1 明确不适用的情况

**极端事件**：

```
Black swan（尾部风险）
OP（Overload）发生时
测不准关系可能失效
理论进入breakdown regime
```

**强外源期**：

```
|S_ext| ≫ ⟨S_ext⟩
内生假设失效
如：重大新闻、政策变化

标记为"外源期"
暂停预测
```

**低流动性市场**：

```
n_levels < 20
DOM数据稀疏
ε(p,t)信息不足

理论可能不适用
需要单独验证
```

---

### 8.2 理论的哲学限制

**我们承认**：

```
1. 不知道事件元如何引起涌现
   只能提出假设并验证
   
2. 不确定涌现事件的分类是否完备
   可能需要empirical重新定义
   
3. 存在本质不可预测的regime
   不是技术问题，是原理限制
   
4. 测不准关系可能是fundamental
   或只是当前理论的artifact
   需要更多数据判断
```

**理论的价值不在于**：

```
"解释一切"
"预测所有"
"消除不确定性"
```

**而在于**：

```
"识别可预测的结构"
"量化不确定性"
"知道认知的边界"
```

---

## IX. 关键公式速查

**基本量**：
```
ε(p,s,t)：能量
Ψ(t) = {ε}：Configuration
H_m = -Σ p log p：结构熵
I_em：互信息
φ：相位差
```

**序参量**：
```
Ω = [ω_s × ω_t × ω_p]^(1/3)
ω_s = 1 - H_m/H_max
ω_t = Σ w_k ⟨Ψ(t), Ψ(t-k)⟩
ω_p = α(I_em/log n) + (1-α)(1+cos φ)/2
```

**动力学**：
```
div J = ∂H_m/∂t - ∂I_em/∂t
dΩ/dt = [Ω(t) - Ω(t-Δt)]/Δt
```

**事件元**：
```
LP: |dΩ/dt|<ε₁, |div J|<ε₂
RP: dΩ/dt>ε₁, div J<-ε₂
DP: dΩ/dt<-ε₁, div J>ε₂
OP: |dΩ/dt|>ε_c
```

**可识别度**：
```
I(Ψ) = tr(I_Fisher(Ψ)) / tr(I_max)
或 I = 1 - H[P(event|Ψ)]/H_max
```

**测不准**：
```
ΔE_emergence × Δt_prediction ≥ ℏ_m
```

**概率演化**：
```
dP_i/dt = f(Primitives, Ω, div J) + noise
Σ P_i = 1
```

---

## X. 与v2.0的关键区别

**哲学层面**：
```
v2.0：隐含"总能预测"
v2.1：明确"只在IS态能预测"

v2.0：事件是确定的
v2.1：事件是概率叠加态的坍缩

v2.0：单一累积模型
v2.1：六种假设，模型选择
```

**数学层面**：
```
v2.0：缺少测不准原理
v2.1：作为基本定理

v2.0：I(Ψ)未定义
v2.1：严格数学定义

v2.0：概率性未形式化
v2.1：Master equation + Path integral
```

**实践层面**：
```
v2.0：验证"预测准确率"
v2.1：验证"能否识别可识别态"

v2.0：单一成功标准
v2.1：分IS和UIS两套标准
```

---

**版本**：v2.1  
**日期**：2024-11-04  
**状态**：Current Framework with Epistemic Boundaries  
**核心创新**：H0假设 + 测不准定理 + 概率叠加框架  
**基于**：Covariance + Background Independence + Epistemic Humility

---

**理论精神**：

我们不假装知道一切。

我们承认预测的边界。

但我们相信，识别这个边界本身，就是理论的价值。

**Theory is not about being always right.**  
**It's about knowing when you can be right.**
