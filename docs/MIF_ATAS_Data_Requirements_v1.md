# MIF ATAS数据需求与实现指令 v1.0

> **目标**: 为MIF Strategy t1提供完整、准确的市场数据  
> **平台**: ATAS Platform (C# API)  
> **数据源**: BTCUSDT (Binance Futures)  
> **输出格式**: JSONL (一行一个bar)

---

## 1. 核心问题定义

### 1.1 问题树

**根问题**: 如何获取高质量的DOM和Cluster数据用于MIF策略?

```
├─ 问题1: DOM数据提取不稳定
│   ├─ 1.1: 历史回放时chart cache先于DOM加载 → 时间戳错位
│   ├─ 1.2: DOM层级数量动态变化 → 数组维度不一致
│   └─ 1.3: 缺失bars的DOM数据 → 覆盖率不足

├─ 问题2: Cluster数据缺失关键字段
│   ├─ 2.1: 需要buy/sell分离的成交量
│   ├─ 2.2: 需要delta (buy - sell)
│   └─ 2.3: 需要trades分布 (用于大单检测)

└─ 问题3: 数据格式与后续处理不匹配
    ├─ 3.1: JSONL嵌套结构复杂
    ├─ 3.2: 时间戳格式不一致
    └─ 3.3: 缺少元数据 (version, export_time)
```

---

## 2. 数据需求清单

### 2.1 DOM (Depth of Market) 数据

**用途**: 计算Ω, I(Ψ), E的DOM代理

**必须字段**:

| 字段名 | 类型 | 维度 | 说明 | 用于计算 |
|--------|------|------|------|---------|
| `timestamp` | ISO8601 string | - | Bar的UTC时间戳 | 时间对齐 |
| `ask_volumes` | decimal[] | [20] | 每层卖方挂单量 | ε, Ω, I(Ψ) |
| `bid_volumes` | decimal[] | [20] | 每层买方挂单量 | ε, Ω, I(Ψ) |
| `price_levels` | decimal[] | [20] | 每层价格 (仅作label) | POC/VA定位 |

**可选字段** (用于高级分析):

| 字段名 | 类型 | 说明 | 用途 |
|--------|------|------|------|
| `best_ask` | decimal | 最优卖价 | 价差计算 |
| `best_bid` | decimal | 最优买价 | 价差计算 |
| `mid_price` | decimal | (ask + bid) / 2 | 参考价格 |
| `spread` | decimal | best_ask - best_bid | 流动性指标 |

**数据质量要求**:
```
- 固定维度: 所有bar统一20层
- 缺失层级: 填充0 (不要用null)
- 覆盖率: > 90% (每个bar都有DOM快照)
- 采样时刻: bar收盘时的DOM状态
```

---

### 2.2 Cluster (成交簿) 数据

**用途**: 计算E的三维向量 (direction, magnitude, quality)

**必须字段**:

| 字段名 | 类型 | 维度 | 说明 | 用于计算 |
|--------|------|------|------|---------|
| `timestamp` | ISO8601 string | - | 与DOM对齐 | 时间对齐 |
| `buy_volumes` | decimal[] | [20] | 每层买方主动成交量 | delta, CVD |
| `sell_volumes` | decimal[] | [20] | 每层卖方主动成交量 | delta, CVD |
| `total_volume` | decimal | - | 总成交量 | 归一化 |
| `delta` | decimal | - | Σ(buy - sell) | E.direction |

**推荐字段** (用于E.quality):

| 字段名 | 类型 | 说明 | 用于计算 |
|--------|------|------|---------|
| `trades_count` | int | 成交笔数 | 大单检测 |
| `buy_trades` | int | 买方成交笔数 | 攻击强度 |
| `sell_trades` | int | 卖方成交笔数 | 防守强度 |
| `max_single_trade` | decimal | 最大单笔成交量 | 异常检测 |

**ATAS术语映射**:

| ATAS API | 含义 | MIF术语 |
|----------|------|---------|
| `PriceVolumeInfo.Ask` | 买方主动成交 (吃掉卖盘) | `buy_volumes[]` |
| `PriceVolumeInfo.Bid` | 卖方主动成交 (吃掉买盘) | `sell_volumes[]` |
| `IndicatorCandle.Delta` | 买卖成交差 | `delta` |
| `IndicatorCandle.Volume` | 总成交量 | `total_volume` |
| `PriceVolumeInfo.Trades` | 该层成交笔数 | `trades_count` |

**数据质量要求**:
```
- 固定维度: 与DOM对齐 (20层)
- 覆盖率: > 90%
- delta验证: Σ(buy - sell) ≈ IndicatorCandle.Delta (允许1%误差)
- 无负值: 所有volume ≥ 0
```

---

### 2.3 Bar元数据 (辅助信息)

**必须字段**:

| 字段名 | 类型 | 说明 | 用途 |
|--------|------|------|------|
| `open` | decimal | 开盘价 | 可选参考 |
| `high` | decimal | 最高价 | 可选参考 |
| `low` | decimal | 最低价 | 可选参考 |
| `close` | decimal | 收盘价 | 触发/回测 |
| `bar_index` | int | Bar序号 (从0开始) | 数据完整性检查 |

**推荐字段**:

| 字段名 | 类型 | 说明 |
|--------|------|------|
| `version` | string | Indicator版本号 (如"v18") |
| `export_timestamp` | ISO8601 | 导出时间 |
| `timeframe` | string | 时间框架 (如"1m") |

---

### 2.4 完整JSONL格式示例

```json
{
  "version": "v18",
  "export_timestamp": "2025-11-09T12:00:00Z",
  "timeframe": "1m",
  "bar_index": 0,
  "timestamp": "2025-11-01T00:00:00Z",
  "ohlc": {
    "open": 68234.50,
    "high": 68245.00,
    "low": 68220.00,
    "close": 68238.00
  },
  "dom": {
    "ask_volumes": [12.5, 8.3, 15.2, ..., 0, 0],
    "bid_volumes": [10.2, 13.4, 7.8, ..., 0, 0],
    "price_levels": [68238.5, 68239.0, 68239.5, ..., 68248.0, 68248.5],
    "best_ask": 68238.5,
    "best_bid": 68238.0,
    "spread": 0.5
  },
  "cluster": {
    "buy_volumes": [5.2, 3.1, 8.4, ..., 0, 0],
    "sell_volumes": [4.8, 2.9, 6.1, ..., 0, 0],
    "total_volume": 234.56,
    "delta": 12.34,
    "trades_count": 145,
    "buy_trades": 78,
    "sell_trades": 67,
    "max_single_trade": 15.6
  }
}
```

**关键设计原则**:
```
1. 固定数组长度 (20层) - 避免维度不匹配
2. 嵌套结构清晰 (dom, cluster, ohlc分组) - 易于解析
3. 时间戳一致 (ISO8601 UTC) - 跨系统兼容
4. 无null值 (用0填充) - 避免解析错误
5. 元数据完整 (version, bar_index) - 数据追溯
```

---

## 3. 当前进度与问题

### 3.1 已完成 (v13/v14)

**✓ DOM数据提取**:
- 固定20层数组
- 覆盖率93.2%
- JSONL导出

**✓ 基础Cluster数据**:
- delta计算
- total_volume
- 基础OHLC

### 3.2 待解决 (v18目标)

**问题1: 历史回放DOM时序错位**

**现象**:
```
历史回放时:
1. Chart cache先加载 → timestamp = T1
2. DOM数据后加载 → 覆盖timestamp = T2
3. 导致T1和T2不一致 → 数据错位
```

**解决方案**:
```csharp
// 核心思路: DOM优先策略
// 如果bar同时有DOM和Cluster:
//   - DOM的timestamp作为master
//   - Cluster数据关联到DOM的timestamp
//   - 丢弃不匹配的Cluster数据

// 伪代码
if (domData != null && domData.Timestamp != null) {
    masterTimestamp = domData.Timestamp;
    
    if (clusterData != null) {
        if (clusterData.Timestamp == masterTimestamp) {
            // 时间对齐,两者都保留
            exportData.DOM = domData;
            exportData.Cluster = clusterData;
        } else {
            // 时间不对齐,只保留DOM
            exportData.DOM = domData;
            exportData.Cluster = null;
            LogWarning($"Cluster时间不匹配: DOM={masterTimestamp}, Cluster={clusterData.Timestamp}");
        }
    } else {
        // 只有DOM
        exportData.DOM = domData;
        exportData.Cluster = null;
    }
} else if (clusterData != null) {
    // 只有Cluster (降级)
    masterTimestamp = clusterData.Timestamp;
    exportData.DOM = null;
    exportData.Cluster = clusterData;
}
```

**验证**:
- 导出后检查: DOM和Cluster的timestamp一致性 > 99%
- 监控warning日志: 不匹配事件 < 1%

---

**问题2: Cluster数据字段不全**

**缺失**:
- buy/sell分层的volumes数组
- trades_count, buy_trades, sell_trades
- max_single_trade

**解决方案**:

参考ATAS API文档:
```csharp
// 获取单个bar的Cluster数据
var candle = GetCandle(bar);

// 方法1: 遍历所有price levels
var priceLevels = candle.GetAllPriceLevels();
decimal[] buyVolumes = new decimal[20];
decimal[] sellVolumes = new decimal[20];
int[] tradesPerLevel = new int[20];

int levelIndex = 0;
foreach (var priceInfo in priceLevels.Take(20)) {
    // ATAS术语:
    // priceInfo.Ask = 买方主动成交 (AggressorBuy)
    // priceInfo.Bid = 卖方主动成交 (AggressorSell)
    buyVolumes[levelIndex] = priceInfo.Ask;
    sellVolumes[levelIndex] = priceInfo.Bid;
    tradesPerLevel[levelIndex] = priceInfo.Trades;
    levelIndex++;
}

// 填充剩余为0
for (int i = levelIndex; i < 20; i++) {
    buyVolumes[i] = 0;
    sellVolumes[i] = 0;
    tradesPerLevel[i] = 0;
}

// 方法2: 使用内置属性
decimal totalDelta = candle.Delta;
decimal totalVolume = candle.Volume;
int totalTrades = candle.Ticks;  // 总成交笔数

// 计算大单相关
decimal maxTrade = 0;
int buyTradesCount = 0;
int sellTradesCount = 0;

foreach (var priceInfo in priceLevels) {
    if (priceInfo.Ask > maxTrade) maxTrade = priceInfo.Ask;
    if (priceInfo.Bid > maxTrade) maxTrade = priceInfo.Bid;
    
    // 估算买卖笔数 (如果priceInfo.Trades不可分)
    // 简化: 按volume比例分配
    if (priceInfo.Ask + priceInfo.Bid > 0) {
        decimal askRatio = priceInfo.Ask / (priceInfo.Ask + priceInfo.Bid);
        buyTradesCount += (int)(priceInfo.Trades * askRatio);
        sellTradesCount += (int)(priceInfo.Trades * (1 - askRatio));
    }
}

// 导出到JSONL
var clusterData = new {
    buy_volumes = buyVolumes,
    sell_volumes = sellVolumes,
    total_volume = totalVolume,
    delta = totalDelta,
    trades_count = totalTrades,
    buy_trades = buyTradesCount,
    sell_trades = sellTradesCount,
    max_single_trade = maxTrade
};
```

**ATAS API关键类/方法**:

| API | 说明 | 返回类型 |
|-----|------|---------|
| `GetCandle(int bar)` | 获取指定bar的candle对象 | `IndicatorCandle` |
| `candle.GetAllPriceLevels()` | 获取所有价格层级 | `IEnumerable<PriceVolumeInfo>` |
| `PriceVolumeInfo.Ask` | 买方主动成交量 | `decimal` |
| `PriceVolumeInfo.Bid` | 卖方主动成交量 | `decimal` |
| `PriceVolumeInfo.Trades` | 该层成交笔数 | `int` |
| `IndicatorCandle.Delta` | 总delta | `decimal` |
| `IndicatorCandle.Volume` | 总成交量 | `decimal` |
| `IndicatorCandle.Ticks` | 总成交笔数 | `int` |

**文档参考**:
- ATAS API: https://docs.atas.net/en/md_DataFeedsCore_2Docs_2en_20025__ReceivingProcessingData.html
- IndicatorCandle: https://docs.atas.net/en/classATAS_1_1Indicators_1_1IndicatorCandle.html

---

**问题3: DOM数据固定层级实现**

**目标**: 始终导出20层,不足填0

**解决方案**:

```csharp
// 使用MarketDepth获取DOM数据
// 参考: https://docs.atas.net/en/classATAS_1_1Indicators_1_1ExtendedIndicator.html

protected override void OnCalculate(int bar, decimal value) {
    const int FIXED_LEVELS = 20;
    
    decimal[] askVolumes = new decimal[FIXED_LEVELS];
    decimal[] bidVolumes = new decimal[FIXED_LEVELS];
    decimal[] priceLevels = new decimal[FIXED_LEVELS];
    
    // 获取当前bar的市场深度
    // 注意: 需要在OnCalculate中实时获取,历史回放时需要特殊处理
    var candle = GetCandle(bar);
    
    // 方法A: 如果有MarketDepth数据
    // (历史回放时可能不可用,取决于ATAS版本)
    if (candle.HasMarketDepth) {
        var depth = candle.MarketDepth;  // 这个API可能不存在,需要验证
        // ...
    }
    
    // 方法B: 使用Volume Profile的POC/VA近似DOM
    // 这不是真正的DOM,但可以作为历史回放的fallback
    var priceLevels_temp = candle.GetAllPriceLevels().ToList();
    
    // 选择top 20层 (按volume排序)
    var top20 = priceLevels_temp
        .OrderByDescending(p => p.Volume)
        .Take(FIXED_LEVELS)
        .ToList();
    
    for (int i = 0; i < FIXED_LEVELS; i++) {
        if (i < top20.Count) {
            var level = top20[i];
            // 注意: Volume Profile不区分ask/bid
            // 需要用其他方法估算
            decimal totalVol = level.Volume;
            
            // 简化估算: 用delta比例分配
            if (level.Delta >= 0) {
                askVolumes[i] = totalVol * 0.5m + level.Delta * 0.5m;
                bidVolumes[i] = totalVol * 0.5m - level.Delta * 0.5m;
            } else {
                askVolumes[i] = totalVol * 0.5m + level.Delta * 0.5m;
                bidVolumes[i] = totalVol * 0.5m - level.Delta * 0.5m;
            }
            
            priceLevels[i] = level.Price;
        } else {
            askVolumes[i] = 0;
            bidVolumes[i] = 0;
            priceLevels[i] = 0;
        }
    }
    
    // 导出
    var domData = new {
        ask_volumes = askVolumes,
        bid_volumes = bidVolumes,
        price_levels = priceLevels,
        best_ask = top20.Count > 0 ? top20[0].Price + tickSize : 0,
        best_bid = top20.Count > 0 ? top20[0].Price : 0
    };
}
```

**警告**: 
- 历史回放时真实DOM数据可能不可用
- 上述方法B是用Volume Profile近似,不是真正的DOM
- 需要在实时和回放模式下分别测试

**验证方法**:
```
1. 实时模式: 对比ATAS内置DOM indicator
2. 历史回放: 检查数组维度一致性
3. 数据合理性: ask_volumes, bid_volumes无负值,总和 > 0
```

---

## 4. v18 Indicator修改指令

### 4.1 文件结构

```
MIF_ATAS_Exporter_v18/
├── MIF_Exporter_v18.cs          # 主indicator类
├── DataModels/
│   ├── BarData.cs               # Bar数据结构
│   ├── DOMData.cs               # DOM数据结构
│   └── ClusterData.cs           # Cluster数据结构
├── Exporters/
│   └── JSONLExporter.cs         # JSONL导出逻辑
└── README.md                    # 使用说明
```

### 4.2 核心修改点

**修改1: 时间戳统一策略**

```csharp
// File: MIF_Exporter_v18.cs

public class MIF_Exporter_v18 : Indicator {
    private Dictionary<int, BarData> _barDataCache = new Dictionary<int, BarData>();
    
    protected override void OnCalculate(int bar, decimal value) {
        // 步骤1: 获取或创建BarData
        if (!_barDataCache.ContainsKey(bar)) {
            _barDataCache[bar] = new BarData {
                BarIndex = bar,
                Version = "v18"
            };
        }
        
        var barData = _barDataCache[bar];
        
        // 步骤2: 提取DOM数据
        var domData = ExtractDOMData(bar);
        if (domData != null) {
            barData.DOM = domData;
            barData.MasterTimestamp = domData.Timestamp;  // DOM优先
        }
        
        // 步骤3: 提取Cluster数据
        var clusterData = ExtractClusterData(bar);
        if (clusterData != null) {
            // 时间对齐检查
            if (barData.MasterTimestamp != null) {
                if (clusterData.Timestamp == barData.MasterTimestamp) {
                    barData.Cluster = clusterData;
                } else {
                    LogWarning($"Bar {bar}: Cluster时间不匹配, 丢弃");
                }
            } else {
                // 只有Cluster,无DOM (降级)
                barData.Cluster = clusterData;
                barData.MasterTimestamp = clusterData.Timestamp;
            }
        }
        
        // 步骤4: 提取OHLC
        var candle = GetCandle(bar);
        barData.OHLC = new OHLCData {
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close
        };
        
        // 步骤5: 导出 (仅在bar确认后)
        if (bar < CurrentBar - 1) {  // 不是最新bar
            if (barData.IsComplete()) {
                ExportBarData(barData);
                _barDataCache.Remove(bar);  // 清理已导出
            }
        }
    }
}
```

**修改2: DOM数据提取 (固定20层)**

```csharp
private DOMData ExtractDOMData(int bar) {
    const int FIXED_LEVELS = 20;
    
    var candle = GetCandle(bar);
    if (candle == null) return null;
    
    var domData = new DOMData {
        Timestamp = candle.Time,  // UTC时间
        AskVolumes = new decimal[FIXED_LEVELS],
        BidVolumes = new decimal[FIXED_LEVELS],
        PriceLevels = new decimal[FIXED_LEVELS]
    };
    
    // 获取price levels (Volume Profile作为近似)
    var levels = candle.GetAllPriceLevels()
        .OrderByDescending(p => p.Volume)
        .Take(FIXED_LEVELS)
        .ToList();
    
    for (int i = 0; i < FIXED_LEVELS; i++) {
        if (i < levels.Count) {
            var level = levels[i];
            
            // 估算ask/bid分布 (用delta近似)
            decimal totalVol = level.Volume;
            decimal delta = level.Delta;
            
            domData.AskVolumes[i] = Math.Max(0, totalVol * 0.5m + delta * 0.5m);
            domData.BidVolumes[i] = Math.Max(0, totalVol * 0.5m - delta * 0.5m);
            domData.PriceLevels[i] = level.Price;
        } else {
            domData.AskVolumes[i] = 0;
            domData.BidVolumes[i] = 0;
            domData.PriceLevels[i] = 0;
        }
    }
    
    // 计算best bid/ask
    if (levels.Count > 0) {
        decimal tickSize = InstrumentInfo.TickSize;
        domData.BestBid = levels[0].Price;
        domData.BestAsk = levels[0].Price + tickSize;
        domData.Spread = tickSize;
    }
    
    return domData;
}
```

**修改3: Cluster数据提取 (完整字段)**

```csharp
private ClusterData ExtractClusterData(int bar) {
    const int FIXED_LEVELS = 20;
    
    var candle = GetCandle(bar);
    if (candle == null) return null;
    
    var clusterData = new ClusterData {
        Timestamp = candle.Time,
        BuyVolumes = new decimal[FIXED_LEVELS],
        SellVolumes = new decimal[FIXED_LEVELS],
        TotalVolume = candle.Volume,
        Delta = candle.Delta
    };
    
    // 获取所有price levels
    var levels = candle.GetAllPriceLevels().Take(FIXED_LEVELS).ToList();
    
    decimal maxTrade = 0;
    int buyTrades = 0;
    int sellTrades = 0;
    int totalTrades = 0;
    
    for (int i = 0; i < FIXED_LEVELS; i++) {
        if (i < levels.Count) {
            var level = levels[i];
            
            // ATAS术语转换
            clusterData.BuyVolumes[i] = level.Ask;   // 买方主动成交
            clusterData.SellVolumes[i] = level.Bid;  // 卖方主动成交
            
            // 追踪最大单笔成交
            if (level.Ask > maxTrade) maxTrade = level.Ask;
            if (level.Bid > maxTrade) maxTrade = level.Bid;
            
            // 估算买卖笔数
            totalTrades += level.Trades;
            if (level.Ask + level.Bid > 0) {
                decimal askRatio = level.Ask / (level.Ask + level.Bid);
                buyTrades += (int)(level.Trades * askRatio);
                sellTrades += (int)(level.Trades * (1 - askRatio));
            }
        } else {
            clusterData.BuyVolumes[i] = 0;
            clusterData.SellVolumes[i] = 0;
        }
    }
    
    clusterData.TradesCount = totalTrades;
    clusterData.BuyTrades = buyTrades;
    clusterData.SellTrades = sellTrades;
    clusterData.MaxSingleTrade = maxTrade;
    
    return clusterData;
}
```

**修改4: JSONL导出 (完整格式)**

```csharp
// File: Exporters/JSONLExporter.cs

public class JSONLExporter {
    private string _outputPath;
    private StreamWriter _writer;
    
    public void ExportBar(BarData barData) {
        var json = new {
            version = barData.Version,
            export_timestamp = DateTime.UtcNow.ToString("o"),
            timeframe = "1m",  // 从ChartInfo获取
            bar_index = barData.BarIndex,
            timestamp = barData.MasterTimestamp?.ToString("o"),
            
            ohlc = barData.OHLC != null ? new {
                open = barData.OHLC.Open,
                high = barData.OHLC.High,
                low = barData.OHLC.Low,
                close = barData.OHLC.Close
            } : null,
            
            dom = barData.DOM != null ? new {
                ask_volumes = barData.DOM.AskVolumes,
                bid_volumes = barData.DOM.BidVolumes,
                price_levels = barData.DOM.PriceLevels,
                best_ask = barData.DOM.BestAsk,
                best_bid = barData.DOM.BestBid,
                spread = barData.DOM.Spread
            } : null,
            
            cluster = barData.Cluster != null ? new {
                buy_volumes = barData.Cluster.BuyVolumes,
                sell_volumes = barData.Cluster.SellVolumes,
                total_volume = barData.Cluster.TotalVolume,
                delta = barData.Cluster.Delta,
                trades_count = barData.Cluster.TradesCount,
                buy_trades = barData.Cluster.BuyTrades,
                sell_trades = barData.Cluster.SellTrades,
                max_single_trade = barData.Cluster.MaxSingleTrade
            } : null
        };
        
        string jsonLine = JsonConvert.SerializeObject(json);
        _writer.WriteLine(jsonLine);
        _writer.Flush();
    }
}
```

### 4.3 数据模型定义

```csharp
// File: DataModels/BarData.cs

public class BarData {
    public string Version { get; set; }
    public int BarIndex { get; set; }
    public DateTime? MasterTimestamp { get; set; }
    
    public OHLCData OHLC { get; set; }
    public DOMData DOM { get; set; }
    public ClusterData Cluster { get; set; }
    
    public bool IsComplete() {
        return MasterTimestamp != null && 
               (DOM != null || Cluster != null) && 
               OHLC != null;
    }
}

public class DOMData {
    public DateTime Timestamp { get; set; }
    public decimal[] AskVolumes { get; set; }
    public decimal[] BidVolumes { get; set; }
    public decimal[] PriceLevels { get; set; }
    public decimal BestAsk { get; set; }
    public decimal BestBid { get; set; }
    public decimal Spread { get; set; }
}

public class ClusterData {
    public DateTime Timestamp { get; set; }
    public decimal[] BuyVolumes { get; set; }
    public decimal[] SellVolumes { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal Delta { get; set; }
    public int TradesCount { get; set; }
    public int BuyTrades { get; set; }
    public int SellTrades { get; set; }
    public decimal MaxSingleTrade { get; set; }
}

public class OHLCData {
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}
```

---

## 5. 验证清单

### 5.1 数据完整性

```
□ 每个bar都有唯一的bar_index
□ timestamp格式: ISO8601 UTC (如"2025-11-01T00:00:00Z")
□ DOM数组维度: 固定20
□ Cluster数组维度: 固定20
□ 无null值 (用0或{}填充)
□ delta验证: Σ(buy-sell) ≈ cluster.delta (允许1%误差)
```

### 5.2 数据质量

```
□ DOM覆盖率 > 90%
□ Cluster覆盖率 > 90%
□ DOM与Cluster时间戳匹配率 > 99%
□ 所有volume ≥ 0 (无负值)
□ spread > 0 (best_ask > best_bid)
□ total_volume > 0 (非零bar)
```

### 5.3 导出性能

```
□ 单bar导出时间 < 10ms
□ 36天数据导出 < 5分钟
□ JSONL文件大小合理 (< 500MB/月)
□ 内存使用 < 2GB
```

---

## 6. 后续Python处理

### 6.1 JSONL解析示例

```python
import json
import pandas as pd
import numpy as np

def load_mif_data(jsonl_path):
    """
    加载MIF JSONL数据
    
    Returns:
        df_bars: 基础bar数据 (timestamp, ohlc)
        df_dom: DOM数据 (每行20层)
        df_cluster: Cluster数据 (每行20层)
    """
    bars = []
    doms = []
    clusters = []
    
    with open(jsonl_path, 'r') as f:
        for line in f:
            data = json.loads(line.strip())
            
            # 基础bar信息
            bar = {
                'bar_index': data['bar_index'],
                'timestamp': pd.to_datetime(data['timestamp']),
                'open': data['ohlc']['open'],
                'high': data['ohlc']['high'],
                'low': data['ohlc']['low'],
                'close': data['ohlc']['close']
            }
            bars.append(bar)
            
            # DOM数据
            if data['dom'] is not None:
                dom = {
                    'bar_index': data['bar_index'],
                    'timestamp': pd.to_datetime(data['timestamp']),
                    'ask_vol': np.array(data['dom']['ask_volumes']),
                    'bid_vol': np.array(data['dom']['bid_volumes']),
                    'prices': np.array(data['dom']['price_levels']),
                    'best_ask': data['dom']['best_ask'],
                    'best_bid': data['dom']['best_bid'],
                    'spread': data['dom']['spread']
                }
                doms.append(dom)
            
            # Cluster数据
            if data['cluster'] is not None:
                cluster = {
                    'bar_index': data['bar_index'],
                    'timestamp': pd.to_datetime(data['timestamp']),
                    'buy_vol': np.array(data['cluster']['buy_volumes']),
                    'sell_vol': np.array(data['cluster']['sell_volumes']),
                    'delta': data['cluster']['delta'],
                    'volume': data['cluster']['total_volume'],
                    'trades': data['cluster']['trades_count'],
                    'buy_trades': data['cluster']['buy_trades'],
                    'sell_trades': data['cluster']['sell_trades'],
                    'max_trade': data['cluster']['max_single_trade']
                }
                clusters.append(cluster)
    
    df_bars = pd.DataFrame(bars).set_index('bar_index')
    df_dom = pd.DataFrame(doms).set_index('bar_index')
    df_cluster = pd.DataFrame(clusters).set_index('bar_index')
    
    return df_bars, df_dom, df_cluster
```

### 6.2 数据质量检查

```python
def validate_mif_data(df_bars, df_dom, df_cluster):
    """验证数据质量"""
    
    print("=== 数据完整性 ===")
    print(f"总bars: {len(df_bars)}")
    print(f"DOM覆盖率: {len(df_dom) / len(df_bars) * 100:.2f}%")
    print(f"Cluster覆盖率: {len(df_cluster) / len(df_bars) * 100:.2f}%")
    
    # 时间戳对齐检查
    aligned = df_dom.index.intersection(df_cluster.index)
    print(f"DOM-Cluster对齐率: {len(aligned) / len(df_dom) * 100:.2f}%")
    
    print("\n=== 数据质量 ===")
    
    # DOM检查
    if len(df_dom) > 0:
        ask_negative = (df_dom['ask_vol'].apply(lambda x: (x < 0).any())).sum()
        bid_negative = (df_dom['bid_vol'].apply(lambda x: (x < 0).any())).sum()
        print(f"DOM负值: Ask={ask_negative}, Bid={bid_negative}")
        
        spread_invalid = (df_dom['spread'] <= 0).sum()
        print(f"Spread无效 (<=0): {spread_invalid}")
    
    # Cluster检查
    if len(df_cluster) > 0:
        buy_negative = (df_cluster['buy_vol'].apply(lambda x: (x < 0).any())).sum()
        sell_negative = (df_cluster['sell_vol'].apply(lambda x: (x < 0).any())).sum()
        print(f"Cluster负值: Buy={buy_negative}, Sell={sell_negative}")
        
        # Delta验证
        calculated_delta = df_cluster.apply(
            lambda row: row['buy_vol'].sum() - row['sell_vol'].sum(), 
            axis=1
        )
        delta_error = ((calculated_delta - df_cluster['delta']).abs() / df_cluster['delta']).mean()
        print(f"Delta计算误差: {delta_error * 100:.2f}%")
    
    print("\n=== 数组维度 ===")
    if len(df_dom) > 0:
        dom_shapes = df_dom['ask_vol'].apply(len).value_counts()
        print(f"DOM数组长度分布:\n{dom_shapes}")
    
    if len(df_cluster) > 0:
        cluster_shapes = df_cluster['buy_vol'].apply(len).value_counts()
        print(f"Cluster数组长度分布:\n{cluster_shapes}")
```

---

## 7. Agent协作指令

### 7.1 给Claude Code / Cursor的指令

```markdown
# 任务: 实现MIF_Exporter_v18 ATAS Indicator

## 背景
- 平台: ATAS (C# API)
- 目标: 导出BTCUSDT的DOM和Cluster数据为JSONL格式
- 版本: v18 (继承v13/v14的改进)

## 当前问题
1. 历史回放时DOM和Cluster时间戳不一致 → 需要DOM优先策略
2. Cluster缺少buy/sell分层数据 → 需要遍历PriceVolumeInfo
3. DOM层级数量不固定 → 需要固定20层,不足填0

## 核心需求
参考本文档第2节"数据需求清单"和第4节"v18修改指令"

## 关键API
- `GetCandle(int bar)` → `IndicatorCandle`
- `candle.GetAllPriceLevels()` → `IEnumerable<PriceVolumeInfo>`
- `PriceVolumeInfo.Ask` = 买方主动成交 (不是挂单!)
- `PriceVolumeInfo.Bid` = 卖方主动成交
- `IndicatorCandle.Delta` = 总delta
- `IndicatorCandle.Volume` = 总成交量

## 实现优先级
1. 时间戳统一 (DOM优先)
2. Cluster完整字段 (buy/sell分层 + trades分布)
3. DOM固定20层
4. JSONL导出 (参考第2.4节格式)

## 验证标准
参考第5节"验证清单"
- DOM覆盖率 > 90%
- Cluster覆盖率 > 90%
- 时间戳匹配率 > 99%
- 所有数组长度 = 20

## 参考文档
- ATAS API: https://docs.atas.net/en/md_DataFeedsCore_2Docs_2en_20025__ReceivingProcessingData.html
- IndicatorCandle: https://docs.atas.net/en/classATAS_1_1Indicators_1_1IndicatorCandle.html

## 注意事项
⚠️ ATAS中Ask/Bid在DOM和Cluster含义相反
⚠️ 历史回放时真实DOM可能不可用,需要用Volume Profile近似
⚠️ 所有decimal数组初始化为固定长度20
```

### 7.2 分模块任务

**任务1: 数据模型定义**
```
创建 DataModels/ 文件夹
- BarData.cs
- DOMData.cs
- ClusterData.cs
- OHLCData.cs

参考: 第4.3节
验证: 可编译,无语法错误
```

**任务2: DOM提取逻辑**
```
实现 ExtractDOMData(int bar) 方法

输入: bar索引
输出: DOMData对象 (固定20层)

关键:
- 用GetAllPriceLevels获取Volume Profile
- 用delta估算ask/bid分布
- 不足20层填0

参考: 第4.2节修改2
验证: 数组长度恒为20,无负值
```

**任务3: Cluster提取逻辑**
```
实现 ExtractClusterData(int bar) 方法

输入: bar索引
输出: ClusterData对象 (完整字段)

关键:
- 遍历PriceVolumeInfo获取ask/bid (注意术语!)
- 计算trades分布
- 追踪max_single_trade

参考: 第4.2节修改3
验证: delta验证通过,trades_count > 0
```

**任务4: 时间戳统一**
```
实现 OnCalculate 主逻辑

关键:
- 使用BarData缓存
- DOM timestamp优先
- Cluster时间不匹配时丢弃并log warning

参考: 第4.2节修改1
验证: warning日志 < 1%
```

**任务5: JSONL导出**
```
实现 JSONLExporter 类

关键:
- 使用Newtonsoft.Json
- 每行一个JSON对象
- 立即flush避免丢失

参考: 第4.2节修改4
验证: 输出文件可被Python正常解析
```

---

## 8. 下一步

### 8.1 立即执行

```
1. 修复v18 indicator代码
2. 历史回放36天数据
3. 运行Python验证脚本 (第6.2节)
4. 确认数据质量达标
```

### 8.2 数据就绪后

```
1. 实现E三维化 (direction, magnitude, quality)
2. 计算CVD, DEPIN, large_trade_pct
3. 回测对比DOM代理 vs Cluster真实
```

---

## 附录: 关键术语澄清

### ATAS术语混淆警告

**关键问题**: ATAS中`Ask/Bid`在DOM和Cluster中含义**相反**！

| 上下文 | Ask | Bid |
|--------|-----|-----|
| **DOM** | 卖方挂单 | 买方挂单 |
| **Cluster** | **买方成交** | **卖方成交** |

**在代码中的体现**:
```csharp
// DOM context
domData.AskVolumes[i] = marketDepthInfo.Asks[i].Volume; // 卖方挂单

// Cluster context (PriceVolumeInfo)
buyVolume = info.Ask;  // 买方成交量 (吃掉了卖盘)
sellVolume = info.Bid; // 卖方成交量 (吃掉了买盘)
```

### MIF核心符号统一

| 标准符号 | 全称 | 定义 |
|---------|------|------|
| **ε** | epsilon能量 | DOM层级能量配置 |
| **Ψ** | Configuration | 完整DOM状态集合 |
| **Ω** | Omega相干度 | 市场可读性[0,1] |
| **ρ** | rho紧迫度比 | u_buy/u_sell |
| **I(Ψ)** | 结构清晰度 | POC稳定性指标 |

---

**版本**: v1.0  
**日期**: 2025-11-09  
**对应策略**: MIF_Strategy_t1.md  
**下一版本**: v1.1将在v18验证通过后发布
---

## 附录：阶段性过渡说明（Snapshot 对齐）

- 本阶段（V14_Final）仅输出 DOM-only 字段；Cluster 字段移至 v2 文档与 MifClusterExporter（v18 相关修复路径已弃用，见 ADR-004_V14_Final_DOM_only.md）。
- 参见《MIF_ATAS_Data_Requirements_v2.md》了解最新字段规范与导出契约。

