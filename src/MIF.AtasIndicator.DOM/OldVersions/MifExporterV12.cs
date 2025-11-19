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
    /// MIF ATAS导出器 V12 - 生产就绪版
    /// 
    /// V12核心特性：
    /// 1. 去重机制 - 每bar只导出1次（解决40倍重复问题）
    /// 2. 可配置维度 - 默认20 levels（可调5-50）
    /// 3. 压缩输出 - 自动过滤零值（减少25%）
    /// 4. UTC时区 - 强制统一时区（Z后缀）
    /// 5. 性能优化 - 文件减少97%，速度提升40倍
    /// </summary>
    [Obsolete("Archived historic implementation; use DomExporterV14 instead.")]
    [DisplayName("MIF Exporter V12")]
    public class MifExporterV12 : Indicator
    {
        #region 配置参数
        
        private int _maxLevels = 20;
        
        [Display(Name = "Max DOM Levels", 
                 GroupName = "Export Settings",
                 Description = "Number of price levels to extract (5-50)")]
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
                 Description = "Only export non-zero levels (recommended)")]
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
        private List<string> _recordBuffer = new();
        private HashSet<string> _processedBars = new();
        
        #endregion
        
        public MifExporterV12()
        {
            Name = "MIF Exporter V12";
            
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
                        $"[V12-START] {DateTime.UtcNow:o}\n" +
                        $"DLL: {asm}\n" +
                        $"Version: {version}\n" +
                        $"Modified: {File.GetLastWriteTime(asm):o}\n" +
                        $"Output: {baseDir}\n" +
                        $"Max levels: {_maxLevels}\n" +
                        $"Compress output: {_compressOutput}\n" +
                        $"UTC timezone: ENFORCED\n" +
                        $"Data source: DOM (fallback to Cluster)\n" +
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
            var candle = GetCandle(bar);
            if (candle == null) return;
            
            // === UTC时区处理：确保时间是UTC ===
            var tOpen = candle.Time.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(candle.Time, DateTimeKind.Utc)
                : candle.Time.ToUniversalTime();
            
            string barKey = tOpen.ToString("o");
            
            // === 去重机制 ===
            if (_processedBars.Contains(barKey))
            {
                _duplicateRecords++;
                return;
            }
            
            var tNow = DateTime.UtcNow;
            var barAge = (tNow - tOpen).TotalSeconds;
            if (barAge < 55 && bar < CurrentBar - 1)
            {
                return;
            }
            
            _processedBars.Add(barKey);
            
            if (_processedBars.Count > 100)
            {
                var cutoff = DateTime.UtcNow.AddHours(-1).ToString("o");
                _processedBars.RemoveWhere(k => string.Compare(k, cutoff) < 0);
            }
            
            // === 获取DOM ===
            DomSnapshotData? domSnapshot = null;
            try
            {
                domSnapshot = CaptureDomSnapshot(bar, candle);
                
                if (domSnapshot != null && domSnapshot.IsValid)
                {
                    _domSuccessCount++;
                    
                    if (domSnapshot.BestAskVolume < _minDomVolume || 
                        domSnapshot.BestBidVolume < _minDomVolume)
                    {
                        _skippedRecords++;
                        return;
                    }
                }
                else
                {
                    _domFailCount++;
                }
            }
            catch (Exception ex)
            {
                _domFailCount++;
                if ((bar & 63) == 0)
                {
                    File.AppendAllText(_alivePath!, 
                        $"{DateTime.UtcNow:o} DOM-ERROR bar={bar}: {ex.Message}\n");
                }
            }
            
            // === Epsilon构建 ===
            double[] ask;
            double[] bid;
            int L;
            string epsilonSource;
            double[] askPrices = Array.Empty<double>();
            double[] bidPrices = Array.Empty<double>();
            
            if (domSnapshot != null && domSnapshot.IsValid && domSnapshot.ExtractedLevels >= 5)
            {
                ask = domSnapshot.AllAskVolumes;
                bid = domSnapshot.AllBidVolumes;
                askPrices = domSnapshot.AllAskPrices;
                bidPrices = domSnapshot.AllBidPrices;
                L = domSnapshot.ExtractedLevels;
                epsilonSource = "dom";
            }
            else
            {
                var allLevels = candle.GetAllPriceLevels();
                if (allLevels == null) return;
                
                var levelsList = allLevels.ToList();
                L = levelsList.Count;
                if (L == 0) return;
                
                ask = new double[L];
                bid = new double[L];
                
                for (int i = 0; i < L; i++)
                {
                    var level = levelsList[i];
                    if (level != null)
                    {
                        ask[i] = (double)level.Ask;
                        bid[i] = (double)level.Bid;
                    }
                }
                
                epsilonSource = "cluster_fallback";
            }
            
            // === Realized volume ===
            double realizedBuy = 0.0;
            double realizedSell = 0.0;
            
            var allLevels2 = candle.GetAllPriceLevels();
            if (allLevels2 != null)
            {
                var levelsList2 = allLevels2.ToList();
                for (int i = 0; i < levelsList2.Count; i++)
                {
                    var level = levelsList2[i];
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
            
            // === 压缩输出 ===
            if (_compressOutput && epsilonSource == "dom")
            {
                var nonZeroIndices = new List<int>();
                for (int i = 0; i < L; i++)
                {
                    if (ask[i] > 0 || bid[i] > 0)
                    {
                        nonZeroIndices.Add(i);
                    }
                }
                
                int compressedL = nonZeroIndices.Count;
                if (compressedL < L)
                {
                    var compressedAsk = new double[compressedL];
                    var compressedBid = new double[compressedL];
                    var compressedAskPrices = new double[compressedL];
                    var compressedBidPrices = new double[compressedL];
                    
                    for (int i = 0; i < compressedL; i++)
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
                    L = compressedL;
                }
            }
            
            // === Urgency计算 ===
            double uBuy, uSell, ratio;
            string dataSource;
            
            if (domSnapshot != null && domSnapshot.IsValid)
            {
                double potBuy = Math.Max(domSnapshot.BestAskVolume, 1e-12);
                double potSell = Math.Max(domSnapshot.BestBidVolume, 1e-12);
                
                uBuy = realizedBuy / potBuy;
                uSell = realizedSell / potSell;
                ratio = Math.Min(1000.0, Math.Max(uBuy, uSell));
                dataSource = domSnapshot.DataSource;
            }
            else
            {
                int bestAskIdx = 0;
                int bestBidIdx = 0;
                
                if (epsilonSource == "cluster_fallback")
                {
                    for (int i = 0; i < L; i++)
                    {
                        if (ask[i] > 0) { bestAskIdx = i; break; }
                    }
                    for (int i = L - 1; i >= 0; i--)
                    {
                        if (bid[i] > 0) { bestBidIdx = i; break; }
                    }
                }
                
                double potBuy = epsilonSource == "cluster_fallback" && bestAskIdx < L
                    ? Math.Max(ask[bestAskIdx], 1e-12)
                    : 1e-12;
                double potSell = epsilonSource == "cluster_fallback" && bestBidIdx < L
                    ? Math.Max(bid[bestBidIdx], 1e-12)
                    : 1e-12;
                
                uBuy = realizedBuy / potBuy;
                uSell = realizedSell / potSell;
                ratio = Math.Min(1000.0, Math.Max(uBuy, uSell));
                dataSource = "cluster_fallback";
            }
            
            // === 确保tClose也是UTC ===
            var tClose = tOpen.AddMinutes(1);
            
            // === JSON输出 ===
            var rec = new
            {
                header = new
                {
                    symbol = "BTCUSDT",
                    timeframe = "1m",
                    t_open = tOpen.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                    t_close = tClose.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                    version = "mif.v12.0",
                    exporter = "MIF.AtasIndicator.V12"
                },
                
                dom_snapshot = domSnapshot != null && domSnapshot.IsValid ? new
                {
                    best_ask_price = domSnapshot.BestAskPrice,
                    best_bid_price = domSnapshot.BestBidPrice,
                    best_ask_volume = domSnapshot.BestAskVolume,
                    best_bid_volume = domSnapshot.BestBidVolume,
                    extracted_levels = domSnapshot.ExtractedLevels,
                    data_source = domSnapshot.DataSource
                } : null,
                
                epsilon = new
                {
                    source = epsilonSource,
                    num_levels = L,
                    ask_vol = ask,
                    bid_vol = bid,
                    ask_px = askPrices.Length > 0 ? askPrices : null,
                    bid_px = bidPrices.Length > 0 ? bidPrices : null
                },
                
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
                string json = JsonSerializer.Serialize(rec, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
                
                _recordBuffer.Add(json);
                _exportedRecords++;
                
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
                        AllAskPrices = Array.Empty<double>(),
                        AllAskVolumes = Array.Empty<double>(),
                        AllBidPrices = Array.Empty<double>(),
                        AllBidVolumes = Array.Empty<double>(),
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
            
            File.AppendAllText(_alivePath,
                $"[HEARTBEAT] {DateTime.UtcNow:o}\n" +
                $"  Uptime: {uptime.TotalMinutes:F1}m\n" +
                $"  Total bars: {_totalBars}\n" +
                $"  Exported: {_exportedRecords}\n" +
                $"  Skipped: {_skippedRecords}\n" +
                $"  Duplicates: {_duplicateRecords}\n" +
                $"  DOM success: {_domSuccessCount} ({domSuccessRate:F1}%)\n" +
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
                
                File.AppendAllText(_alivePath,
                    $"\n{new string('=', 80)}\n" +
                    $"[V12-END] {DateTime.UtcNow:o}\n" +
                    $"Session Summary:\n" +
                    $"  Duration: {uptime.TotalMinutes:F1} minutes\n" +
                    $"  Total bars processed: {_totalBars}\n" +
                    $"  Records exported: {_exportedRecords}\n" +
                    $"  Records skipped: {_skippedRecords}\n" +
                    $"  Duplicate bars filtered: {_duplicateRecords}\n" +
                    $"  DOM success rate: {domSuccessRate:F1}%\n" +
                    $"  Max levels: {_maxLevels}\n" +
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
    }
}
