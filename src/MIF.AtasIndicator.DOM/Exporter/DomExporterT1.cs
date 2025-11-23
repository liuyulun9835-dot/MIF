using ATAS.Indicators;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using MIF.Shared.IO;
using MIF.Shared.Logging;

namespace MIF.AtasIndicator.DOM.Exporter
{
    /// <summary>
    /// Phase 1 DOM-only exporter (Test line T1).
    /// </summary>
    [DisplayName("MIF DomExporter T1")]
    public class DomExporterT1 : Indicator
    {
        private const int DomLevels = 20;
        private const string VersionTag = "t1";
        private const string EstimationMethod = "dom_proxy_v14";
        private const string FilePrefix = "dom_t1";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        private readonly List<string> _recordBuffer = new();
        private readonly HashSet<int> _processedBars = new();

        private string? _baseDirectory;
        private string? _alivePath;
        private string _outputDirectory = string.Empty;
        private int _bufferSize = 256;
        private bool _initialized;
        private DateTime _sessionStartUtc = DateTime.UtcNow;

        private int _totalBars;
        private int _exportedBars;
        private int _skippedBars;
        private int _domSuccess;
        private int _domFailure;
        private int _backtrackRecoveries;

        [Display(Name = "Output Directory", GroupName = "Export", Description = "Destination folder for JSONL output")]
        public string OutputDirectory
        {
            get => _outputDirectory;
            set
            {
                _outputDirectory = value ?? string.Empty;
                _initialized = false;
            }
        }

        [Display(Name = "Buffer Size", GroupName = "Performance", Description = "Number of records to buffer before writing")]
        public int BufferSize
        {
            get => _bufferSize;
            set => _bufferSize = Math.Max(1, Math.Min(2000, value));
        }

        public DomExporterT1()
        {
            Name = "MIF DomExporter T1";
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar < 0)
            {
                return;
            }

            EnsureInitialized();

            _totalBars++;

            try
            {
                ProcessBar(bar);
            }
            catch (Exception ex)
            {
                FileLogger.LogError(_alivePath, $"ProcessBar failed at bar {bar}", ex);
            }
        }

        private void ProcessBar(int bar)
        {
            if (_processedBars.Contains(bar))
            {
                return;
            }

            var candle = GetCandle(bar);
            if (candle == null)
            {
                _skippedBars++;
                return;
            }

            var closeTimeUtc = EnsureUtc(candle.Time);

            // ATAS mapping: realized_buy = candle.GetPriceVolumeInfo().Ask (aggressive buy)
            //                realized_sell = candle.GetPriceVolumeInfo().Bid (aggressive sell)
            var priceVolume = candle.GetPriceVolumeInfo();
            double realizedBuy = ExtractDouble(priceVolume, "Ask");
            double realizedSell = ExtractDouble(priceVolume, "Bid");

            var domSnapshot = CaptureDomSnapshot(bar);
            if (!domSnapshot.IsValid)
            {
                _domFailure++;
                _skippedBars++;
                return;
            }

            _domSuccess++;

            double askSum = domSnapshot.AskVolumes.Sum();
            double bidSum = domSnapshot.BidVolumes.Sum();

            double rhoBuy = askSum > 0 ? realizedBuy / askSum : 0.0;
            double rhoSell = bidSum > 0 ? realizedSell / bidSum : 0.0;

            var record = new
            {
                metadata = new
                {
                    version = VersionTag,
                    estimation_method = EstimationMethod,
                    bar_index = bar,
                    ts_utc = closeTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                    exporter = Name,
                    mode = "dom_only"
                },
                dom_levels = new
                {
                    // ATAS mapping: ask_volumes = MarketDepthSnapshot.Asks (passive sell liquidity)
                    ask_volumes = domSnapshot.AskVolumes,
                    // ATAS mapping: bid_volumes = MarketDepthSnapshot.Bids (passive buy liquidity)
                    bid_volumes = domSnapshot.BidVolumes
                },
                flows = new
                {
                    realized_buy = realizedBuy,
                    realized_sell = realizedSell
                },
                rho = new
                {
                    rho_buy = rhoBuy,
                    rho_sell = rhoSell
                }
            };

            var json = JsonSerializer.Serialize(record, JsonOptions);
            _recordBuffer.Add(json);
            _processedBars.Add(bar);
            _exportedBars++;

            if (_recordBuffer.Count >= _bufferSize)
            {
                FlushBuffer(closeTimeUtc);
            }
        }

        private DomSnapshot CaptureDomSnapshot(int bar)
        {
            try
            {
                var depthInfo = MarketDepthInfo;
                if (depthInfo == null)
                {
                    return DomSnapshot.Invalid();
                }

                var snapshot = depthInfo.GetMarketDepthSnapshot();
                if (snapshot == null)
                {
                    return DomSnapshot.Invalid();
                }

                if (snapshot is not IEnumerable levels)
                {
                    return DomSnapshot.Invalid();
                }

                var askLevels = new List<(decimal price, decimal volume)>();
                var bidLevels = new List<(decimal price, decimal volume)>();

                foreach (var level in levels)
                {
                    if (level == null)
                    {
                        continue;
                    }

                    var price = GetNullableDecimal(level, "Price");
                    var volume = GetNullableDecimal(level, "Volume");
                    var isAsk = GetNullableBool(level, "IsAsk");
                    var isBid = GetNullableBool(level, "IsBid");

                    if (!price.HasValue || !volume.HasValue)
                    {
                        continue;
                    }

                    if (isAsk == true)
                    {
                        askLevels.Add((price.Value, volume.Value));
                    }
                    else if (isBid == true)
                    {
                        bidLevels.Add((price.Value, volume.Value));
                    }
                }

                if (askLevels.Count == 0 || bidLevels.Count == 0)
                {
                    return DomSnapshot.Invalid();
                }

                var sortedAsks = askLevels.OrderBy(x => x.price).Take(DomLevels).ToList();
                var sortedBids = bidLevels.OrderByDescending(x => x.price).Take(DomLevels).ToList();

                var askVolumes = new double[DomLevels];
                var bidVolumes = new double[DomLevels];

                for (int i = 0; i < sortedAsks.Count; i++)
                {
                    askVolumes[i] = (double)sortedAsks[i].volume;
                }

                for (int i = 0; i < sortedBids.Count; i++)
                {
                    bidVolumes[i] = (double)sortedBids[i].volume;
                }

                return new DomSnapshot(true, askVolumes, bidVolumes, Math.Min(sortedAsks.Count, sortedBids.Count));
            }
            catch (Exception ex)
            {
                FileLogger.LogError(_alivePath, $"DOM snapshot failed at bar {bar}", ex);
                return DomSnapshot.Invalid();
            }
        }

