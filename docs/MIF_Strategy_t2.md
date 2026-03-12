# MIF Strategy Test v2.0 (t2)

> **Market Information Field 交易策略 - 鞅/Hawkes 修正版**  
> 基于 MIF v2.4 理论（经典随机过程框架）  
> 强调: 鞅态规避 | 自激确认 | 动能衰减离场 | 认知边界

---

## 1. 理论基础

### 1.1 核心世界观

MIF 将市场视为**信息场**，DOM 能量配置 Γ(t) 是基本可观测量，价格是 Γ 的标量投影。

**三个本体论承诺**：
1. **背景独立性**：理论量不依赖绝对价格，使用 level index
2. **信息流动**：div J = ∂H_m/∂t - ∂I_em/∂t = S_ext
3. **认知有界性**：存在 IS（可识别态）和 UIS（不可识别态），理论价值在于区分二者

**根本假设 H0**：
```
市场在任意时刻 t 处于两种认知态之一：
- IS：结构清晰 + 突破自持 → 可提取 alpha
- UIS：鞅态或信息不可识别 → 预测不优于先验

IS 确认需要双重通过：I(Γ) > I_critical AND n ≥ n_threshold
```

### 1.2 Alpha 的第一性原理来源

```
鞅态（UIS）：
  E[X_{t+1} | F_t] = X_t
  过去信息对预测未来无帮助
  任何方向性交易期望值 ≤ 0（含摩擦）

Alpha 存在于鞅被打破的窗口中：
  系统从齐次泊松相变为超临界Hawkes（n ≥ 1）
  订单流自激级联形成，价格获得正漂移项
  
Alpha 消失于鞅恢复时：
  Hawkes 激发项指数衰减，λ(t) → μ
  做市商重新铺设双向限价单
  系统回到泊松基线
  
因此：Alpha = 鞅破裂到鞅恢复之间的窗口收益
```

### 1.3 策略六大信条

1. **关系优于点**：quantile(x, p) 而非 hardcode 阈值
2. **动态优于静态**：dΩ/dt 导数而非 Ω 静态值
3. **矢量优于标量**：E = (direction, magnitude, quality) 三维
4. **分布优于期望**：输出 confidence 概率而非 0/1 信号
5. **窗口优于瞬时**：std_M 滑动窗口统计而非单点
6. **相位优于幅度**：R = z(I)×z(E) 内积而非简单求和

---

## 2. 判别流水线

### 2.1 总体架构

```
[鞅过滤器（可选）] → [结构层判别] → [事件层判别] → [执行决策]

> **顺序声明**：以下Step 0-5的顺序是当前工程默认值。各指标的计算彼此独立（可并行），门控顺序可根据empirical验证调整。已知潜在盲点：当n≥1但Ω<q68时，当前顺序会在Step 1拒绝而不触发Hawkes。详见 MIF_理论哲学_v2_4.md 的"分解顺序开放声明"。

具体流程：

Step 0（可选）：VR(q) 鞅过滤
  VR(q) ≈ 1 → 跳过后续，维持 UIS
  VR(q) 显著偏离 1 → 继续

Step 1：Ω 环境门控
  Ω < q68 → UIS，不交易
  dΩ/dt < 0 且持续 → 环境恶化，不交易
  OP 密集区检测 → 理论不适用态，不交易

Step 2：I(Γ) 结构判别
  I(Γ) ≤ I_critical → 结构不清晰，不交易
  I(Γ) > I_critical → IS 候选

Step 3：Hawkes 突破确认
  dΩ/dt 越过 θ_crit → 启动 Hawkes MLE
  n < n_threshold → IS 候选被否决，回退 UIS
  n ≥ n_threshold → IS 确认

Step 4：方向与模式判断
  E.direction 确定方向
  D 确定模式（Belief-led / Structure-led / Co-dominant）
  confidence 综合评分确定仓位

Step 5：入场后监测
  持续计算 λ_excite(t)
  λ_excite → μ → regime transition → 离场
```

### 2.2 OP 密集区检测（理论不适用态过滤）

