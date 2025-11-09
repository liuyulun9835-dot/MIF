using System;

namespace MIF.AtasIndicator.DataModels
{
    public sealed class BarData
    {
        public string Version { get; set; } = string.Empty;
        public int BarIndex { get; set; }
        public DateTime? MasterTimestamp { get; set; }
        public OHLCData? OHLC { get; set; }
        public DOMData? DOM { get; set; }
        public ClusterData? Cluster { get; set; }

        public bool IsComplete()
        {
            return MasterTimestamp.HasValue && OHLC is not null && (DOM is not null || Cluster is not null);
        }
    }
}
