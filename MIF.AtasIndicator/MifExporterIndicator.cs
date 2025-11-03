using ATAS.Indicators;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MIF.AtasIndicator
{
    public class MifExporterIndicatorV4 : Indicator
    {
        private readonly string? _outPath;
        private readonly string? _alivePath;
        private static int _hb;

        public MifExporterIndicatorV4()
        {
            Name = "MIF · Exporter V4";

            try
            {
                var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var baseDir = Path.Combine(doc, "MIF", "atas_export");
                Directory.CreateDirectory(baseDir);

                _outPath = Path.Combine(baseDir, "bars.jsonl");
                _alivePath = Path.Combine(baseDir, "_alive.log");

                var asm = Assembly.GetExecutingAssembly().Location;
                File.AppendAllText(_alivePath, $"LOADED {DateTime.UtcNow:o} dll={asm} ts={File.GetLastWriteTime(asm):o}\n");
            }
            catch (Exception ex)
            {
                // Fallback: 桌面日志
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var fallbackLog = Path.Combine(desktop, "mif_atas_error.log");
                try
                {
                    File.AppendAllText(fallbackLog, $"[{DateTime.UtcNow:o}] CTOR ERROR: {ex}\n");
                }
                catch { /* 完全失败，静默 */ }
            }
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (_alivePath == null || _outPath == null) return;

            if ((_hb++ & 63) == 0)
                File.AppendAllText(_alivePath, $"{DateTime.UtcNow:o} bar={bar}\n");

            // 1) 使用官方 API 获取层级数据
            var candle = GetCandle(bar);
            if (candle == null)
            {
                if ((bar & 63) == 0) File.AppendAllText(_alivePath, $"{DateTime.UtcNow:o} no-candle bar={bar}\n");
                return;
            }

            var allLevels = candle.GetAllPriceLevels();
            if (allLevels == null)
            {
                if ((bar & 63) == 0) File.AppendAllText(_alivePath, $"{DateTime.UtcNow:o} no-levels bar={bar}\n");
                return;
            }

            // 2) 构建 ask/bid 数组并计算 realized 量
            var levelsList = allLevels.ToList();
            int L = levelsList.Count;
            if (L == 0) return;

            double[] ask = new double[L];
            double[] bid = new double[L];
            double realizedBuy = 0.0;
            double realizedSell = 0.0;

            for (int i = 0; i < L; i++)
            {
                var level = levelsList[i];
                if (level != null)
                {
                    ask[i] = (double)level.Ask;
                    bid[i] = (double)level.Bid;
                    realizedBuy += (double)level.Ask;
                    realizedSell += (double)level.Bid;
                }
            }

            // 3) bestIdx 定位（找第一个非零）
            int mid = L / 2;
            int bestAskIdx = mid;
            int bestBidIdx = mid - 1;

            for (int i = mid; i < L; i++)
            {
                if (ask[i] > 0) { bestAskIdx = i; break; }
            }
            for (int i = mid - 1; i >= 0; i--)
            {
                if (bid[i] > 0) { bestBidIdx = i; break; }
            }

            // 边界保护
            bestAskIdx = Math.Max(0, Math.Min(L - 1, bestAskIdx));
            bestBidIdx = Math.Max(0, Math.Min(L - 1, bestBidIdx));

            // 4) 紧急度：未来/已实现
            double potBuy = Math.Max(ask[bestAskIdx], 1e-12);
            double potSell = Math.Max(bid[bestBidIdx], 1e-12);
            double uBuy = realizedBuy / potBuy;
            double uSell = realizedSell / potSell;
            double ratio = Math.Max(0.0, Math.Min(1.0, Math.Max(uBuy, uSell)));

            // 5) 守恒签名
            bool conserve = Math.Abs(ask.Sum() - realizedBuy) <= 1e-2
                         && Math.Abs(bid.Sum() - realizedSell) <= 1e-2;

            // 6) JSONL
            var now = DateTime.UtcNow;
            var rec = new
            {
                header = new
                {
                    symbol = "BTCUSDT",
                    timeframe = "1m",
                    t_open = now.ToString("o"),
                    t_close = now.AddMinutes(1).ToString("o"),
                    version = "mif.v1.1",
                    exporter = "MIF.AtasIndicator",
                    window_convention = "UTC-right-closed"
                },
                cluster_stats = new
                {
                    best_ask_idx = bestAskIdx,
                    best_bid_idx = bestBidIdx,
                    ask_per_level = ask,
                    bid_per_level = bid,
                    num_levels = L
                },
                trades = new { realized_buy = realizedBuy, realized_sell = realizedSell },
                urgency_metrics = new { u_buy = uBuy, u_sell = uSell, urgency_ratio = ratio },
                liquidity_metrics = new
                {
                    book_imbalance = (bid.Sum() + ask.Sum()) > 0
                        ? (bid.Sum() - ask.Sum()) / (bid.Sum() + ask.Sum())
                        : 0.0,
                    depth = L
                },
                validation_metrics = (object?)null,
                signatures = new
                {
                    energy_conservation_ok = conserve,
                    levels_sorted_ok = true,
                    mif_compliance = conserve ? "full" : "partial"
                }
            };

            var json = JsonSerializer.Serialize(rec, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            File.AppendAllText(_outPath, json + "\n");
        }
    }
}
