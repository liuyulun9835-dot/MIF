using System;

namespace MIF.AtasIndicator.DataModels
{
    public sealed class ClusterData
    {
        public DateTime Timestamp { get; set; }
        public decimal[] BuyVolumes { get; set; } = Array.Empty<decimal>();
        public decimal[] SellVolumes { get; set; } = Array.Empty<decimal>();
        public decimal TotalVolume { get; set; }
        public decimal Delta { get; set; }
        public int TradesCount { get; set; }
        public int BuyTrades { get; set; }
        public int SellTrades { get; set; }
        public decimal MaxSingleTrade { get; set; }
    }
}