```python
# 在 Step 1 中执行
# 区分 UIS（安静鞅态）和 OP 密集区（信息过载）

abs_dOmega = abs(dΩ/dt)
sign_consistency = abs(sum(sgn(dΩ/dt[-M:]))) / M

if abs_dOmega > ε_critical and sign_consistency < 0.3:
    # |dΩ/dt| 极高但方向频繁反转
    return "OP_DENSE_ZONE: 理论不适用，强制暂停"

# 与 UIS 的区别：
# UIS: |dΩ/dt| < ε₁，系统安静
# OP 密集区: |dΩ/dt| > ε_critical 但方向混乱
# 两者操作结果相同（不交易），但原因不同
```

---

## 3. 核心指标

### 3.1 结构层指标（Level 0-3）

**Ω - 相干序参量**：
```
Ω = [ω_s × ω_t × ω_p]^(1/3)

ω_s = 1 - H_m / log(n_levels)     # 空间相干度
ω_t = Σ w_k × cos_sim(Γ(t), Γ(t-k))  # 时间相干度
ω_p = α×[I_em/log(n)] + (1-α)×[(1+cosφ)/2]  # 相位相干度

几何平均：任一分量→0, Ω→0（veto 性质）
```

**I(Γ) - 结构清晰度**：
```
I(Γ) = EMA_9[(z_comp + z_POC + z_u) / 3]

z_comp = z(1 - w_box)      # 盒子紧密度
z_POC = -z(std_12(POC))    # POC 稳定性
z_u = -z(std_12(u))        # 位置稳定性
```

**E - 执行推进（三维矢量）**：
```
E = (direction, magnitude, quality)

direction = sgn(Σ(buy - sell))
magnitude = |z(Σ(buy - sell))|

Phase 1 (DOM-only):
  quality = κ  # DOM 弹性作为唯一质量代理

Phase 2 (Cluster 就绪后):
  quality = 0.3×CVD_slope + 0.2×DEPIN + 0.2×large_trade_pct + 0.3×κ
```

**κ - DOM 弹性**：
```
κ_ask = autocorr(Σ ask_volumes[0:20], lag=1, W=12)
κ_bid = autocorr(Σ bid_volumes[0:20], lag=1, W=12)
κ = min(κ_ask, κ_bid)

注意方向性使用：
  Belief-led（进攻主导）：看对手侧的弱度（1 - κ_opponent）
  Structure-led（结构主导）：看结构侧的强度（κ 直接使用）
```

### 3.2 事件层指标（Hawkes）

**Hawkes 强度函数**：
```
λ(t) = μ + Σ_{t_i < t} α · exp(−β(t − t_i))

μ：基线强度（无刺激时的随机交易概率）
α：激发跳跃（吃单事件拉升后续交易概率）
β：指数衰减率（跟风情绪冷却速度）
```

**分支比**：
```
n = α / β

n ≈ 0：无自激（齐次泊松/鞅态）
n < 1：次临界（假阳性，动能衰减）
n ≥ 1：超临界（真阳性，自激级联）
```

**激发项剩余动能**：
```
λ_excite(t) = Σ_{t_i < t} α · exp(−β(t − t_i))

用于入场后监测：
  λ_excite → μ → 动能耗尽 → 离场信号
  ∂²λ_excite/∂t² < 0 → 衰减在加速 → 提前离场
```

### 3.3 综合指标

**R - 共振强度**：
```
R = z(I(Γ)) × z(E.magnitude)
R > 0：结构与执行同向（共振）
R < 0：反相（冲突）→ 拒绝
```

**D - 主导度**：
```
D = (|z(I(Γ))| - |z(E)|) / (|z(I(Γ))| + |z(E)|)
D < -0.25*：Belief-led（执行主导）
D > +0.25*：Structure-led（结构主导）
|D| < 0.25*：Co-dominant

*所有阈值使用分位数，此处为说明用近似值
```

**confidence - 动态置信度**：
```
confidence = (
    0.20 × Ω_static_score +       # 静态可读性
    0.20 × Ω_trend_score +        # 动态轨迹（dΩ/dt, d²Ω/dt²）
    0.25 × E.quality +            # 执行质量
    0.20 × hawkes_confirmation +   # Hawkes 确认强度（新增）
    0.15 × no_divergence_score    # 无背离
)
```

