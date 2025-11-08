using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using ATAS.Indicators;
using ATAS.Indicators.Helpers;
using ATAS.Indicators.Technical;

namespace MIF.AtasIndicator
{
    // ε 域只需要 Ask/Bid；Price 仅用于验证层（可为 null）
    public sealed class Level
    {
        public double Ask;
        public double Bid;
        public double? Price;
    }

    public static class AtasBindings
    {
        // —— 可选名表（不同版本的拼写做兼容）——
        static readonly string[] CandleMethodNames = { "GetCandle" };
        static readonly string[] CandlePropNames = { "Candle", "CurrentCandle" };

        static readonly string[] LevelsMethodNames = { "GetAllPriceLevels" };
        static readonly string[] LevelsPropNames = { "PriceLevels", "Levels", "AllPriceLevels" };

        static readonly string[] AskNames = { "Ask", "AskVolume", "AskVol", "AggressorBuy", "Buy" };
        static readonly string[] BidNames = { "Bid", "BidVolume", "BidVol", "AggressorSell", "Sell" };
        static readonly string[] PriceNames = { "Price", "PriceDouble", "P" };

        // 读取“当前 bar”的层级数组
        public static bool TryGetLevels(Indicator ctx, int bar, out Level[] levels)
        {
            levels = Array.Empty<Level>();

            // 1) 拿 Candle 对象
            object? candle = InvokeFirst(ctx, CandleMethodNames, bar);
            if (candle is null)
                candle = GetFirstProperty(ctx, CandlePropNames);

            if (candle is null)
                return false;

            // 2) 从 Candle 取层级集合（IEnumerable）
            object? levelsObj = InvokeFirst(candle, LevelsMethodNames)
                                ?? GetFirstProperty(candle, LevelsPropNames);
            if (levelsObj is null) return false;

            var list = new System.Collections.Generic.List<Level>();
            foreach (var pl in (IEnumerable)levelsObj)
            {
                if (pl is null) continue;
                double? ask = GetDouble(pl, AskNames);
                double? bid = GetDouble(pl, BidNames);
                double? price = GetDouble(pl, PriceNames, allowNull: true);

                if (ask is null && bid is null) continue;
                list.Add(new Level { Ask = ask ?? 0.0, Bid = bid ?? 0.0, Price = price });
            }

            levels = list.ToArray();
            return levels.Length > 0;
        }

        // 先用簇聚合当作“本根 bar 的已实现成交”
        public static bool TryGetRealizedFlow(Indicator ctx, int bar,
            out double realizedBuy, out double realizedSell)
        {
            realizedBuy = realizedSell = 0.0;
            if (!TryGetLevels(ctx, bar, out var lvls)) return false;

            realizedBuy = lvls.Sum(x => x.Ask);
            realizedSell = lvls.Sum(x => x.Bid);
            return true;
        }

        // 选取离中最近且非空的 top-of-book 索引（不使用价格）
        public static (int bestAskIdx, int bestBidIdx) FindBestIndices(Level[] lvls)
        {
            int n = lvls.Length; if (n == 0) return (0, 0);
            int mid = n / 2;

            int bestAsk = mid;
            for (int i = mid; i < n; i++) if (lvls[i].Ask > 0) { bestAsk = i; break; }
            int bestBid = mid - 1;
            for (int i = mid - 1; i >= 0; i--) if (lvls[i].Bid > 0) { bestBid = i; break; }

            bestAsk = Math.Max(0, Math.Min(n - 1, bestAsk));
            bestBid = Math.Max(0, Math.Min(n - 1, bestBid));
            return (bestAsk, bestBid);
        }

        // ===== 反射小工具 =====
        static object? InvokeFirst(object target, string[] methodNames, params object[] args)
        {
            var t = target.GetType();
            foreach (var name in methodNames)
            {
                // 使用 GetMethods 避免 AmbiguousMatchException
                var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                               .Where(m => m.Name == name)
                               .ToArray();

                if (methods.Length == 0) continue;

                // 查找匹配参数数量和类型的方法
                foreach (var m in methods)
                {
                    var pars = m.GetParameters();
                    if (pars.Length == args.Length && (pars.Length == 0 || pars[0].ParameterType == typeof(int)))
                        return m.Invoke(target, args);
                }
            }
            return null;
        }

        static object? GetFirstProperty(object target, string[] propNames)
        {
            var t = target.GetType();
            foreach (var name in propNames)
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p is null) continue;
                return p.GetValue(target);
            }
            return null;
        }

        static double? GetDouble(object target, string[] names, bool allowNull = false)
        {
            var t = target.GetType();
            foreach (var name in names)
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p is null) continue;
                var v = p.GetValue(target);
                if (v is null) return allowNull ? null : 0.0;
                try { return Convert.ToDouble(v); } catch { /* 继续试下一个候选 */ }
            }
            return allowNull ? null : 0.0;
        }

        // === V18: 统一通过接口拿 price levels，规避重载/缓存歧义 ===
        public static bool TryGetClusterLevels(object indicator, int bar, out PriceLevelDTO[] levels)
        {
            levels = Array.Empty<PriceLevelDTO>();
            try
            {
                var core = indicator as Indicator;
                if (core is null) return false;

                var candle = core.GetCandle(bar); // 官方推荐：先取 bar 的 IndicatorCandle
                // 关键：转到接口再取"无参"重载，避免 cacheItem 语义/版本差异
                if (candle is ISupportedPriceInfo spi)
                {
                    var infos = spi.GetAllPriceLevels(); // IEnumerable<PriceVolumeInfo>
                    var list = new System.Collections.Generic.List<PriceLevelDTO>();
                    foreach (var pvi in infos)
                    {
                        if (pvi == null) continue;

                        double ask;
                        double bid;
                        try { ask = Convert.ToDouble(pvi.Ask); } catch { continue; }
                        try { bid = Convert.ToDouble(pvi.Bid); } catch { continue; }

                        double? price = null;
                        try
                        {
                            var rawPrice = pvi.Price;
                            if (rawPrice != null)
                            {
                                price = Convert.ToDouble(rawPrice);
                            }
                        }
                        catch
                        {
                            price = null;
                        }

                        list.Add(new PriceLevelDTO
                        {
                            Ask = ask,
                            Bid = bid,
                            Price = price
                        });
                    }
                    levels = list.ToArray();
                    return levels.Length > 0;
                }
                return false;
            }
            catch { return false; }
        }
    }

    public sealed class PriceLevelDTO
    {
        public double Ask { get; set; }
        public double Bid { get; set; }
        public double? Price { get; set; } // 仅 label
    }
}
