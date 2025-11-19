using ATAS.Indicators;
using System.ComponentModel;

namespace MIF.AtasIndicator.Cluster;

/// <summary>
/// 占位：Cluster 导出器 v1。
/// TODO: 接入 ATAS 簇/Footprint API，实现聚合导出逻辑。
/// </summary>
[DisplayName("MIF Cluster Exporter V1")]
public class ClusterExporterV1 : Indicator
{
    protected override void OnInitialize()
    {
        // TODO: 初始化数据订阅、簇聚合配置。
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        // TODO: 构建 Cluster 数据结构并输出 JSONL。
    }
}