**hawkes_confirmation 评分**：
```python
if n >= n_threshold:
    # 超临界确认，按 n 的超额程度给分
    hawkes_confirmation = min(1.0, (n - n_threshold) / n_threshold + 0.6)
else:
    hawkes_confirmation = 0.0  # 否决
```

---

## 4. 方向决策

### 4.1 降噪优先（不做什么）

```
1. Ω < q68 → 不给方向（环境不可读）
2. OP 密集区 → 不给方向（理论不适用）
3. I(Γ) < I_critical → 不给方向（结构不清晰）
4. n < n_threshold → 不给方向（突破不自持）    ← 新增
5. R < 0 → 不给方向（结构-执行反相）
6. E.quality < q80 → 不给方向（质量不足）
7. confidence < 0.60 → 不给方向（置信度不足）
8. Structure-led 但 u 不在边缘 → 不给方向
```

### 4.2 模式判断（做什么）

**模式 1：Belief-led（执行主导）**
```
判据：
- D < quantile(D[D<0], 0.25)
- R > 0, z(R) ≥ 0.25
- n ≥ n_threshold（Hawkes 确认）        ← 新增
- E.quality > q80

方向：direction = sgn(E)

确认：
- 15m 框架内有 1m/5m 的 E 同向上穿
- dE/dt > 0（执行在加速）
- dΩ/dt符号一致性 > 0.6（方向持续）   ← 新增

做市商机制解读：
  做市商评估对手方毒性飙升，防御性撤单
  DOM 出现单侧流动性真空
  对应场景 D（真突破）
```

**模式 2：Structure-led（结构主导）**
```
判据：
- D > quantile(D[D>0], 0.75)
- R > 0, z(R) ≥ 0.25
- u ≤ q30 或 u ≥ q70（在边缘）
- n ≥ n_threshold（Hawkes 确认）        ← 新增

方向：
- u ≤ q30 → 做多（下沿回归）
- u ≥ q70 → 做空（上沿回归）

确认：
- POC 未大幅位移：std_5(POC) < quantile(std_rolling, 0.3)
- κ > quantile(κ, 0.75)（DOM 弹性强 = 结构稳固）

做市商机制解读：
  做市商在结构边缘持续铺设限价单（高 κ）
  结构回归力强，偏离会被拉回
  前提：Hawkes 确认动能自持，否则可能是流动性吸收（场景 C）
```

**模式 3：Co-dominant（共主导）**
```
判据：
- |D| < quantile range
- z(R) ≥ 0.25
- n ≥ n_threshold                        ← 新增

方向：direction = sgn(E)，降权

确认：
- EMA(20) 同向
- E.quality > q75

置信度：基础 0.5（低于其他模式）
```

### 4.3 多尺度结构确认 · MSI【⚠️ 需要验证】

```python
# 在原有条件之上叠加，不替代
C_ms = corr([Ω_5m, Ω_15m, Ω_30m])      # 跨尺度一致性
ΔΩ_multi = mean([dΩ_dt_5m, dΩ_dt_15m, dΩ_dt_30m])  # 多尺度延续速率
MSI_weight = sigmoid((C_ms - 0.5) / 0.1)

if MSI_weight > 0.6:
    open_position(confidence=MSI_weight)   # 正常入场
elif MSI_weight > 0.5 and ΔΩ_multi >= 0:
    open_probe(size=min_base_size * 0.5)   # 试探入场
else:
    skip()
```

---

## 5. 仓位管理

### 5.1 开仓仓位

```python
# 最大仓位（基于 Ω_weekly 初值）
if Ω_weekly_initial > 0.6:
    max_position = 1.0
elif Ω_weekly_initial > 0.5:
    max_position = 0.7
else:
    max_position = 0.5

# 根据置信度动态调整
if confidence > 0.75:
    size = max_position
elif confidence > 0.60:
    size = max_position × 0.5
else:
    size = 0  # 拒绝
```

### 5.2 MSI 引导的加减仓【⚠️ 需要验证】

```python
# 加仓：跨尺度延续被强化
if ΔΩ_multi > 0 and C_ms is rising:
    add_position(size=current_position * MSI_weight)

# 减仓：跨尺度动能衰减
elif ΔΩ_multi < 0 and C_ms is falling:
    reduce_position(size=current_position * (1 - MSI_weight))
```