        protected override void OnDispose()
        {
            try
            {
                if (_alivePath != null)
                {
                    FileLogger.AppendBlock(_alivePath, $"[T1-BACKTRACK] Start scan 0..{CurrentBar - 1}\n");
                }

                for (int bar = 0; bar < CurrentBar; bar++)
                {
                    if (_processedBars.Contains(bar))
                    {
                        continue;
                    }

                    try
                    {
                        ProcessBar(bar);
                        _backtrackRecoveries++;
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogError(_alivePath, $"Backtrack failure at bar {bar}", ex);
                    }
                }

                if (_alivePath != null)
                {
                    FileLogger.AppendBlock(_alivePath, $"[T1-BACKTRACK] Completed. Recovered {_backtrackRecoveries} bars.\n");
                }
            }
            finally
            {
                if (_recordBuffer.Count > 0)
                {
                    FlushBuffer(DateTime.UtcNow);
                }

                if (_alivePath != null)
                {
                    var uptime = DateTime.UtcNow - _sessionStartUtc;
                    FileLogger.AppendBlock(_alivePath,
                        $"[T1-END] {DateTime.UtcNow:o}\n" +
                        $"  Uptime: {uptime.TotalMinutes:F1} minutes\n" +
                        $"  Bars processed: {_totalBars}\n" +
                        $"  Exported: {_exportedBars}\n" +
                        $"  Skipped: {_skippedBars}\n" +
                        $"  DOM success: {_domSuccess}\n" +
                        $"  DOM failure: {_domFailure}\n" +
                        $"  Backtracked: {_backtrackRecoveries}\n\n");
                }

                base.OnDispose();
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                var defaultDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "MIF",
                    "dom_t1");

                _baseDirectory = string.IsNullOrWhiteSpace(_outputDirectory) ? defaultDir : _outputDirectory;
                Directory.CreateDirectory(_baseDirectory);

                _alivePath = Path.Combine(_baseDirectory, "_dom_t1.log");
                _sessionStartUtc = DateTime.UtcNow;

                FileLogger.AppendBlock(_alivePath,
                    $"[T1-START] {DateTime.UtcNow:o}\n" +
                    $"  DOM mode: DOM-only (no cluster fallback)\n" +
                    $"  Fixed depth: {DomLevels}\n" +
                    $"  Buffer size: {_bufferSize}\n" +
                    $"  Output: {_baseDirectory}\n\n");

                _initialized = true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError(_alivePath, "Initialization failed", ex);
                throw;
            }
        }

        private void FlushBuffer(DateTime anchorTimeUtc)
        {
            if (_baseDirectory == null || _recordBuffer.Count == 0)
            {
                return;
            }

            var utcTime = EnsureUtc(anchorTimeUtc);
            JsonlWriter.AppendRecords(_baseDirectory, FilePrefix, utcTime, _recordBuffer);
            _recordBuffer.Clear();
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

        private static double ExtractDouble(object? obj, string property)
        {
            if (obj == null)
            {
                return 0.0;
            }

            var prop = obj.GetType().GetProperty(property);
            if (prop == null)
            {
                return 0.0;
            }

            var value = prop.GetValue(obj);
            if (value == null)
            {
                return 0.0;
            }

            return Convert.ToDouble(value);
        }

        private static decimal? GetNullableDecimal(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName);
            if (prop == null)
            {
                return null;
            }

            var value = prop.GetValue(obj);
            if (value == null)
            {
                return null;
            }

            return value switch
            {
                decimal dec => dec,
                double dbl => (decimal)dbl,
                float flt => (decimal)flt,
                int i => i,
                long l => l,
                _ => Convert.ToDecimal(value)
            };
        }

        private static bool? GetNullableBool(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName);
            if (prop == null)
            {
                return null;
            }

            var value = prop.GetValue(obj);
            if (value == null)
            {
                return null;
            }

            if (value is bool flag)
            {
                return flag;
            }

            return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
        }

        private sealed class DomSnapshot
        {
            public DomSnapshot(bool isValid, double[] askVolumes, double[] bidVolumes, int extractedLevels)
            {
                IsValid = isValid;
                AskVolumes = askVolumes;
                BidVolumes = bidVolumes;
                ExtractedLevels = extractedLevels;
            }

            public bool IsValid { get; }
            public double[] AskVolumes { get; }
            public double[] BidVolumes { get; }
            public int ExtractedLevels { get; }

            public static DomSnapshot Invalid()
            {
                return new DomSnapshot(false, new double[DomLevels], new double[DomLevels], 0);
            }
        }
    }
}
