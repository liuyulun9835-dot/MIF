using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ATAS.Indicators;

namespace MIF.AtasIndicator
{
    /// <summary>
    /// MIF ATAS导出器 V20 - 沿用V18稳定实现，维持单文件导出逻辑
    ///
    /// V18关键更新：
    /// 1. Cluster读取修正 - 使用ISupportedPriceInfo.GetAllPriceLevels()（无参）
    /// 2. DOM历史语义修正 - 实时用snapshot，历史用GetMarketDepthSnapshotsAsync
    /// 3. 导出节流 - 只在新bar或bar关闭时输出一条，防止重复
    /// 4. 完全解耦 - DOM→epsilon、Cluster→cluster，杜绝cluster_fallback
    ///
    /// V17基础特性（保留）：
    /// - DOM活性自检 - 检测并自动降级重复的DOM数据
    /// - 深拷贝保护 - 防止数组引用复用导致数据沿用
    /// - 增强统计 - DOM细分(levels/cumulative/unavailable) + Cluster详情
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
    [DisplayName("MIF Exporter V20")]
    public class MifExporterV20 : Indicator
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
        private string? _cachedInstrument = null;
        private string? _cachedTimeframe = null;

        // === DOM 细分统计 ===
        private int _domLevelsCount = 0;
        private int _domUnavailableCount = 0;

        // === Cluster 统计 ===
        private int _clusterSuccessCount = 0;
        private int _clusterZeroCount = 0;
        private long _clusterEffectiveLevelsSum = 0;

        // === DOM 活性自检 ===
        private double[]? _lastAskVolumes = null;
        private int _domStaleCount = 0;
        private const int DOM_STALE_THRESHOLD = 5;
        private static readonly string[] DomPricePropertyNames = { "Price", "PriceDouble", "P" };
        private static readonly string[] DomVolumePropertyNames = { "Volume", "Vol", "Quantity", "Value", "Size" };
        private static readonly string[] DomAskFlagPropertyNames = { "IsAsk", "IsBuy" };
        private static readonly string[] DomBidFlagPropertyNames = { "IsBid", "IsSell" };
        private static readonly string[] DomSidePropertyNames = { "Side", "Direction", "Type" };
        private static readonly string[] DomAskCollectionPropertyNames = { "Asks", "AskLevels", "BuyLevels" };
        private static readonly string[] DomBidCollectionPropertyNames = { "Bids", "BidLevels", "SellLevels" };
        private static readonly string[] DomLevelCollectionPropertyNames = { "Levels", "Depth", "Dom", "Entries" };

        // === V18: 导出节流 ===
        private int _lastWrittenBar = -1;

        #endregion