---

## 6. 退出规则

### 6.1 立即退出

```
1. Ω < q68（可读性丧失）
2. R 由正转负（结构-执行反相）
3. confidence 跌破 0.3
4. dΩ/dt 方向反转且持续 2 bar
5. Belief-led 中 E 反向持续 2 bar
6. λ_excite(t) → μ 且 ∂²λ/∂t² < 0（动能耗尽）  ← 新增：Hawkes 衰减离场
```

**关于第 6 条的机制说明**：
```
不是等到"价格反转"才走——那是空间锚点思维。
而是在"动能耗尽"时走：
  λ(t) → μ 意味着系统重新满足鞅条件
  即使价格还没有反转，alpha 已经消失
  
对应场景 G（再平衡）：
  做市商重新铺设双向限价单
  λ_make ≈ λ_take 均衡恢复
  继续持有的期望值 ≤ 0
```

### 6.2 减仓 50%

```
1. confidence 跌至 0.3-0.5 区间
2. E.quality 跌破 q50
3. Ω-R 背离 > quantile(|div|, 0.75)
4. n 从 ≥ 1 下降到 0.5-1.0 区间（动能衰减但未完全耗尽）  ← 新增
```

### 6.3 MSI 退相干退出【⚠️ 需要验证】

```python
# 跨尺度退相干
if falling(Ω_5m) and falling(Ω_15m) and stale_or_falling(Ω_30m):
    trigger_exit("MSI退相干")
```

### 6.4 动态响应框架（MSI）【⚠️ 需要验证】

```python
class DynamicMSI:
    def should_exit_now(self, state, bars_held):
        # 边际分析：每个 bar 实时判断
        marginal_alpha = estimate_marginal_alpha(state)
        marginal_cost = estimate_marginal_cost(state)
        return marginal_cost > marginal_alpha

    def dynamic_response_matrix(self, S):
        # S = 趋势强度（背景独立的相对度量）
        if S > 0.7:   return "6-10 bars + 积极升级"
        elif S > 0.3: return "4-6 bars + 观察"
        elif S > 0:   return "2-4 bars + 快速退出"
        else:          return "1-3 bars + 不追踪"
```

---

## 7. 场景-策略映射

> 以下映射来自 MIF v2.4 Section 8.3 的七个场景分析

| 场景 | MIF 判定 | 策略动作 | 关键判别指标 |
|------|---------|---------|------------|
| A 均衡做市 | UIS | 不交易 | n≈0, VR(q)≈1 |
| B 幌骗 | UIS | 不交易 | ω_t 崩溃 → Ω 被拉低 |
| C 流动性吸收 | IS候选被否决 | 不交易 | Ω 瞬升但 n≪1 |
| D 真突破 | **IS 确认** | **趋势跟随** | I(Γ)>I_c AND n≥1 |
| E 止损猎杀 | 边界场景 | MSI过滤或被骗 | n瞬间≥1但方向反转 |
| F 双向混沌 | OP 密集区 | 强制暂停 | |dΩ/dt|极高但一致率低 |
| G 再平衡 | IS→UIS | 离场 | λ(t)→μ, n衰减<1 |

**关键观察**：七个场景中只有场景 D 给出执行信号。策略的核心是高选择性。

---

## 8. 风险与边界

### 8.1 理论边界

```
极端事件：OP（Overload）发生时，信息-时间权衡可能失效
强外源期：|S_ext| ≫ ⟨S_ext⟩，标记为"外源期"暂停
低流动性：n_levels < 20，DOM 信息不足
OP 密集区：信息过载但方向不可识别（计算不可约）
```

### 8.2 策略风险

**已知脆弱点——止损猎杀（场景 E）**：
```
如果猎杀者的 Phase 1（止损级联）持续时间 ≥ MSI 窗口，
且 n ≥ 1，MIF 无法区分这与真突破。
猎杀者掌握了 MIF 不观测的信息（止损单分布）。
这是认知有界性的实例，无法通过算法消除。

缓解措施：
- MSI 窗口要求（4-6 bars 确认）过滤大部分快速猎杀
- dΩ/dt 符号一致性检测排除快速反转
- 严格止损限制单笔最大亏损
```

