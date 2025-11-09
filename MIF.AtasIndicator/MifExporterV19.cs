using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ATAS.Indicators;
using MIF.AtasIndicator.DataModels;
using MIF.AtasIndicator.Exporters;

namespace MIF.AtasIndicator
{
    [DisplayName("MIF Exporter V19")]
    public sealed class MifExporterV19 : Indicator
    {
        private const int FixedLevels = 20;
        private const string Version = "v19";

        private static readonly string[] PriceLevelMethodNames = { "GetAllPriceLevels" };
        private static readonly string[] PricePropertyNames = { "Price", "PriceDouble", "P" };
        private static readonly string[] AskPropertyNames = { "Ask", "AskVolume", "AskVol", "AggressorBuy", "Buy" };
        private static readonly string[] BidPropertyNames = { "Bid", "BidVolume", "BidVol", "AggressorSell", "Sell" };
        private static readonly string[] VolumePropertyNames = { "Volume", "Vol", "Quantity" };
        private static readonly string[] DeltaPropertyNames = { "Delta" };
        private static readonly string[] TradesPropertyNames = { "Trades", "Ticks" };
        private static readonly string[] ChartInfoPropertyNames = { "ChartInfo", "Chart", "Owner" };
        private static readonly string[] InstrumentPropertyNames = { "InstrumentInfo", "Instrument" };
        private static readonly string[] SymbolPropertyNames = { "Symbol", "Name", "FullName", "Ticker" };
        private static readonly string[] TimeFramePropertyNames = { "TimeFrame", "Timeframe", "Interval", "Period" };
        private static readonly string[] TimeFrameTextPropertyNames = { "TimeFrameText", "Text", "Name" };

        private readonly Dictionary<int, BarData> _barCache = new();
        private readonly HashSet<int> _exportedBars = new();
        private JSONLExporter? _exporter;
        private string? _resolvedTimeframe;
        private string? _resolvedSymbol;
        private string? _exportFilePath;
        private string? _resolvedOutputDirectory;
        private string? _alivePath;
        private readonly Dictionary<int, string> _lastGateReasons = new();
        private bool _startLogged;

        public MifExporterV19()
        {
        }

        [Display(Name = "Output Directory",
            GroupName = "Export",
            Description = "Leave empty to use Documents/MIF/atas_export")]
        public string OutputDirectory { get; set; } = string.Empty;

        [Display(Name = "Export DOM",
            GroupName = "Export",
            Description = "Include DOM snapshot data")]
        public bool ExportDom { get; set; } = true;

        [Display(Name = "Export Cluster",
            GroupName = "Export",
            Description = "Include cluster volume distribution")]
        public bool ExportCluster { get; set; } = true;

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar < 0)
            {
                return;
            }

            var candle = GetCandle(bar);
            if (candle is null)
            {
                return;
            }

            if (!_startLogged)
            {
                LogAlive($"indicator-start version={Version}");
                _startLogged = true;
            }

            var barData = GetOrCreateBarData(bar);

            if (ExportDom)
            {
                var dom = ExtractDomData(bar, candle);
                if (dom is not null)
                {
                    barData.DOM = dom;
                    barData.MasterTimestamp = dom.Timestamp;
                }
            }

            if (ExportCluster)
            {
                var cluster = ExtractClusterData(bar, candle);
                if (cluster is not null)
                {
                    if (barData.MasterTimestamp.HasValue)
                    {
                        if (cluster.Timestamp == barData.MasterTimestamp.Value)
                        {
                            barData.Cluster = cluster;
                        }
                        else
                        {
                            var domTimestamp = barData.MasterTimestamp.HasValue
                                ? barData.MasterTimestamp.Value.ToString("o")
                                : "null";
                            ReportWarning($"Cluster timestamp mismatch at bar {bar}: DOM={domTimestamp}, Cluster={cluster.Timestamp:o}");
                        }
                    }
                    else
                    {
                        barData.Cluster = cluster;
                        barData.MasterTimestamp = cluster.Timestamp;
                    }
                }
            }

