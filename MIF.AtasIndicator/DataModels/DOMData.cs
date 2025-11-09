using System;

namespace MIF.AtasIndicator.DataModels
{
    public sealed class DOMData
    {
        public DateTime Timestamp { get; set; }
        public decimal[] AskVolumes { get; set; } = Array.Empty<decimal>();
        public decimal[] BidVolumes { get; set; } = Array.Empty<decimal>();
        public decimal[] PriceLevels { get; set; } = Array.Empty<decimal>();
        public decimal BestAsk { get; set; }
        public decimal BestBid { get; set; }
        public decimal Spread { get; set; }
        public decimal MidPrice { get; set; }
    }
}
