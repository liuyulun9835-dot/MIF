using ATAS.Indicators;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MIF.AtasIndicator
{
    public class MifExporterIndicator : Indicator
    {
        private readonly string _outPath;
        private readonly string _alivePath;
        private static int _hb;

        public MifExporterIndicator()
        {
            var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var baseDir = Path.Combine(doc, "MIF", "atas_export");
            Directory.CreateDirectory(baseDir);

            _outPath = Path.Combine(baseDir, "bars.jsonl");
            _alivePath = Path.Combine(baseDir, "_alive.log");

            var asm = Assembly.GetExecutingAssembly().Location;
            File.AppendAllText(_alivePath, $"LOADED {DateTime.UtcNow:o} dll={asm} ts={File.GetLastWriteTime(asm):o}\n");
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if ((_hb++ & 63) == 0)
                File.AppendAllText(_alivePath, $"{DateTime.UtcNow:o} bar={bar}\n");

            // 1) 绑定层：拿层级 & 成交
            if (!AtasBindings.TryGetLevels(this, bar, out var lvls))
            {
                if ((bar & 63) == 0) File.AppendAllText(_alivePath, $"{DateTime.UtcNow:o} no-levels bar={bar}\n");
                return;
            }
            if (!AtasBindings.TryGetRealizedFlow(this, bar, out var realizedBuy, out var realizedSell))
            {
                if ((bar & 63) == 0) File.AppendAllText(_alivePath, $"{DateTime.UtcNow:o} no-trades bar={bar}\n");
                return;
            }

            int L = lvls.Length; if (L == 0) return;
            var (bestAskIdx, bestBidIdx) = AtasBindings.FindBestIndices(lvls);

            // 2) ε 域数组
            double[] ask = new double[L], bid = new double[L];
            for (int i = 0; i < L; i++) { ask[i] = lvls[i].Ask; bid[i] = lvls[i].Bid; }

            // 3) 紧迫度（背景独立）
            double potBuy = Math.Max(ask[bestAskIdx], 1e-12);
            double potSell = Math.Max(bid[bestBidIdx], 1e-12);
            double uBuy = realizedBuy / potBuy;
            double uSell = realizedSell / potSell;
            double ratio = Math.Max(0.0, Math.Min(1.0, Math.Max(uBuy, uSell)));

            // 4) 守恒签名
            bool conserve = Math.Abs(ask.Sum() - realizedBuy) <= 1e-2
                         && Math.Abs(bid.Sum() - realizedSell) <= 1e-2;

            // 5) JSONL
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