            barData.OHLC = new OHLCData
            {
                Open = Convert.ToDecimal(candle.Open, CultureInfo.InvariantCulture),
                High = Convert.ToDecimal(candle.High, CultureInfo.InvariantCulture),
                Low = Convert.ToDecimal(candle.Low, CultureInfo.InvariantCulture),
                Close = Convert.ToDecimal(candle.Close, CultureInfo.InvariantCulture)
            };

            if (!barData.MasterTimestamp.HasValue)
            {
                barData.MasterTimestamp = EnsureUtc(candle.Time);
            }

            if (ShouldExportBar(bar, candle, out var gateReason))
            {
                if (barData.IsComplete() && _exportedBars.Add(bar))
                {
                    EnsureExporter();
                    try
                    {
                        _exporter?.ExportBar(barData);
                        LogAlive($"exported bar={bar} ts={barData.MasterTimestamp:O} dom={(barData.DOM != null ? 1 : 0)} cluster={(barData.Cluster != null ? 1 : 0)}");
                    }
                    catch (Exception ex)
                    {
                        LogAlive($"export-failed bar={bar} error={ex.Message}");
                        throw;
                    }
                    _lastGateReasons.Remove(bar);
                    _barCache.Remove(bar);
                }
                else if (!barData.IsComplete())
                {
                    LogAlive($"skip-incomplete bar={bar} reason={gateReason ?? "none"} hasDom={(barData.DOM != null ? 1 : 0)} hasCluster={(barData.Cluster != null ? 1 : 0)} hasOhlc={(barData.OHLC != null ? 1 : 0)} ts={(barData.MasterTimestamp.HasValue ? barData.MasterTimestamp.Value.ToString("o") : "null")}");
                }
            }
            else if (!string.IsNullOrEmpty(gateReason))
            {
                if (!_lastGateReasons.TryGetValue(bar, out var existing) || !string.Equals(existing, gateReason, StringComparison.Ordinal))
                {
                    _lastGateReasons[bar] = gateReason;
                    LogAlive($"gate-skip bar={bar} reason={gateReason}");
                }
            }
        }

        protected override void OnDispose()
        {
            try
            {
                EnsureExporter();
                foreach (var bar in _barCache.Values.OrderBy(b => b.BarIndex).ToList())
                {
                    if (bar.IsComplete())
                    {
                        try
                        {
                            _exporter?.ExportBar(bar);
                            LogAlive($"disposed-export bar={bar.BarIndex} ts={bar.MasterTimestamp:O}");
                        }
                        catch (Exception ex)
                        {
                            LogAlive($"dispose-export-failed bar={bar.BarIndex} error={ex.Message}");
                        }
                        _exportedBars.Add(bar.BarIndex);
                    }
                }
                _barCache.Clear();
                _exportedBars.Clear();
                _lastGateReasons.Clear();
            }
            finally
            {
                _exporter?.Dispose();
                _exporter = null;
                LogAlive("indicator-disposed");
                base.OnDispose();
            }
        }

        private BarData GetOrCreateBarData(int bar)
        {
            if (!_barCache.TryGetValue(bar, out var barData))
            {
                barData = new BarData
                {
                    BarIndex = bar,
                    Version = Version
                };
                _barCache[bar] = barData;
            }

            return barData;
        }

        private DOMData? ExtractDomData(int bar, IndicatorCandle candle)
        {
            var timestamp = EnsureUtc(candle.Time);
            var domData = new DOMData
            {
                Timestamp = timestamp,
                AskVolumes = new decimal[FixedLevels],
                BidVolumes = new decimal[FixedLevels],
                PriceLevels = new decimal[FixedLevels]
            };

            var levels = GetPriceLevels(candle)
                .Select(CreateSnapshot)
                .Where(snapshot => snapshot is not null)
                .Select(snapshot => snapshot!.Value)
                .ToList();

            if (levels.Count > 0)
            {
                levels.Sort(CompareSnapshotsByPrice);
            }
            else if (AtasBindings.TryGetLevels(this, bar, out var fallbackLevels) && fallbackLevels.Length > 0)
            {
                foreach (var level in fallbackLevels)
                {
                    levels.Add(new PriceLevelSnapshot
                    {
                        Ask = Convert.ToDecimal(level.Ask, CultureInfo.InvariantCulture),
                        Bid = Convert.ToDecimal(level.Bid, CultureInfo.InvariantCulture),
                        Price = level.Price.HasValue ? Convert.ToDecimal(level.Price.Value, CultureInfo.InvariantCulture) : null,
                        Volume = Convert.ToDecimal(level.Ask + level.Bid, CultureInfo.InvariantCulture),
                        Delta = Convert.ToDecimal(level.Ask - level.Bid, CultureInfo.InvariantCulture),
                        Trades = 0
                    });
                    if (levels.Count >= FixedLevels)
                    {
                        break;
                    }
                }

                if (levels.Count > 0)
                {
                    levels.Sort(CompareSnapshotsByPrice);
                    LogAlive($"dom-fallback bar={bar} levels={levels.Count}");
                }
            }

            if (levels.Count == 0)
            {
                LogAlive($"dom-missing bar={bar}");
            }

            for (var i = 0; i < FixedLevels; i++)
            {
                if (i < levels.Count)
                {
                    var level = levels[i];
                    domData.AskVolumes[i] = Math.Max(0m, EstimateAskFromProfile(level));
                    domData.BidVolumes[i] = Math.Max(0m, EstimateBidFromProfile(level));
                    domData.PriceLevels[i] = level.Price ?? 0m;
                }
                else
                {
                    domData.AskVolumes[i] = 0m;
                    domData.BidVolumes[i] = 0m;
                    domData.PriceLevels[i] = 0m;
                }
            }

            var bestAskPrice = levels
                .Where(l => l.Price.HasValue && l.Ask > 0m)
                .Select(l => l.Price!.Value)
                .DefaultIfEmpty(0m)
                .Min();

            var bestBidPrice = levels
                .Where(l => l.Price.HasValue && l.Bid > 0m)
                .Select(l => l.Price!.Value)
                .DefaultIfEmpty(0m)
                .Max();

            domData.BestAsk = bestAskPrice;
            domData.BestBid = bestBidPrice;
            domData.Spread = (bestAskPrice > 0m && bestBidPrice > 0m && bestAskPrice > bestBidPrice)
                ? bestAskPrice - bestBidPrice
                : 0m;
            domData.MidPrice = (bestAskPrice > 0m && bestBidPrice > 0m)
                ? (bestAskPrice + bestBidPrice) / 2m
                : 0m;

            return domData;
        }

        private ClusterData? ExtractClusterData(int bar, IndicatorCandle candle)
        {
            var timestamp = EnsureUtc(candle.Time);
            var clusterData = new ClusterData
            {
                Timestamp = timestamp,
                BuyVolumes = new decimal[FixedLevels],
                SellVolumes = new decimal[FixedLevels],
                TotalVolume = Convert.ToDecimal(candle.Volume, CultureInfo.InvariantCulture),
                Delta = Convert.ToDecimal(candle.Delta, CultureInfo.InvariantCulture)
            };

            var levels = GetPriceLevels(candle)
                .Select(CreateSnapshot)
                .Where(snapshot => snapshot is not null)
                .Select(snapshot => snapshot!.Value)
                .ToList();

            if (levels.Count > 0)
            {
                levels.Sort(CompareSnapshotsByPrice);
            }
            else if (AtasBindings.TryGetClusterLevels(this, bar, out var fallbackLevels) && fallbackLevels.Length > 0)
            {
                foreach (var level in fallbackLevels)
                {
                    levels.Add(new PriceLevelSnapshot
                    {
                        Ask = Convert.ToDecimal(level.Ask, CultureInfo.InvariantCulture),
                        Bid = Convert.ToDecimal(level.Bid, CultureInfo.InvariantCulture),
                        Price = level.Price.HasValue ? Convert.ToDecimal(level.Price.Value, CultureInfo.InvariantCulture) : null,
                        Volume = Convert.ToDecimal(level.Ask + level.Bid, CultureInfo.InvariantCulture),
                        Delta = Convert.ToDecimal(level.Ask - level.Bid, CultureInfo.InvariantCulture),
                        Trades = 0
                    });
                    if (levels.Count >= FixedLevels)
                    {
                        break;
                    }
                }

                if (levels.Count > 0)
                {
                    levels.Sort(CompareSnapshotsByPrice);
                    LogAlive($"cluster-fallback bar={bar} levels={levels.Count}");
                }
            }

            if (levels.Count == 0)
            {
                LogAlive($"cluster-missing bar={bar}");
            }

            decimal maxSingleTrade = 0m;
            int buyTrades = 0;
            int sellTrades = 0;
            int totalTrades = 0;

            for (var i = 0; i < FixedLevels; i++)
            {
                if (i < levels.Count)
                {
                    var level = levels[i];
                    var buyVolume = Math.Max(0m, level.Ask);
                    var sellVolume = Math.Max(0m, level.Bid);

                    clusterData.BuyVolumes[i] = buyVolume;
                    clusterData.SellVolumes[i] = sellVolume;

                    maxSingleTrade = Math.Max(maxSingleTrade, Math.Max(buyVolume, sellVolume));

                    totalTrades += level.Trades;

                    var combined = buyVolume + sellVolume;
                    if (combined > 0m && level.Trades > 0)
                    {
                        var ratio = buyVolume / combined;
                        buyTrades += (int)Math.Round(level.Trades * (double)ratio, MidpointRounding.AwayFromZero);
                        sellTrades += level.Trades - (int)Math.Round(level.Trades * (double)ratio, MidpointRounding.AwayFromZero);
                    }
                }
                else
                {
                    clusterData.BuyVolumes[i] = 0m;
                    clusterData.SellVolumes[i] = 0m;
                }
            }

            if (totalTrades == 0)
            {
                totalTrades = SafeConvertToInt(candle.Ticks);
            }

            if (buyTrades == 0 && sellTrades == 0 && totalTrades > 0)
            {
                buyTrades = totalTrades / 2;
                sellTrades = totalTrades - buyTrades;
            }

            clusterData.TradesCount = totalTrades;
            clusterData.BuyTrades = buyTrades;
            clusterData.SellTrades = sellTrades;
            clusterData.MaxSingleTrade = maxSingleTrade;

            return clusterData;
        }

        private bool ShouldExportBar(int bar, IndicatorCandle candle, out string? reason)
        {
            reason = null;
            if (_exportedBars.Contains(bar))
            {
                reason = "already-exported";
                return false;
            }

            if (bar < CurrentBar - 1)
            {
                reason = "historical";
                return true;
            }

            var closeTime = EnsureUtc(candle.Time);

            if (bar < CurrentBar)
            {
                reason = "closed";
                return true;
            }

            if (candle.Time.Kind == DateTimeKind.Unspecified)
            {
                reason = "unspecified-kind";
                return true;
            }

            var age = DateTime.UtcNow - closeTime;
            if (age > TimeSpan.FromSeconds(5))
            {
                reason = $"aged-{age.TotalSeconds:F0}s";
                return true;
            }

            reason = $"waiting-active ({age.TotalSeconds:F1}s)";
            return false;
        }

        private void EnsureExporter()
        {
            if (_exporter != null)
            {
                return;
            }

            _resolvedSymbol ??= ResolveSymbol();
            _resolvedTimeframe ??= ResolveTimeframe();

            var directory = GetOutputDirectory();
            var fileName = $"{SanitizeFileName(_resolvedSymbol ?? "instrument")}_{SanitizeFileName(_resolvedTimeframe ?? "1m")}_{Version}.jsonl";
            _exportFilePath = Path.Combine(directory, fileName);
            _exporter = new JSONLExporter(_exportFilePath, _resolvedTimeframe ?? "1m");
            LogAlive($"exporter-created path={_exportFilePath}");
        }

        private string GetOutputDirectory()
        {
            if (_resolvedOutputDirectory is not null)
            {
                return _resolvedOutputDirectory;
            }

            if (!string.IsNullOrWhiteSpace(OutputDirectory))
            {
                Directory.CreateDirectory(OutputDirectory);
                _resolvedOutputDirectory = OutputDirectory;
                return _resolvedOutputDirectory;
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var defaultPath = Path.Combine(documents, "MIF", "atas_export");
            Directory.CreateDirectory(defaultPath);
            _resolvedOutputDirectory = defaultPath;
            return _resolvedOutputDirectory;
        }

        private string ResolveSymbol()
        {
            var instrument = TryGetProperty(this, InstrumentPropertyNames);
            if (instrument is null)
            {
                foreach (var chartName in ChartInfoPropertyNames)
                {
                    var chart = TryGetProperty(this, chartName);
                    if (chart is not null)
                    {
                        instrument = TryGetProperty(chart, InstrumentPropertyNames);
                        if (instrument is not null)
                        {
                            break;
                        }
                    }
                }
            }

            if (instrument is not null)
            {
                foreach (var name in SymbolPropertyNames)
                {
                    var value = TryGetProperty(instrument, name);
                    if (value is string symbol && !string.IsNullOrWhiteSpace(symbol))
                    {
                        return symbol;
                    }
                }
            }

            return "instrument";
        }

        private string ResolveTimeframe()
        {
            foreach (var chartName in ChartInfoPropertyNames)
            {
                var chart = TryGetProperty(this, chartName);
                if (chart is null)
                {
                    continue;
                }

                foreach (var tfName in TimeFramePropertyNames)
                {
                    var tf = TryGetProperty(chart, tfName);
                    if (tf is null)
                    {
                        continue;
                    }

                    if (tf is string s && !string.IsNullOrWhiteSpace(s))
                    {
                        return s;
                    }

                    foreach (var textName in TimeFrameTextPropertyNames)
                    {
                        var text = TryGetProperty(tf, textName);
                        if (text is string str && !string.IsNullOrWhiteSpace(str))
                        {
                            return str;
                        }
                    }
                }
            }

            return "1m";
        }

        private static IEnumerable<object> GetPriceLevels(IndicatorCandle candle)
        {
            foreach (var methodName in PriceLevelMethodNames)
            {
                var method = candle.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method is null)
                {
                    continue;
                }

                object? result = method.GetParameters().Length == 0
                    ? method.Invoke(candle, Array.Empty<object>())
                    : method.Invoke(candle, new object?[] { true });

                if (result is IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is not null)
                        {
                            yield return item;
                        }
                    }
                    yield break;
                }
            }
        }

        private static PriceLevelSnapshot? CreateSnapshot(object level)
        {
            var ask = GetDecimal(level, AskPropertyNames);
            var bid = GetDecimal(level, BidPropertyNames);
            var price = GetNullableDecimal(level, PricePropertyNames);
            var volume = GetDecimal(level, VolumePropertyNames);
            if (volume == 0m)
            {
                volume = Math.Max(0m, ask + bid);
            }

            var delta = GetDecimal(level, DeltaPropertyNames, ask - bid);
            var trades = GetInt(level, TradesPropertyNames);

            return new PriceLevelSnapshot
            {
                Ask = ask,
                Bid = bid,
                Price = price,
                Volume = volume,
                Delta = delta,
                Trades = trades
            };
        }

        private static int CompareSnapshotsByPrice(PriceLevelSnapshot left, PriceLevelSnapshot right)
        {
            if (left.Price.HasValue && right.Price.HasValue)
            {
                return left.Price.Value.CompareTo(right.Price.Value);
            }

            if (left.Price.HasValue)
            {
                return -1;
            }

            if (right.Price.HasValue)
            {
                return 1;
            }

            return right.Volume.CompareTo(left.Volume);
        }

        private static decimal EstimateAskFromProfile(PriceLevelSnapshot snapshot)
        {
            if (snapshot.Volume <= 0m)
            {
                return Math.Max(0m, snapshot.Ask);
            }

            var estimated = (snapshot.Volume + snapshot.Delta) / 2m;
            if (estimated < 0m)
            {
                estimated = 0m;
            }

            return estimated;
        }

        private static decimal EstimateBidFromProfile(PriceLevelSnapshot snapshot)
        {
            if (snapshot.Volume <= 0m)
            {
                return Math.Max(0m, snapshot.Bid);
            }

            var estimated = (snapshot.Volume - snapshot.Delta) / 2m;
            if (estimated < 0m)
            {
                estimated = 0m;
            }

            return estimated;
        }

        private static DateTime EnsureUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                _ => value.ToUniversalTime()
            };
        }

        private static decimal GetDecimal(object source, string[] propertyNames, decimal fallback = 0m)
        {
            foreach (var name in propertyNames)
            {
                var property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property is null)
                {
                    continue;
                }

                var value = property.GetValue(source);
                if (value is null)
                {
                    continue;
                }

                try
                {
                    return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                }
                catch
                {
                    // ignore
                }
            }

            return fallback;
        }

        private static decimal? GetNullableDecimal(object source, string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property is null)
                {
                    continue;
                }

                var value = property.GetValue(source);
                if (value is null)
                {
                    continue;
                }

                try
                {
                    return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                }
                catch
                {
                    // ignore
                }
            }

            return null;
        }

        private static int GetInt(object source, string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property is null)
                {
                    continue;
                }

                var value = property.GetValue(source);
                if (value is null)
                {
                    continue;
                }

                try
                {
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
                catch
                {
                    // ignore
                }
            }

            return 0;
        }

        private static object? TryGetProperty(object source, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property is null)
                {
                    continue;
                }

                var value = property.GetValue(source);
                if (value is not null)
                {
                    return value;
                }
            }

            return null;
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join(string.Empty, value.Where(c => !invalidChars.Contains(c))).ToLowerInvariant();
        }

        private static int SafeConvertToInt(object? value)
        {
            if (value is null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private readonly record struct PriceLevelSnapshot
        {
            public decimal Ask { get; init; }
            public decimal Bid { get; init; }
            public decimal? Price { get; init; }
            public decimal Volume { get; init; }
            public decimal Delta { get; init; }
            public int Trades { get; init; }
        }

        private void ReportWarning(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                var method = GetType().GetMethod("LogWarning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method is not null)
                {
                    method.Invoke(this, new object?[] { message });
                    LogAlive(message);
                    return;
                }
            }
            catch
            {
                // ignore reflection errors
            }

            Debug.WriteLine(message);
            LogAlive(message);
        }

        private void EnsureAliveFile()
        {
            if (_alivePath is not null)
            {
                return;
            }

            var directory = GetOutputDirectory();
            Directory.CreateDirectory(directory);
            _alivePath = Path.Combine(directory, "_alive.log");
            if (!File.Exists(_alivePath))
            {
                try
                {
                    using var _ = File.AppendText(_alivePath);
                }
                catch
                {
                    _alivePath = null;
                }
            }
        }

        private void LogAlive(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                EnsureAliveFile();
                if (_alivePath is null)
                {
                    return;
                }

                var line = $"{DateTime.UtcNow:o} {message}{Environment.NewLine}";
                File.AppendAllText(_alivePath, line);
            }
            catch
            {
                // ignore logging failures
            }
        }
    }
}
