using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MIF.AtasExporter
{
    // ====== 选项 / 组合策略 ======
    public enum CombineMode { Max, Mean, Geomean }

    public sealed class ExporterOptions
    {
        public string Symbol { get; set; } = "BTCUSDT";
        public string Timeframe { get; set; } = "1m";
        public int Levels { get; set; } = 120;
        public CombineMode CombineMode { get; set; } = CombineMode.Max;
        public double ConsEps { get; set; } = 1e-2;
        public string OutputPath { get; set; } = "out\\bars.jsonl";
    }

    // ====== 定义层数据结构（只含 ε、索引、无 price 参与） ======
    public sealed record EnergyCluster(
        int BestAskIdx, int BestBidIdx,
        double[] AskPerLevel, double[] BidPerLevel, int NumLevels);

    public sealed record TradeFlow(double RealizedBuy, double RealizedSell);

    public sealed record UrgencyOut(double UBuy, double USell, double Ratio, int? Class = null);

    public sealed record Liquidity(double BookImbalance, int Depth);

    // ====== 验证层（允许出现 price，用于一致性对照，不得回流定义） ======
    public sealed record ValidationView(double? BestAsk, double? BestBid, double? MidPrice, double[]? PriceLevels);

    public sealed record Header(string Symbol, string Timeframe, DateTime TOpen, DateTime TClose, string Version, string Exporter, string WindowConvention);

    public sealed record Signatures(bool EnergyConservationOk, bool LevelsSortedOk, string MifCompliance);

    public sealed record BarRecord(
        Header Header,
        EnergyCluster ClusterStats,
        TradeFlow Trades,
        UrgencyOut? UrgencyMetrics,
        Liquidity? LiquidityMetrics,
        ValidationView? ValidationMetrics,
        Signatures Signatures
    );

    // ====== 计算口径（全部在 ε 域） ======
    public static class UrgencyCalculator
    {
        public static UrgencyOut Compute(EnergyCluster c, TradeFlow t, CombineMode mode)
        {
            double potBuy  = Math.Max(c.AskPerLevel[Math.Clamp(c.BestAskIdx, 0, c.NumLevels-1)], 1e-12);
            double potSell = Math.Max(c.BidPerLevel[Math.Clamp(c.BestBidIdx, 0, c.NumLevels-1)], 1e-12);

            double uBuy  = t.RealizedBuy  / potBuy;
            double uSell = t.RealizedSell / potSell;

            double r = mode switch
            {
                CombineMode.Max     => Math.Max(uBuy, uSell),
                CombineMode.Mean    => 0.5 * (uBuy + uSell),
                CombineMode.Geomean => Math.Sqrt(uBuy * uSell),
                _ => Math.Max(uBuy, uSell)
            };
            r = Math.Clamp(r, 0.0, 1.0); // 无量纲，裁剪到 [0,1]
            return new UrgencyOut(uBuy, uSell, r, null); // class 留空，待你接入分位模板
        }
    }

    public static class LiquidityCalculator
    {
        public static Liquidity Compute(EnergyCluster c)
        {
            double sAsk = c.AskPerLevel.Sum();
            double sBid = c.BidPerLevel.Sum();
            double imb = (sBid + sAsk) > 0 ? (sBid - sAsk) / (sBid + sAsk) : 0.0;
            return new Liquidity(imb, c.NumLevels);
        }
    }

    public static class Validator
    {
        public static (bool ok, double dAsk, double dBid) CheckConservation(EnergyCluster c, TradeFlow t, double eps)
        {
            double dA = Math.Abs(c.AskPerLevel.Sum() - t.RealizedBuy);
            double dB = Math.Abs(c.BidPerLevel.Sum() - t.RealizedSell);
            return (dA <= eps && dB <= eps, dA, dB);
        }

        public static bool LevelsSorted(EnergyCluster c)
        {
            // 这里仅检查长度一致；如需严格递增/递减排序，可在 ValidationView 携带 priceLevels 后做一致性检测
            return c.AskPerLevel.Length == c.NumLevels && c.BidPerLevel.Length == c.NumLevels;
        }
    }

    // ====== 写出（JSONL；一行一条 bar） ======
    public interface IRecordWriter { Task WriteAsync(BarRecord rec, CancellationToken ct = default); }

    public sealed class JsonlWriter : IRecordWriter
    {
        private readonly string _path;
        private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.General)
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public JsonlWriter(string path) { _path = path; }

        public Task WriteAsync(BarRecord rec, CancellationToken ct = default)
        {
            var line = JsonSerializer.Serialize(rec, _opts) + Environment.NewLine;
            File.AppendAllText(_path, line);
            return Task.CompletedTask;
        }
    }

    // ====== 示例数据源（替换为 ATAS 实现即可） ======
    public interface IBarSource
    {
        (EnergyCluster cluster, TradeFlow trades, ValidationView? validation, DateTime tOpen, DateTime tClose) NextBar();
    }

    public sealed class SampleAtasSource : IBarSource
    {
        private readonly int _levels;
        private readonly Random _rng = new(1234);
        private DateTime _cursor = DateTime.UtcNow;

        public SampleAtasSource(int levels) { _levels = levels; }

        public (EnergyCluster, TradeFlow, ValidationView?, DateTime, DateTime) NextBar()
        {
            int mid = _levels / 2;
            int bestAskIdx = mid;
            int bestBidIdx = mid - 1;

            var ask = new double[_levels];
            var bid = new double[_levels];

            // 构造一个在 TOB 集中的 ε 场（背景独立）
            for (int i = 0; i < _levels; i++)
            {
                double da = Math.Max(0, 1.0 - Math.Abs(i - bestAskIdx) / (double)mid);
                double db = Math.Max(0, 1.0 - Math.Abs(i - bestBidIdx) / (double)mid);
                ask[i] = 5.0 * da * (_rng.NextDouble() * 0.2 + 0.9); // 稍有扰动
                bid[i] = 5.0 * db * (_rng.NextDouble() * 0.2 + 0.9);
            }

            // realized 作为 ε 的函数（不读 price）
            double realizedBuy  = Math.Max(0.1, ask[bestAskIdx] * (_rng.NextDouble() * 0.6 + 0.2));   // < potBuy
            double realizedSell = Math.Max(0.1, bid[bestBidIdx] * (_rng.NextDouble() * 0.6 + 0.2));   // < potSell

            // 为了演示，有意不强制严格守恒（签名会给出 full/partial）
            var cluster = new EnergyCluster(bestAskIdx, bestBidIdx, ask, bid, _levels);
            var trades  = new TradeFlow(realizedBuy, realizedSell);

            // 验证视图：仅用于一致性对照（允许 price 出现，但不参与任何定义）
            var priceLevels = Enumerable.Range(0, _levels).Select(i => 70000.0 + (i - mid) * 0.5).ToArray();
            var validation  = new ValidationView(
                BestAsk: priceLevels[bestAskIdx],
                BestBid: priceLevels[bestBidIdx],
                MidPrice: 0.5 * (priceLevels[bestAskIdx] + priceLevels[bestBidIdx]),
                PriceLevels: priceLevels
            );

            var tOpen = _cursor;
            var tClose = _cursor = _cursor.AddMinutes(1);
            return (cluster, trades, validation, tOpen, tClose);
        }
    }

    // ====== 导出主流程（定义层计算 → 守恒/排序检查 → 写出） ======
    public sealed class ExporterService
    {
        private readonly IBarSource _src;
        private readonly IRecordWriter _writer;
        public ExporterService(IBarSource src, IRecordWriter writer) { _src = src; _writer = writer; }

        public async Task RunAsync(ExporterOptions opt, int bars = 10, CancellationToken ct = default)
        {
            for (int i = 0; i < bars; i++)
            {
                var (cluster, trades, validation, tOpen, tClose) = _src.NextBar();

                var urg = UrgencyCalculator.Compute(cluster, trades, opt.CombineMode);
                var liq = LiquidityCalculator.Compute(cluster);

                var (ok, dA, dB) = Validator.CheckConservation(cluster, trades, opt.ConsEps);
                var sig = new Signatures(ok, Validator.LevelsSorted(cluster), ok ? "full" : "partial");

                var header = new Header(opt.Symbol, opt.Timeframe, tOpen, tClose, "mif.v1.1", "MIF.AtasExporter", "UTC-right-closed");

                var rec = new BarRecord(header, cluster, trades, urg, liq, validation, sig);
                await _writer.WriteAsync(rec, ct);
            }
        }
    }
}