**数据质量风险**：
```
Cluster 提取失败 → E.quality 退化为 κ（DOM-only 代理）
DOM 覆盖率 < 80% → 降级告警，不停止交易
时间戳不匹配 → DOM 优先策略（ADR-004）
```

**过度优化风险**：
```
缓解：
- 始终保留"无条件基线组"做 A/B 对比
- 每个过滤条件都测试其增量价值（p < 0.05 才保留）
- Hawkes 阈值使用分位数，不用固定值
```

### 8.3 执行风险

```
成本模型：
  Taker fee: 0.05%
  滑点估计: 0.05%
  总成本: ~0.10% per side = ~0.20% round-trip

  在 15m 窗口下 20bp 是关键门槛
  E.quality 必须预测能覆盖此成本

流动性约束：
  仓位上限: 日均成交量的 1%
  分批建仓: 2-3 笔
```

### 8.4 范式危机标准

**立即停止交易，启动 30 天审查**：
```
满足以下 3 项或以上：
□ 相关性衰减：corr(Ω, 胜率) < 0.1
□ 止损频率异常：> 60%
□ 逆向盈亏：UIS 赚钱, IS 亏钱
□ E.quality 无区分度：top 20% vs bottom 80%, p > 0.05
□ 元认知失准：confidence 校准误差 > 0.15
□ Hawkes 判别无效：n ≥ 1 与后续收益无相关   ← 新增
□ 样本外完全失效：30 天胜率 < 10%
```

---

## 9. 成功标准

### 9.1 不期望

```
❌ 预测所有价格变动
❌ 胜率 > 50%
❌ 零假阳性
❌ 所有 regime 都有效
```

### 9.2 期望

```
✅ 识别何时可预测（IS vs UIS）
✅ IS 态：胜率 > 25%, 盈亏比 > 2.0
✅ UIS 态：拒绝交易或极轻仓
✅ E.quality 有区分度：top 20% vs bottom 80% 显著差异
✅ Hawkes n 有区分度：n≥1 vs n<1 的后续收益显著不同  ← 新增
✅ confidence 与实际胜率校准误差 < 15%
✅ 策略在 30 天样本外仍有效 (p < 0.05)
```

---

## 10. 附录

### 10.1 关键公式速查

**结构层**：
```python
Ω = [ω_s × ω_t × ω_p]^(1/3)
I(Γ) = EMA_9[(z_comp + z_POC + z_u) / 3]
E = (sgn(delta), |z(delta)|, quality)
R = z(I(Γ)) × z(E)
D = (|z(I(Γ))| - |z(E)|) / (|z(I(Γ))| + |z(E)|)
κ = min(autocorr(Σask, lag=1, W=12), autocorr(Σbid, lag=1, W=12))
```

**事件层**：
```python
λ(t) = μ + Σ α·exp(−β(t − t_i))
n = α / β
IS确认 = I(Γ) > I_critical AND n ≥ n_threshold
λ_excite(t) = Σ α·exp(−β(t − t_i))  # 剩余动能
```

**动力学**：
```python
dΩ/dt = (Ω_t - mean(Ω[-W:])) / Δt       # W=5
d²Ω/dt² = (dΩ/dt - dΩ/dt_{t-W}) / Δt    # 加速度
div J = ∂H_m/∂t - ∂I_em/∂t
sign_consistency = |Σ sgn(dΩ/dt[-M:])| / M  # OP 检测用
```

**置信度**：
```python
confidence = (
    0.20 × Ω_static_score +
    0.20 × Ω_trend_score +
    0.25 × E.quality +
    0.20 × hawkes_confirmation +
    0.15 × no_divergence_score
)
```

### 10.2 决策树

> **注**：以下决策树反映当前默认的门控顺序。门控顺序是可调整的工程参数，不是理论约束。

