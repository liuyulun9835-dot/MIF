using ATAS.Indicators;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MIF.AtasIndicator
{
    /// <summary>
    /// MIF ATAS导出器 V17 - DOM/Cluster解耦版 + 活性检测
    ///
    /// V17关键更新：
    /// 1. DOM活性自检 - 检测并自动降级重复的DOM数据
    /// 2. 深拷贝保护 - 防止数组引用复用导致数据沿用
    /// 3. 增强统计 - DOM细分(levels/cumulative/unavailable) + Cluster详情
    /// 4. Bug修复 - urgency.ratio改为正确的u_buy/u_sell比值
    ///
    /// V16基础特性（保留）：
    /// - DOM/Cluster完全解耦 - epsilon仅来自DOM，cluster独立导出
    /// - 新增导出开关 - ExportDom, ExportCluster, IncludeClusterPrices
    /// - 消除cluster_fallback - DOM不可用时epsilon标记为"unavailable"
    /// - Cluster价格仅作标签 - 不参与ε计算，仅供参考
    ///
    /// V14基础特性（保留）：
    /// - 完整性保证 - OnDispose时回溯处理所有历史bars
    /// - OHLCV数据 - 完整的1分钟K线数据
    /// - 固定维度 - 保证数据维度一致性
    /// </summary>
    [DisplayName("MIF Exporter V17")]
    public class MifExporterV17 : Indicator
    {
        #region 配置参数
        
        private int _maxLevels = 20;
        
        [Display(Name = "Max DOM Levels", 
                 GroupName = "Export Settings",
                 Description = "FIXED dimension for all bars (5-50)")]
        [Range(5, 50)]
        public int MaxLevels
        {
            get => _maxLevels;
            set
            {
                _maxLevels = Math.Max(5, Math.Min(50, value));
                RecalculateValues();
            }
        }
        
        private string _outputDirectory = "";
        
        [Display(Name = "Output Directory", 
                 GroupName = "Export Settings",
                 Description = "Leave empty for default: Documents/MIF/atas_export")]
        public string OutputDirectory
        {
            get => _outputDirectory;
            set
            {
                _outputDirectory = value;
                RecalculateValues();
            }
        }
        
        private bool _compressOutput = true;
        
        [Display(Name = "Compress Output", 
                 GroupName = "Performance",
                 Description = "Reorder non-zero levels to front (keeps fixed dimension)")]
        public bool CompressOutput
        {
            get => _compressOutput;
            set => _compressOutput = value;
        }
        
        private int _bufferSize = 100;
        
        [Display(Name = "Write Buffer Size", 
                 GroupName = "Performance",
                 Description = "Number of records to buffer before writing (1-1000)")]
        [Range(1, 1000)]
        public int BufferSize
        {
            get => _bufferSize;
            set => _bufferSize = Math.Max(1, Math.Min(1000, value));
        }
        
        private double _minDomVolume = 0.001;
        
        [Display(Name = "Min DOM Volume (BTC)", 
                 GroupName = "Data Quality",
                 Description = "Skip records where DOM volume < threshold")]
        [Range(0.0, 1.0)]
        public double MinDomVolume
        {
            get => _minDomVolume;
            set => _minDomVolume = value;
        }
        
        private bool _skipZeroTrades = true;

        [Display(Name = "Skip Zero Trades",
                 GroupName = "Data Quality",
                 Description = "Skip bars with no realized trades")]
        public bool SkipZeroTrades
        {
            get => _skipZeroTrades;
            set => _skipZeroTrades = value;
        }

        // === Cluster export and DOM/Cluster decoupling ===
        private bool _exportDom = true;

        [Display(Name = "Export DOM → epsilon",
                 GroupName = "Export Settings",
                 Description = "Export DOM depth as epsilon")]
        public bool ExportDom
        {
            get => _exportDom;
            set => _exportDom = value;
        }

        private bool _exportCluster = true;

        [Display(Name = "Export Cluster → cluster",
                 GroupName = "Export Settings",
                 Description = "Export price-level cluster snapshot (labels only)")]
        public bool ExportCluster
        {
            get => _exportCluster;
            set => _exportCluster = value;
        }

        private bool _includeClusterPrices = true;

        [Display(Name = "Include Cluster Prices (labels)",
                 GroupName = "Export Settings",
                 Description = "Prices as labels only, not used in ε")]
        public bool IncludeClusterPrices
        {
            get => _includeClusterPrices;
            set => _includeClusterPrices = value;
        }

        #endregion
        
        #region 内部状态
        
        private string? _outPath;
        private string? _alivePath;
        private DateTime _sessionStart;
        private int _totalBars = 0;
        private int _exportedRecords = 0;
        private int _skippedRecords = 0;
        private int _duplicateRecords = 0;
        private int _domSuccessCount = 0;
        private int _domFailCount = 0;
        private int _dimensionErrors = 0;
        private List<string> _recordBuffer = new();
        private HashSet<int> _processedBarIndices = new(); // V14: 改用bar索引

        // === DOM 细分统计 ===
        private int _domLevelsCount = 0;
        private int _domCumulativeCount = 0;
        private int _domUnavailableCount = 0;

        // === Cluster 统计 ===
        private int _clusterSuccessCount = 0;
        private int _clusterZeroCount = 0;
        private long _clusterEffectiveLevelsSum = 0;

        // === DOM 活性自检 ===
        private double[]? _lastAskVolumes = null;
        private int _domStaleCount = 0;
        private const int DOM_STALE_THRESHOLD = 5;
        
        #endregion
        
        public MifExporterV17()
        {
            Name = "MIF Exporter V17";
            
            try
            {
                string baseDir = string.IsNullOrWhiteSpace(_outputDirectory)
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "MIF", "atas_export")
                    : _outputDirectory;
                
                Directory.CreateDirectory(baseDir);
                
                _outPath = Path.Combine(baseDir, "bars.jsonl");
                _alivePath = Path.Combine(baseDir, "_alive.log");
                
                _sessionStart = DateTime.UtcNow;
                
                if (_alivePath != null)
                {
                    var asm = Assembly.GetExecutingAssembly().Location;
                    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
                    
                    File.AppendAllText(_alivePath,
                        $"\n{new string('=', 80)}\n" +
                        $"[V17-START] {DateTime.UtcNow:o}\n" +
                        $"DLL: {asm}\n" +
                        $"Version: {version}\n" +
                        $"Modified: {File.GetLastWriteTime(asm):o}\n" +
                        $"Output: {baseDir}\n" +
                        $"FIXED DIMENSION: {_maxLevels} levels (GUARANTEED)\n" +
                        $"OHLCV: ENABLED\n" +
                        $"Completeness: Full backtrack on dispose\n" +
                        $"Compress output: {_compressOutput}\n" +
                        $"UTC timezone: ENFORCED\n" +
                        $"Data source: DOM→epsilon; Cluster→cluster (decoupled)\n" +
                        $"Export DOM: {(_exportDom ? "YES" : "NO")}\n" +
                        $"Export Cluster: {(_exportCluster ? "YES" : "NO")}\n" +
                        $"Buffer size: {_bufferSize}\n" +
                        $"{new string('=', 80)}\n\n");
                }
            }
            catch (Exception ex)
            {
                if (_alivePath != null)
                {
                    try
                    {
                        File.AppendAllText(_alivePath,
                            $"[ERROR] Constructor failed: {ex.Message}\n");
                    }
                    catch { }
                }
            }
        }
        
        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar < 0 || _alivePath == null || _outPath == null) return;
            
            _totalBars++;
            
            if ((_totalBars & 1023) == 0)
            {
                LogHeartbeat();
            }
            
            try
            {
                ProcessBar(bar);
            }
            catch (Exception ex)
            {
                if ((bar & 63) == 0)
                {
                    LogError($"ProcessBar failed at bar {bar}", ex);
                }
            }
        }
        
        private void ProcessBar(int bar)
        {
            // V14: 使用bar索引去重
            if (_processedBarIndices.Contains(bar))
            {
                _duplicateRecords++;
                return;
            }
            
            var candle = GetCandle(bar);
            if (candle == null) return;
            
            // === UTC时区处理 ===
            var tOpen = candle.Time.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(candle.Time, DateTimeKind.Utc)
                : candle.Time.ToUniversalTime();
            
            // V14: 提取OHLCV数据
            double open = (double)candle.Open;
            double high = (double)candle.High;
            double low = (double)candle.Low;
            double close = (double)candle.Close;
            double volume = (double)candle.Volume;
            
            // === 获取DOM（强制每 bar 新快照）===
            DomSnapshotData? domSnapshot = null;
            try
            {
                // 强制每次调用获取新快照，不缓存
                domSnapshot = CaptureDomSnapshot(bar, candle);

                if (domSnapshot != null && domSnapshot.IsValid)
                {
                    _domSuccessCount++;

                    // === DOM 活性自检：检测连续相同数据 ===
                    if (_lastAskVolumes != null &&
                        domSnapshot.AllAskVolumes.Length == _lastAskVolumes.Length &&
                        domSnapshot.AllAskVolumes.SequenceEqual(_lastAskVolumes))
                    {
                        _domStaleCount++;

                        // 连续多次完全相同 → 降级为 unavailable
                        if (_domStaleCount >= DOM_STALE_THRESHOLD)
                        {
                            if ((bar & 63) == 0)
                            {
                                File.AppendAllText(_alivePath!,
                                    $"{DateTime.UtcNow:o} DOM-STALE bar={bar}: Same data for {_domStaleCount} consecutive bars, degrading to unavailable\n");
                            }
                            domSnapshot = null; // 标记为无效
                            _domUnavailableCount++;
                        }
                    }
                    else
                    {
                        _domStaleCount = 0; // 重置计数器
                    }

                    // 深拷贝当前 DOM 用于下次对比
                    if (domSnapshot != null)
                    {
                        _lastAskVolumes = (double[])domSnapshot.AllAskVolumes.Clone();

                        // 按数据源统计
                        if (domSnapshot.DataSource == "dom_levels")
                            _domLevelsCount++;
                        else if (domSnapshot.DataSource == "dom_cumulative")
                            _domCumulativeCount++;
                    }

                    if (domSnapshot != null &&
                        (domSnapshot.BestAskVolume < _minDomVolume ||
                         domSnapshot.BestBidVolume < _minDomVolume))
                    {
                        _skippedRecords++;
                        return;
                    }
                }
                else
                {
                    _domFailCount++;
                    _domUnavailableCount++;
                    _domStaleCount = 0; // 重置
                    _lastAskVolumes = null;
                }
            }
            catch (Exception ex)
            {
                _domFailCount++;
                _domUnavailableCount++;
                _domStaleCount = 0;
                _lastAskVolumes = null;
                if ((bar & 63) == 0)
                {
                    File.AppendAllText(_alivePath!,
                        $"{DateTime.UtcNow:o} DOM-ERROR bar={bar}: {ex.Message}\n");
                }
            }
            
            // === Epsilon构建：强制固定维度（仅来自DOM）===
            double[] ask = Array.Empty<double>();
            double[] bid = Array.Empty<double>();
            double[] askPrices = Array.Empty<double>();
            double[] bidPrices = Array.Empty<double>();
            string epsilonSource = "unavailable";
            int effectiveLevels = 0;

            if (ExportDom && domSnapshot != null && domSnapshot.IsValid && domSnapshot.ExtractedLevels >= 1)
            {
                // 使用DOM数据
                ask = domSnapshot.AllAskVolumes;
                bid = domSnapshot.AllBidVolumes;
                askPrices = domSnapshot.AllAskPrices;
                bidPrices = domSnapshot.AllBidPrices;
                effectiveLevels = domSnapshot.ExtractedLevels;
                epsilonSource = "dom_levels";

                // 维度验证
                if (ask.Length != _maxLevels || bid.Length != _maxLevels)
                {
                    _dimensionErrors++;
                    _skippedRecords++;
                    return;
                }
            }
            // Note: When DOM is unavailable, epsilon remains "unavailable"; no cluster_fallback
            
            // === Realized volume ===
            double realizedBuy = 0.0;
            double realizedSell = 0.0;
            
            var allLevels2 = candle.GetAllPriceLevels();
            if (allLevels2 != null)
            {
                var levelsList2 = allLevels2.ToList();
                foreach (var level in levelsList2)
                {
                    if (level != null)
                    {
                        realizedBuy += (double)level.Ask;
                        realizedSell += (double)level.Bid;
                    }
                }
            }
            
            if (_skipZeroTrades && realizedBuy == 0 && realizedSell == 0)
            {
                _skippedRecords++;
                return;
            }
            
            // === 压缩优化（可选）===
            if (_compressOutput && epsilonSource == "dom_levels")
            {
                var nonZeroIndices = new List<int>();
                for (int i = 0; i < _maxLevels; i++)
                {
                    if (ask[i] > 0 || bid[i] > 0)
                    {
                        nonZeroIndices.Add(i);
                    }
                }

                int nonZeroCount = nonZeroIndices.Count;

                if (nonZeroCount < _maxLevels * 0.7)
                {
                    var compressedAsk = new double[_maxLevels];
                    var compressedBid = new double[_maxLevels];
                    var compressedAskPrices = new double[_maxLevels];
                    var compressedBidPrices = new double[_maxLevels];

                    for (int i = 0; i < nonZeroCount; i++)
                    {
                        int idx = nonZeroIndices[i];
                        compressedAsk[i] = ask[idx];
                        compressedBid[i] = bid[idx];
                        compressedAskPrices[i] = askPrices[idx];
                        compressedBidPrices[i] = bidPrices[idx];
                    }

                    ask = compressedAsk;
                    bid = compressedBid;
                    askPrices = compressedAskPrices;
                    bidPrices = compressedBidPrices;
                }
            }
            
            // === Urgency计算 ===
            double uBuy = 0.0, uSell = 0.0, ratio = 0.0;
            string dataSource = "unavailable";

            if (domSnapshot != null && domSnapshot.IsValid)
            {
                double potBuy = Math.Max(domSnapshot.BestAskVolume, 1e-12);
                double potSell = Math.Max(domSnapshot.BestBidVolume, 1e-12);

                uBuy = realizedBuy / potBuy;
                uSell = realizedSell / potSell;
                // 修复：ratio = u_buy / u_sell (买卖比)
                ratio = uSell > 0 ? Math.Min(1000.0, uBuy / uSell) : (uBuy > 0 ? 1000.0 : 0.0);
                dataSource = domSnapshot.DataSource;
            }
            
            var tClose = tOpen.AddMinutes(1);

            // === Cluster 结构快照（与 DOM/epsilon 解耦）===
            var cluster = CaptureClusterLevels(bar);

            // === Cluster 统计 ===
            if (cluster.IsValid && cluster.N > 0)
            {
                _clusterSuccessCount++;
                _clusterEffectiveLevelsSum += cluster.N;
            }
            else if (ExportCluster)
            {
                _clusterZeroCount++;
            }

            // === JSON输出 ===
            var rec = new
            {
                header = new
                {
                    symbol = "BTCUSDT",
                    timeframe = "1m",
                    t_open = tOpen.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                    t_close = tClose.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                    version = "mif.v17.0",
                    exporter = "MIF.AtasIndicator.V17",
                    fixed_dimension = true,
                    dimension_size = _maxLevels
                },
                // V14: 新增OHLCV字段
                ohlcv = new
                {
                    open = open,
                    high = high,
                    low = low,
                    close = close,
                    volume = volume
                },
                epsilon = new
                {
                    source = epsilonSource,
                    dimension = _maxLevels,
                    effective_levels = effectiveLevels,
                    ask_vol = ask,
                    bid_vol = bid
                },
                cluster = ExportCluster && cluster.IsValid
                    ? new
                    {
                        source = cluster.Source,
                        dimension = _maxLevels,
                        effective_levels = cluster.N,
                        ask_vol = cluster.Ask,
                        bid_vol = cluster.Bid,
                        prices = _includeClusterPrices ? cluster.Prices : null, // labels only
                        note = "prices are labels only, NOT used in epsilon"
                    }
                    : null,
                trades = new
                {
                    buy = realizedBuy,
                    sell = realizedSell
                },
                urgency = new
                {
                    u_buy = uBuy,
                    u_sell = uSell,
                    ratio = ratio,
                    source = dataSource
                }
            };
            
            try
            {
                var json = JsonSerializer.Serialize(rec);
                _recordBuffer.Add(json);
                _exportedRecords++;
                _processedBarIndices.Add(bar); // V14: 标记已处理
                
                if (_recordBuffer.Count >= _bufferSize)
                {
                    FlushBuffer(tOpen);
                }
            }
            catch (Exception ex)
            {
                LogError("JSON serialization failed", ex);
            }
        }
        
        private void FlushBuffer(DateTime barTime)
        {
            if (_outPath == null || _recordBuffer.Count == 0) return;
            
            try
            {
                var utcTime = barTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(barTime, DateTimeKind.Utc)
                    : barTime.ToUniversalTime();
                    
                var dateKey = utcTime.ToString("yyyyMMdd");
                var dailyPath = Path.Combine(
                    Path.GetDirectoryName(_outPath)!,
                    $"bars_{dateKey}.jsonl");
                
                File.AppendAllLines(dailyPath, _recordBuffer);
                _recordBuffer.Clear();
            }
            catch (Exception ex)
            {
                LogError("FlushBuffer failed", ex);
            }
        }
        
        private DomSnapshotData? CaptureDomSnapshot(int bar, IndicatorCandle candle)
        {
            try
            {
                var depthInfo = MarketDepthInfo;
                if (depthInfo == null) return null;
                
                var dom = depthInfo.GetMarketDepthSnapshot();
                if (dom == null) return null;
                
                if (dom is System.Collections.IEnumerable enumerable)
                {
                    var elements = enumerable.Cast<object>().ToList();
                    if (!elements.Any()) return GetFallbackSnapshot(depthInfo);
                    
                    var asks = new List<(decimal price, decimal volume)>();
                    var bids = new List<(decimal price, decimal volume)>();
                    
                    foreach (var elem in elements)
                    {
                        var price = GetPropertyValue<decimal>(elem, "Price");
                        var volume = GetPropertyValue<decimal>(elem, "Volume");
                        var isAsk = GetPropertyValue<bool>(elem, "IsAsk");
                        var isBid = GetPropertyValue<bool>(elem, "IsBid");
                        
                        if (!price.HasValue || !volume.HasValue) continue;
                        
                        if (isAsk.HasValue && isAsk.Value)
                            asks.Add((price.Value, volume.Value));
                        else if (isBid.HasValue && isBid.Value)
                            bids.Add((price.Value, volume.Value));
                    }
                    
                    if (asks.Any() && bids.Any())
                    {
                        var sortedAsks = asks.OrderBy(x => x.price).Take(_maxLevels).ToList();
                        var sortedBids = bids.OrderByDescending(x => x.price).Take(_maxLevels).ToList();

                        var bestAsk = sortedAsks.First();
                        var bestBid = sortedBids.First();

                        // === 固定大小数组，zero-padding（强制深拷贝，每次新分配）===
                        // 每次都创建全新数组实例，避免共享引用
                        double[] askPrices = new double[_maxLevels];
                        double[] askVolumes = new double[_maxLevels];
                        double[] bidPrices = new double[_maxLevels];
                        double[] bidVolumes = new double[_maxLevels];
                        
                        for (int i = 0; i < sortedAsks.Count; i++)
                        {
                            askPrices[i] = (double)sortedAsks[i].price;
                            askVolumes[i] = (double)sortedAsks[i].volume;
                        }
                        
                        for (int i = 0; i < sortedBids.Count; i++)
                        {
                            bidPrices[i] = (double)sortedBids[i].price;
                            bidVolumes[i] = (double)sortedBids[i].volume;
                        }
                        
                        return new DomSnapshotData
                        {
                            BestAskPrice = (double)bestAsk.price,
                            BestBidPrice = (double)bestBid.price,
                            BestAskVolume = (double)bestAsk.volume,
                            BestBidVolume = (double)bestBid.volume,
                            
                            AllAskPrices = askPrices,
                            AllAskVolumes = askVolumes,
                            AllBidPrices = bidPrices,
                            AllBidVolumes = bidVolumes,
                            
                            TotalAskLevels = asks.Count,
                            TotalBidLevels = bids.Count,
                            ExtractedLevels = Math.Min(sortedAsks.Count, sortedBids.Count),
                            DataSource = "dom_levels",
                            IsValid = true
                        };
                    }
                    
                    return GetFallbackSnapshot(depthInfo);
                }
                
                return GetFallbackSnapshot(depthInfo);
            }
            catch
            {
                return null;
            }
        }
        
        private DomSnapshotData? GetFallbackSnapshot(dynamic depthInfo)
        {
            try
            {
                var cumAsk = depthInfo.CumulativeDomAsks;
                var cumBid = depthInfo.CumulativeDomBids;
                
                if (cumAsk > 0 && cumBid > 0)
                {
                    return new DomSnapshotData
                    {
                        BestAskPrice = 0,
                        BestBidPrice = 0,
                        BestAskVolume = (double)cumAsk,
                        BestBidVolume = (double)cumBid,
                        AllAskPrices = new double[_maxLevels],
                        AllAskVolumes = new double[_maxLevels],
                        AllBidPrices = new double[_maxLevels],
                        AllBidVolumes = new double[_maxLevels],
                        TotalAskLevels = 0,
                        TotalBidLevels = 0,
                        ExtractedLevels = 0,
                        DataSource = "dom_cumulative",
                        IsValid = true
                    };
                }
            }
            catch { }
            
            return null;
        }
        
        private void LogHeartbeat()
        {
            if (_alivePath == null) return;

            var uptime = DateTime.UtcNow - _sessionStart;
            var domSuccessRate = _domSuccessCount + _domFailCount > 0
                ? _domSuccessCount * 100.0 / (_domSuccessCount + _domFailCount)
                : 0;

            // DOM 细分统计
            var domLevelsRate = _domSuccessCount > 0 ? _domLevelsCount * 100.0 / _domSuccessCount : 0;
            var domCumulativeRate = _domSuccessCount > 0 ? _domCumulativeCount * 100.0 / _domSuccessCount : 0;
            var domUnavailableRate = (_domSuccessCount + _domFailCount) > 0
                ? _domUnavailableCount * 100.0 / (_domSuccessCount + _domFailCount)
                : 0;

            // Cluster 统计
            var clusterTotal = _clusterSuccessCount + _clusterZeroCount;
            var clusterSuccessRate = clusterTotal > 0 ? _clusterSuccessCount * 100.0 / clusterTotal : 0;
            var clusterAvgEffective = _clusterSuccessCount > 0
                ? (double)_clusterEffectiveLevelsSum / _clusterSuccessCount
                : 0;

            File.AppendAllText(_alivePath,
                $"[HEARTBEAT] {DateTime.UtcNow:o}\n" +
                $"  Uptime: {uptime.TotalMinutes:F1}m\n" +
                $"  Total bars: {_totalBars}\n" +
                $"  Exported: {_exportedRecords}\n" +
                $"  Skipped: {_skippedRecords}\n" +
                $"  Duplicates: {_duplicateRecords}\n" +
                $"  DOM success: {_domSuccessCount} ({domSuccessRate:F1}%)\n" +
                $"    ├─ DOM levels: {_domLevelsCount} ({domLevelsRate:F1}%)\n" +
                $"    ├─ DOM cumulative: {_domCumulativeCount} ({domCumulativeRate:F1}%)\n" +
                $"    └─ DOM unavailable: {_domUnavailableCount} ({domUnavailableRate:F1}%)\n" +
                $"  DOM stale count: {_domStaleCount} (consecutive)\n" +
                $"  Cluster success: {_clusterSuccessCount} / {clusterTotal} ({clusterSuccessRate:F1}%)\n" +
                $"  Cluster avg effective levels: {clusterAvgEffective:F1}\n" +
                $"  Dimension errors: {_dimensionErrors}\n" +
                $"  Buffer: {_recordBuffer.Count}/{_bufferSize}\n\n");
        }
        
        private void LogError(string message, Exception ex)
        {
            if (_alivePath == null) return;
            
            try
            {
                File.AppendAllText(_alivePath,
                    $"[ERROR] {DateTime.UtcNow:o} - {message}\n" +
                    $"  {ex.GetType().Name}: {ex.Message}\n" +
                    $"  Stack: {ex.StackTrace}\n\n");
            }
            catch { }
        }
        
        private T? GetPropertyValue<T>(object obj, string propertyName) where T : struct
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                if (prop == null) return null;
                
                var value = prop.GetValue(obj);
                if (value == null) return null;
                
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return null;
            }
        }
        
        protected override void OnDispose()
        {
            // V14: 回溯处理所有历史bars，确保完整性
            try
            {
                if (_alivePath != null)
                {
                    File.AppendAllText(_alivePath,
                        $"\n[V17-BACKTRACK] Starting full history scan from bar 0 to {CurrentBar - 1}...\n");
                }
                
                int backtrackCount = 0;
                for (int bar = 0; bar < CurrentBar; bar++)
                {
                    if (!_processedBarIndices.Contains(bar))
                    {
                        try
                        {
                            ProcessBar(bar);
                            backtrackCount++;
                        }
                        catch (Exception ex)
                        {
                            LogError($"Backtrack failed at bar {bar}", ex);
                        }
                    }
                }
                
                if (_alivePath != null)
                {
                    File.AppendAllText(_alivePath,
                        $"[V17-BACKTRACK] Completed. Recovered {backtrackCount} missing bars.\n\n");
                }
            }
            catch (Exception ex)
            {
                LogError("Backtrack failed", ex);
            }
            
            // 刷新剩余缓冲
            if (_recordBuffer.Count > 0)
            {
                FlushBuffer(DateTime.UtcNow);
            }
            
            if (_alivePath != null)
            {
                var uptime = DateTime.UtcNow - _sessionStart;
                var domSuccessRate = _domSuccessCount + _domFailCount > 0
                    ? _domSuccessCount * 100.0 / (_domSuccessCount + _domFailCount)
                    : 0;

                // DOM 细分统计
                var domLevelsRate = _domSuccessCount > 0 ? _domLevelsCount * 100.0 / _domSuccessCount : 0;
                var domCumulativeRate = _domSuccessCount > 0 ? _domCumulativeCount * 100.0 / _domSuccessCount : 0;
                var domUnavailableRate = (_domSuccessCount + _domFailCount) > 0
                    ? _domUnavailableCount * 100.0 / (_domSuccessCount + _domFailCount)
                    : 0;

                // Cluster 统计
                var clusterTotal = _clusterSuccessCount + _clusterZeroCount;
                var clusterSuccessRate = clusterTotal > 0 ? _clusterSuccessCount * 100.0 / clusterTotal : 0;
                var clusterAvgEffective = _clusterSuccessCount > 0
                    ? (double)_clusterEffectiveLevelsSum / _clusterSuccessCount
                    : 0;

                File.AppendAllText(_alivePath,
                    $"\n{new string('=', 80)}\n" +
                    $"[V17-END] {DateTime.UtcNow:o}\n" +
                    $"Session Summary:\n" +
                    $"  Duration: {uptime.TotalMinutes:F1} minutes\n" +
                    $"  Total bars processed: {_totalBars}\n" +
                    $"  Records exported: {_exportedRecords}\n" +
                    $"  Records skipped: {_skippedRecords}\n" +
                    $"  Duplicate bars filtered: {_duplicateRecords}\n" +
                    $"  DOM success rate: {domSuccessRate:F1}%\n" +
                    $"    ├─ DOM levels: {_domLevelsCount} ({domLevelsRate:F1}%)\n" +
                    $"    ├─ DOM cumulative: {_domCumulativeCount} ({domCumulativeRate:F1}%)\n" +
                    $"    └─ DOM unavailable: {_domUnavailableCount} ({domUnavailableRate:F1}%)\n" +
                    $"  Cluster success: {_clusterSuccessCount}/{clusterTotal} ({clusterSuccessRate:F1}%)\n" +
                    $"  Cluster avg effective levels: {clusterAvgEffective:F1}\n" +
                    $"  Dimension errors: {_dimensionErrors}\n" +
                    $"  FIXED DIMENSION: {_maxLevels} levels\n" +
                    $"  OHLCV: Included\n" +
                    $"  DOM exported: {(_exportDom ? "YES" : "NO")}\n" +
                    $"  Cluster exported: {(_exportCluster ? "YES" : "NO")}\n" +
                    $"  Compression: {(_compressOutput ? "ON" : "OFF")}\n" +
                    $"{new string('=', 80)}\n\n");
            }
            
            base.OnDispose();
        }
        
        private class DomSnapshotData
        {
            public double BestAskPrice { get; set; }
            public double BestBidPrice { get; set; }
            public double BestAskVolume { get; set; }
            public double BestBidVolume { get; set; }

            public double[] AllAskPrices { get; set; } = Array.Empty<double>();
            public double[] AllAskVolumes { get; set; } = Array.Empty<double>();
            public double[] AllBidPrices { get; set; } = Array.Empty<double>();
            public double[] AllBidVolumes { get; set; } = Array.Empty<double>();

            public int TotalAskLevels { get; set; }
            public int TotalBidLevels { get; set; }
            public int ExtractedLevels { get; set; }
            public string DataSource { get; set; } = "";
            public bool IsValid { get; set; }
        }

        private class ClusterSnapshot
        {
            public bool IsValid { get; set; }
            public int N { get; set; }
            public double[] Prices { get; set; } = Array.Empty<double>(); // labels only
            public double[] Ask { get; set; } = Array.Empty<double>();
            public double[] Bid { get; set; } = Array.Empty<double>();
            public string Source => "cluster_levels";
        }

        // Fixed dimension zero-padding (aligned to _maxLevels)
        private ClusterSnapshot CaptureClusterLevels(int bar)
        {
            var snap = new ClusterSnapshot { IsValid = false, N = 0 };

            if (!ExportCluster) return snap;

            if (AtasBindings.TryGetLevels(this, bar, out var lvls) && lvls.Length > 0)
            {
                int copy = Math.Min(lvls.Length, _maxLevels);

                var ask = new double[_maxLevels];
                var bid = new double[_maxLevels];
                var prices = new double[_maxLevels];

                for (int i = 0; i < copy; i++)
                {
                    ask[i] = lvls[i].Ask;
                    bid[i] = lvls[i].Bid;
                    prices[i] = lvls[i].Price ?? 0.0; // label only
                }

                snap.IsValid = true;
                snap.N = copy;
                snap.Ask = ask;
                snap.Bid = bid;
                snap.Prices = prices;
            }
            return snap;
        }
    }
}