        public MifExporterV20()
        {
            Name = "MIF Exporter V20";

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
                        $"[V20-START] {DateTime.UtcNow:o}\n" +
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
                        $"V20 Features: Cluster via ISupportedPriceInfo, DOM history support, Export throttling\n" +
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

        // === V18: 辅助判断是否为实时bar ===
        private bool IsRealtimeBar(int bar) => bar >= CurrentBar - 1;

        private void ProcessBar(int bar)
        {
            // V18: 导出节流 - 已写过的bar跳过
            if (bar == _lastWrittenBar)
            {
                _duplicateRecords++;
                return;
            }

            // V14: 使用bar索引去重
            if (_processedBarIndices.Contains(bar))
            {
                _duplicateRecords++;
                return;
            }

            var candle = GetCandle(bar);
            if (candle == null) return;

            // V18: 只在bar关闭时写（或者是最后一根的情况）
            bool isLast = bar >= CurrentBar - 1;
            // Note: 如果IsFormed可用，可以改为: bool formed = candle.IsFormed;
            // 这里用简单的判断：非最后一根的bar视为已形成
            bool formed = !isLast;
            if (!formed && !isLast) return; // 非形成、非最后的中间态不写

            // === UTC时区处理 ===
            var tOpen = candle.Time.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(candle.Time, DateTimeKind.Utc)
                : candle.Time.ToUniversalTime();
            var tClose = tOpen.AddMinutes(1);

            // V14: 提取OHLCV数据
            double open = (double)candle.Open;
            double high = (double)candle.High;
            double low = (double)candle.Low;
            double close = (double)candle.Close;
            double volume = (double)candle.Volume;

            // === 获取DOM（区分实时/历史）===
            DomSnapshotData? domSnapshot = null;
            try
            {
                if (ExportDom)
                {
                    domSnapshot = IsRealtimeBar(bar)
                        ? CaptureDomSnapshot(bar, candle)
                        : CaptureDomHistorySnapshot(bar, candle, tOpen, tClose);

                    if (domSnapshot != null && domSnapshot.IsValid)
                    {
                        _domSuccessCount++;

                        if (_lastAskVolumes != null &&
                            domSnapshot.AllAskVolumes.Length == _lastAskVolumes.Length &&
                            domSnapshot.AllAskVolumes.SequenceEqual(_lastAskVolumes))
                        {
                            _domStaleCount++;

                            if (_domStaleCount >= DOM_STALE_THRESHOLD)
                            {
                                if ((bar & 63) == 0)
                                {
                                    File.AppendAllText(_alivePath!,
                                        $"{DateTime.UtcNow:o} DOM-STALE bar={bar}: Same data for {_domStaleCount} consecutive bars, degrading to unavailable\n");
                                }

                                domSnapshot = null;
                                _domUnavailableCount++;
                            }
                        }
                        else
                        {
                            _domStaleCount = 0;
                        }

                        if (domSnapshot != null)
                        {
                            _lastAskVolumes = (double[])domSnapshot.AllAskVolumes.Clone();

                            if (domSnapshot.DataSource == "dom_levels")
                            {
                                _domLevelsCount++;
                            }
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
                        _domStaleCount = 0;
                        _lastAskVolumes = null;
                    }
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

            // === Cluster 原始层级（用于成交量和结构）===
            PriceLevelDTO[] clusterLevels;
            if (!TryExtractClusterLevels(candle, out clusterLevels) || clusterLevels == null)
            {
                clusterLevels = Array.Empty<PriceLevelDTO>();
                _clusterZeroCount++;
            }
            else
            {
                _clusterSuccessCount++;
                _clusterEffectiveLevelsSum += clusterLevels.Length;
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

            if (clusterLevels.Length > 0)
            {
                realizedBuy = clusterLevels.Sum(l => l.Ask);
                realizedSell = clusterLevels.Sum(l => l.Bid);
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

            // === V18: Cluster 结构快照（与 DOM/epsilon 解耦，使用新方法）===
            var cluster = BuildClusterSnapshot(clusterLevels);

            // === Cluster 统计 ===
            var clusterValid = cluster.IsValid && cluster.N > 0;
            if (!clusterValid && ExportCluster && clusterLevels.Length > 0)
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
                    version = "mif.v20.0",
                    exporter = "MIF.AtasIndicator.V20",
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
                _lastWrittenBar = bar; // V18: 记录最后写入的bar

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
                if (depthInfo == null)
                {
                    return null;
                }

                var dom = depthInfo.GetMarketDepthSnapshot();
                return BuildDomSnapshot(dom, "dom_levels");
            }
            catch
            {
                return null;
            }
        }

        // === V18: 历史DOM获取（使用异步接口）===
        private DomSnapshotData? CaptureDomHistorySnapshot(int bar, IndicatorCandle candle, DateTime tOpen, DateTime tClose)
        {
            try
            {
                var provider = GetDataProviderInstance();
                if (provider == null)
                {
                    return null;
                }

                var instrument = TryGetInstrumentKey(candle);
                var timeframe = TryGetTimeframeKey();

                var snapshots = RequestHistoricalDomSnapshots(provider, tOpen, tClose, _maxLevels, instrument, timeframe);
                if (snapshots == null)
                {
                    return null;
                }

                object? candidate = null;
                DateTime? candidateTime = null;

                foreach (var item in snapshots)
                {
                    if (item == null) continue;

                    var time = GetPropertyValue<DateTime>(item!, "Time") ?? GetPropertyValue<DateTime>(item!, "Timestamp");
                    if (time.HasValue)
                    {
                        if (time.Value <= tClose && (candidateTime == null || time.Value > candidateTime.Value))
                        {
                            candidateTime = time.Value;
                            candidate = item;
                        }
                    }
                    else
                    {
                        candidate = item;
                    }
                }

                return BuildDomSnapshot(candidate, "dom_levels");
            }
            catch
            {
                return null;
            }
        }

        private DomSnapshotData? BuildDomSnapshot(object? source, string dataSource)
        {
            if (source == null)
            {
                return null;
            }

            if (source is IEnumerable enumerable)
            {
                return BuildDomFromFlatEnumerable(enumerable, dataSource);
            }

            var asks = TryGetEnumerable(source, DomAskCollectionPropertyNames);
            var bids = TryGetEnumerable(source, DomBidCollectionPropertyNames);
            if (asks != null && bids != null)
            {
                return BuildDomFromSideEnumerables(asks, bids, dataSource);
            }

            var levels = TryGetEnumerable(source, DomLevelCollectionPropertyNames);
            if (levels != null)
            {
                return BuildDomFromFlatEnumerable(levels, dataSource);
            }

            return null;
        }

        private DomSnapshotData? BuildDomFromFlatEnumerable(IEnumerable enumerable, string dataSource)
        {
            var asks = new List<(double price, double volume)>();
            var bids = new List<(double price, double volume)>();

            foreach (var item in enumerable)
            {
                if (item == null) continue;

                var price = TryGetDouble(item, DomPricePropertyNames);
                var volume = TryGetDouble(item, DomVolumePropertyNames);
                if (!price.HasValue || !volume.HasValue) continue;

                var isAsk = TryGetBool(item, DomAskFlagPropertyNames);
                var isBid = TryGetBool(item, DomBidFlagPropertyNames);

                if (isAsk == true)
                {
                    asks.Add((price.Value, volume.Value));
                    continue;
                }

                if (isBid == true)
                {
                    bids.Add((price.Value, volume.Value));
                    continue;
                }

                var side = GetStringProperty(item, DomSidePropertyNames);
                if (!string.IsNullOrWhiteSpace(side))
                {
                    if (side.StartsWith("a", StringComparison.OrdinalIgnoreCase) ||
                        side.StartsWith("s", StringComparison.OrdinalIgnoreCase) ||
                        side.StartsWith("o", StringComparison.OrdinalIgnoreCase))
                    {
                        asks.Add((price.Value, volume.Value));
                    }
                    else if (side.StartsWith("b", StringComparison.OrdinalIgnoreCase))
                    {
                        bids.Add((price.Value, volume.Value));
                    }
                }
            }

            return CreateDomSnapshot(asks, bids, dataSource);
        }

        private DomSnapshotData? BuildDomFromSideEnumerables(IEnumerable askEnumerable, IEnumerable bidEnumerable, string dataSource)
        {
            var asks = ExtractDomSideLevels(askEnumerable);
            var bids = ExtractDomSideLevels(bidEnumerable);
            return CreateDomSnapshot(asks, bids, dataSource);
        }

        private List<(double price, double volume)> ExtractDomSideLevels(IEnumerable source)
        {
            var list = new List<(double price, double volume)>();
            foreach (var item in source)
            {
                if (item == null) continue;
                var price = TryGetDouble(item, DomPricePropertyNames);
                var volume = TryGetDouble(item, DomVolumePropertyNames);
                if (price.HasValue && volume.HasValue)
                {
                    list.Add((price.Value, volume.Value));
                }
            }
            return list;
        }

        private DomSnapshotData? CreateDomSnapshot(List<(double price, double volume)> asks, List<(double price, double volume)> bids, string dataSource)
        {
            if (asks.Count == 0 || bids.Count == 0)
            {
                return null;
            }

            var sortedAsks = asks.OrderBy(x => x.price).Take(_maxLevels).ToList();
            var sortedBids = bids.OrderByDescending(x => x.price).Take(_maxLevels).ToList();

            if (sortedAsks.Count == 0 || sortedBids.Count == 0)
            {
                return null;
            }

            var bestAsk = sortedAsks[0];
            var bestBid = sortedBids[0];

            var askPrices = new double[_maxLevels];
            var askVolumes = new double[_maxLevels];
            var bidPrices = new double[_maxLevels];
            var bidVolumes = new double[_maxLevels];

            for (int i = 0; i < sortedAsks.Count; i++)
            {
                askPrices[i] = sortedAsks[i].price;
                askVolumes[i] = sortedAsks[i].volume;
            }

            for (int i = 0; i < sortedBids.Count; i++)
            {
                bidPrices[i] = sortedBids[i].price;
                bidVolumes[i] = sortedBids[i].volume;
            }

            return new DomSnapshotData
            {
                BestAskPrice = bestAsk.price,
                BestBidPrice = bestBid.price,
                BestAskVolume = bestAsk.volume,
                BestBidVolume = bestBid.volume,
                AllAskPrices = askPrices,
                AllAskVolumes = askVolumes,
                AllBidPrices = bidPrices,
                AllBidVolumes = bidVolumes,
                TotalAskLevels = asks.Count,
                TotalBidLevels = bids.Count,
                ExtractedLevels = Math.Min(sortedAsks.Count, sortedBids.Count),
                DataSource = dataSource,
                IsValid = true
            };
        }

        private IEnumerable? TryGetEnumerable(object source, string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var value = GetPropertyObject(source, name);
                if (value is IEnumerable enumerable)
                {
                    return enumerable;
                }
            }

            return null;
        }

        private double? TryGetDouble(object obj, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var value = GetPropertyValue<double>(obj, name);
                if (value.HasValue) return value.Value;

                var alt = GetPropertyValue<decimal>(obj, name);
                if (alt.HasValue) return (double)alt.Value;
            }

            foreach (var name in propertyNames)
            {
                var raw = GetPropertyObject(obj, name);
                if (raw == null) continue;

                switch (raw)
                {
                    case double d:
                        return d;
                    case float f:
                        return f;
                    case long l:
                        return l;
                    case int i:
                        return i;
                    case decimal dec:
                        return (double)dec;
                    case string s when double.TryParse(s, out var parsed):
                        return parsed;
                }
            }

            return null;
        }

        private bool? TryGetBool(object obj, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var value = GetPropertyValue<bool>(obj, name);
                if (value.HasValue) return value.Value;

                var intVal = GetPropertyValue<int>(obj, name);
                if (intVal.HasValue) return intVal.Value != 0;

                var str = GetStringProperty(obj, name);
                if (!string.IsNullOrWhiteSpace(str))
                {
                    if (bool.TryParse(str, out var parsedBool))
                    {
                        return parsedBool;
                    }

                    if (str.Equals("ask", StringComparison.OrdinalIgnoreCase) ||
                        str.Equals("sell", StringComparison.OrdinalIgnoreCase) ||
                        str.Equals("offer", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (str.Equals("bid", StringComparison.OrdinalIgnoreCase) ||
                        str.Equals("buy", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return null;
        }

        private string? GetStringProperty(object obj, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var value = GetPropertyObject(obj, name);
                if (value == null) continue;

                if (value is string s)
                {
                    return s;
                }

                return value.ToString();
            }

            return null;
        }

        private object? GetPropertyObject(object obj, string propertyName)
        {
            try
            {
                return obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
            }
            catch
            {
                return null;
            }
        }

        private object? GetDataProviderInstance()
        {
            var type = GetType();
            while (type != null)
            {
                var prop = type.GetProperty("DataProvider", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                {
                    try
                    {
                        return prop.GetValue(this);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                type = type.BaseType;
            }

            return null;
        }

        private IEnumerable? RequestHistoricalDomSnapshots(object provider, DateTime from, DateTime to, int depth, string? instrument, string? timeframe)
        {
            try
            {
                var providerType = provider.GetType();
                var methods = providerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "GetMarketDepthSnapshotsAsync");

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    object?[] args;

                    if (parameters.Length == 0)
                    {
                        args = Array.Empty<object?>();
                    }
                    else
                    {
                        var request = CreateMarketDepthRequest(providerType.Assembly, from, to, depth, instrument, timeframe);
                        if (request == null)
                        {
                            continue;
                        }

                        if (parameters.Length == 1)
                        {
                            args = new[] { request };
                        }
                        else if (parameters.Length == 2 && parameters[1].ParameterType == typeof(CancellationToken))
                        {
                            args = new object?[] { request, CancellationToken.None };
                        }
                        else
                        {
                            continue;
                        }
                    }

                    var taskObj = method.Invoke(provider, args);
                    if (taskObj is Task task)
                    {
                        task.ConfigureAwait(false).GetAwaiter().GetResult();
                        var resultProp = task.GetType().GetProperty("Result");
                        if (resultProp != null && resultProp.GetValue(task) is IEnumerable enumerable)
                        {
                            return enumerable;
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private object? CreateMarketDepthRequest(Assembly providerAssembly, DateTime from, DateTime to, int depth, string? instrument, string? timeframe)
        {
            var candidateTypeNames = new[]
            {
                "ATAS.DataProviders.MarketDepthSnapshotRequest",
                "ATAS.Indicators.Helpers.MarketDepthSnapshotRequest",
                "ATAS.MarketData.MarketDepthSnapshotRequest"
            };

            foreach (var asm in EnumerateAssemblies(providerAssembly))
            {
                foreach (var typeName in candidateTypeNames)
                {
                    var type = asm.GetType(typeName);
                    if (type == null) continue;

                    object? request = null;
                    try
                    {
                        request = Activator.CreateInstance(type);
                    }
                    catch
                    {
                        request = null;
                    }

                    if (request == null) continue;

                    SetPropertyIfExists(request, new[] { "From", "Start", "StartTime" }, from);
                    SetPropertyIfExists(request, new[] { "To", "End", "EndTime" }, to);
                    SetPropertyIfExists(request, new[] { "Depth", "Levels", "DepthLimit" }, depth);

                    if (!string.IsNullOrWhiteSpace(instrument))
                    {
                        SetPropertyIfExists(request, new[] { "Instrument", "Symbol", "Security", "Ticker" }, instrument);
                    }

                    if (!string.IsNullOrWhiteSpace(timeframe))
                    {
                        SetPropertyIfExists(request, new[] { "TimeFrame", "Timeframe", "Interval", "Aggregation" }, timeframe);
                    }

                    return request;
                }
            }

            return null;
        }

        private IEnumerable<Assembly> EnumerateAssemblies(Assembly primary)
        {
            yield return primary;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == primary) continue;
                yield return asm;
            }
        }

        private string? TryGetInstrumentKey(IndicatorCandle candle)
        {
            if (!string.IsNullOrWhiteSpace(_cachedInstrument))
            {
                return _cachedInstrument;
            }

            object? instrumentInfo = GetPropertyObject(this, "InstrumentInfo") ?? GetPropertyObject(this, "Instrument");
            string? candidate = null;

            if (instrumentInfo != null)
            {
                candidate = GetStringProperty(instrumentInfo, "Instrument", "Symbol", "Name", "Code", "Ticker");
            }

            if (string.IsNullOrWhiteSpace(candidate) && candle != null)
            {
                candidate = GetStringProperty(candle, "Instrument", "Symbol");
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = GetStringProperty(this, "Symbol", "Ticker");
            }

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                _cachedInstrument = candidate;
            }

            return _cachedInstrument;
        }

        private string? TryGetTimeframeKey()
        {
            if (!string.IsNullOrWhiteSpace(_cachedTimeframe))
            {
                return _cachedTimeframe;
            }

            string? candidate = GetStringProperty(this, "TimeFrame", "Timeframe", "Interval");
            if (string.IsNullOrWhiteSpace(candidate))
            {
                var series = GetPropertyObject(this, "Series") ?? GetPropertyObject(this, "CurrentSeries");
                if (series != null)
                {
                    candidate = GetStringProperty(series, "TimeFrame", "Timeframe", "Interval");
                }
            }

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                _cachedTimeframe = candidate;
            }

            return _cachedTimeframe;
        }

        private static void SetPropertyIfExists(object target, string[] propertyNames, object? value)
        {
            if (target == null || value == null)
            {
                return;
            }

            foreach (var name in propertyNames)
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null || !prop.CanWrite) continue;

                try
                {
                    var propertyType = prop.PropertyType;
                    var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                    object? convertedValue = value;
                    if (value is string strValue && underlying != typeof(string))
                    {
                        if (underlying == typeof(DateTime) && DateTime.TryParse(strValue, out var parsedDate))
                        {
                            convertedValue = parsedDate;
                        }
                        else if (underlying.IsEnum)
                        {
                            convertedValue = Enum.Parse(underlying, strValue, true);
                        }
                        else
                        {
                            convertedValue = Convert.ChangeType(strValue, underlying);
                        }
                    }
                    else if (!underlying.IsAssignableFrom(value.GetType()))
                    {
                        if (underlying.IsEnum)
                        {
                            convertedValue = Enum.Parse(underlying, value.ToString()!, true);
                        }
                        else
                        {
                            convertedValue = Convert.ChangeType(value, underlying);
                        }
                    }

                    prop.SetValue(target, convertedValue);
                    return;
                }
                catch
                {
                    // continue to next candidate
                }
            }
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
                        $"\n[V20-BACKTRACK] Starting full history scan from bar 0 to {CurrentBar - 1}...\n");
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
                        $"[V20-BACKTRACK] Completed. Recovered {backtrackCount} missing bars.\n\n");
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
                    $"[V20-END] {DateTime.UtcNow:o}\n" +
                    $"Session Summary:\n" +
                    $"  Duration: {uptime.TotalMinutes:F1} minutes\n" +
                    $"  Total bars processed: {_totalBars}\n" +
                    $"  Records exported: {_exportedRecords}\n" +
                    $"  Records skipped: {_skippedRecords}\n" +
                    $"  Duplicate bars filtered: {_duplicateRecords}\n" +
                    $"  DOM success rate: {domSuccessRate:F1}%\n" +
                    $"    ├─ DOM levels: {_domLevelsCount} ({domLevelsRate:F1}%)\n" +
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


        private bool TryExtractClusterLevels(IndicatorCandle candle, out PriceLevelDTO[] levels)
        {
            levels = Array.Empty<PriceLevelDTO>();

            try
            {
                if (candle is ISupportedPriceInfo priceInfo)
                {
                    var allLevels = priceInfo.GetAllPriceLevels();

                    if (allLevels is IEnumerable enumerableLevels)
                    {
                        var validLevels = new List<PriceLevelDTO>();

                        foreach (var pl in enumerableLevels)
                        {
                            if (pl == null)
                            {
                                continue;
                            }

                            double ask = ExtractNumeric(pl, "Ask", "AskVolume", "BuyVolume") ?? 0.0;
                            double bid = ExtractNumeric(pl, "Bid", "BidVolume", "SellVolume") ?? 0.0;
                            double? price = ExtractNumeric(pl, "Price", "PriceLevel");

                            if (ask == 0.0 && bid == 0.0)
                            {
                                continue;
                            }

                            validLevels.Add(new PriceLevelDTO
                            {
                                Ask = ask,
                                Bid = bid,
                                Price = price
                            });
                        }

                        if (validLevels.Count > 0)
                        {
                            levels = validLevels.ToArray();
                            return true;
                        }
                    }
                }

                var candidates = new[] { "Clusters", "Cluster", "Footprint", "PriceLevels" };
                var candleType = candle.GetType();

                foreach (var propName in candidates)
                {
                    var prop = candleType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (prop == null)
                    {
                        continue;
                    }

                    var container = prop.GetValue(candle);
                    if (container == null)
                    {
                        continue;
                    }

                    if (container is IEnumerable enumerable)
                    {
                        var validLevels = new List<PriceLevelDTO>();

                        foreach (var item in enumerable)
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            double? ask = ExtractNumeric(item, "Ask", "AskVolume", "BuyVolume");
                            double? bid = ExtractNumeric(item, "Bid", "BidVolume", "SellVolume");
                            double? price = ExtractNumeric(item, "Price", "PriceLevel");

                            if (ask == null && bid == null)
                            {
                                continue;
                            }

                            validLevels.Add(new PriceLevelDTO
                            {
                                Ask = ask ?? 0.0,
                                Bid = bid ?? 0.0,
                                Price = price
                            });
                        }

                        if (validLevels.Count > 0)
                        {
                            levels = validLevels.ToArray();
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_alivePath != null)
                {
                    File.AppendAllText(_alivePath, $"[WARN] TryExtractClusterLevels failed: {ex.Message}\n");
                }
            }

            return false;
        }

        private double? ExtractNumeric(object obj, params string[] propertyNames)
        {
            var objType = obj.GetType();

            foreach (var name in propertyNames)
            {
                var prop = objType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop == null)
                {
                    continue;
                }

                var value = prop.GetValue(obj);
                if (value == null)
                {
                    continue;
                }

                try
                {
                    return Convert.ToDouble(value);
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        private class ClusterSnapshot
        {
            public bool IsValid { get; set; }
            public int N { get; set; }
            public double[]? Prices { get; set; } // labels only
            public double[] Ask { get; set; } = Array.Empty<double>();
            public double[] Bid { get; set; } = Array.Empty<double>();
            public string Source => "cluster_levels";
        }

        private ClusterSnapshot BuildClusterSnapshot(PriceLevelDTO[] levels)
        {
            var snapshot = new ClusterSnapshot { IsValid = false, N = 0 };

            if (!ExportCluster || levels.Length == 0)
            {
                return snapshot;
            }

            int copy = Math.Min(levels.Length, _maxLevels);

            var ask = new double[_maxLevels];
            var bid = new double[_maxLevels];
            double[]? prices = _includeClusterPrices ? new double[_maxLevels] : null;

            for (int i = 0; i < copy; i++)
            {
                ask[i] = levels[i].Ask;
                bid[i] = levels[i].Bid;

                if (prices != null)
                {
                    prices[i] = levels[i].Price ?? double.NaN;
                }
            }

            snapshot.IsValid = copy > 0;
            snapshot.N = copy;
            snapshot.Ask = ask;
            snapshot.Bid = bid;
            snapshot.Prices = prices;

            return snapshot;
        }
    }
}