```
开仓决策：
├─ OP 密集区？ → REJECT（理论不适用）
├─ Ω < q68？ → REJECT（环境不可读）
├─ dΩ/dt 持续下降？ → REJECT（环境恶化）
├─ I(Γ) < I_critical？ → REJECT（结构不清晰）
├─ R < 0？ → REJECT（反相）
├─ dΩ/dt 越过 θ_crit → 启动 Hawkes MLE
│   ├─ n < n_threshold？ → REJECT（突破不自持）
│   └─ n ≥ n_threshold → IS 确认，继续
├─ E.quality < q80？ → REJECT（质量不足）
├─ confidence < 0.60？ → REJECT（置信度不足）
└─ 通过所有检查 → 判断模式
    ├─ D < D_belief：Belief-led → direction = sgn(E)
    ├─ D > D_struct 且 u 在边缘：Structure-led → 回归策略
    └─ |D| < threshold：Co-dominant → 谨慎跟随

持仓决策：
├─ Ω < q68？ → 立即平仓
├─ R 转负？ → 立即平仓
├─ confidence < 0.3？ → 立即平仓
├─ λ_excite → μ 且 ∂²λ/∂t² < 0？ → 立即平仓（动能耗尽）
├─ n 从 ≥1 降至 0.5-1.0？ → 减仓 50%
├─ MSI 退相干？ → 退出
├─ 边际成本 > 边际收益？ → MSI 动态退出 【⚠️ 需验证】
└─ 正常 → 继续持有
```

### 10.3 术语对照表

| 符号 | 名称 | 范围 | 用途 | 数据源 |
|------|------|------|------|--------|
| ε | 能量场 | ℝ⁺ | DOM层级能量 | DOM |
| Γ | Configuration | — | 完整DOM状态 | DOM |
| Ω | 相干度 | [0,1] | 市场可读性 | DOM |
| I(Γ) | 结构清晰度 | [0,1] | 结构存在性 | DOM |
| E | 执行推进 | ℝ³ | 推动方向/强度/质量 | Cluster/DOM |
| κ | DOM弹性 | [-1,1] | 防守侧回复能力 | DOM |
| R | 共振强度 | ℝ | 结构-执行同向性 | I(Γ)+E |
| D | 主导度 | [-1,1] | 谁在主导 | I(Γ)+E |
| n | Hawkes分支比 | [0,∞) | 突破自持性 | Tick/事件流 |
| λ(t) | Hawkes强度 | ℝ⁺ | 订单到达率 | Tick/事件流 |
| μ | 泊松基线 | ℝ⁺ | 鞅态基线强度 | 历史统计 |

### 10.4 ATAS 术语警告

| 上下文 | Ask | Bid |
|--------|-----|-----|
| **DOM** | 卖方挂单 | 买方挂单 |
| **Cluster** | **买方成交** | **卖方成交** |

---

## 11. 下一步行动

### Phase 1：V14_Final DOM-only

```
- 封装 DOM 导出器
- 实现 Ω、I(Γ)、κ 计算
- E 使用 (direction, magnitude)，quality = κ
- Hawkes MLE 的 tick 级数据管线设计
```

### Phase 2：Hawkes 集成

```
- 建立 dΩ/dt → 事件流 {t_i} 的映射管线
- 实现 Hawkes MLE（α, β 拟合）
- 验证 n 对突破持续性的判别力
- A/B 测试：有 Hawkes vs 无 Hawkes
```

### Phase 3：MifClusterExporter

```
- 独立 .csproj 产出 Cluster 精确字段
- E.quality 四分量完整实现
- ρ(dom_proxy) vs ρ(cluster_true) 偏差评估
```

### Phase 4：全面回测

```
目标指标：
- 胜率 > 25%
- 夏普 > 0.5
- 最大回撤 < 15%
- n≥1 vs n<1 分组收益差异 p < 0.05
```

---

**版本**：t2 (Test Version 2)  
**日期**：2026-03-12  
**状态**：待验证  
**基于**：MIF v2.4（经典随机过程框架）  
**核心理念**：鞅态规避 | 自激确认 | 动能衰减离场 | 认知边界

**相对 t1 的核心变化**：
- 引入 Hawkes 分支比 n 作为 IS 确认的必要条件
- 引入 λ_excite 衰减作为离场信号
- 引入 OP 密集区检测作为理论不适用态过滤
- confidence 公式新增 hawkes_confirmation 分量
- 决策树新增 Hawkes 判别节点
- 场景-策略映射表替代旧的抽象规则
