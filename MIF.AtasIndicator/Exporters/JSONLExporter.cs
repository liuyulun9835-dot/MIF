using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MIF.AtasIndicator.DataModels;

namespace MIF.AtasIndicator.Exporters
{
    public sealed class JSONLExporter : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly JsonSerializerOptions _options;
        private readonly string _timeframe;

        public JSONLExporter(string filePath, string timeframe)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path must not be empty", nameof(filePath));
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };

            _timeframe = timeframe;

            _options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        public void ExportBar(BarData barData)
        {
            if (barData is null)
            {
                throw new ArgumentNullException(nameof(barData));
            }

            var domPayload = barData.DOM is null
                ? null
                : new
                {
                    ask_volumes = barData.DOM.AskVolumes,
                    bid_volumes = barData.DOM.BidVolumes,
                    price_levels = barData.DOM.PriceLevels,
                    best_ask = barData.DOM.BestAsk,
                    best_bid = barData.DOM.BestBid,
                    spread = barData.DOM.Spread,
                    mid_price = barData.DOM.MidPrice
                };

            var clusterPayload = barData.Cluster is null
                ? null
                : new
                {
                    buy_volumes = barData.Cluster.BuyVolumes,
                    sell_volumes = barData.Cluster.SellVolumes,
                    total_volume = barData.Cluster.TotalVolume,
                    delta = barData.Cluster.Delta,
                    trades_count = barData.Cluster.TradesCount,
                    buy_trades = barData.Cluster.BuyTrades,
                    sell_trades = barData.Cluster.SellTrades,
                    max_single_trade = barData.Cluster.MaxSingleTrade
                };

            var ohlcPayload = barData.OHLC is null
                ? null
                : new
                {
                    open = barData.OHLC.Open,
                    high = barData.OHLC.High,
                    low = barData.OHLC.Low,
                    close = barData.OHLC.Close
                };

            var exportTimestamp = DateTime.UtcNow;

            var jsonObject = new
            {
                version = barData.Version,
                export_timestamp = exportTimestamp.ToString("o"),
                timeframe = _timeframe,
                bar_index = barData.BarIndex,
                timestamp = barData.MasterTimestamp?.ToString("o"),
                ohlc = ohlcPayload,
                dom = domPayload,
                cluster = clusterPayload
            };

            string jsonLine = JsonSerializer.Serialize(jsonObject, _options);
            _writer.WriteLine(jsonLine);
        }

        public void Dispose()
        {
            _writer.Dispose();
        }
    }
}
