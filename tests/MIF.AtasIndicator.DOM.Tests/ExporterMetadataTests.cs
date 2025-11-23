using System;
using System.Reflection;
using MIF.AtasIndicator.DOM.Exporter;
using MIF.AtasIndicator;
using Xunit;

namespace MIF.AtasIndicator.DOM.Tests;

public class ExporterMetadataTests
{
    [Fact]
    public void DomExporterT1_ShouldNotBeMarkedObsolete()
    {
        var obsoleteAttr = typeof(DomExporterT1).GetCustomAttribute<ObsoleteAttribute>();
        Assert.Null(obsoleteAttr);
    }

    [Fact]
    public void HistoricVariants_ShouldBeMarkedObsolete()
    {
        var historicTypes = new[]
        {
            typeof(MifExporterV12),
            typeof(MifExporterV17),
            typeof(MifExporterV18),
            typeof(MifExporterV19),
            typeof(MifExporterV20),
            typeof(MifExporterV21),
            typeof(MifExporterV14F)
        };

        foreach (var type in historicTypes)
        {
            var obsoleteAttr = type.GetCustomAttribute<ObsoleteAttribute>();
            Assert.NotNull(obsoleteAttr);

            // Fix: Allow both legacy and newer obsolescence messages
            bool isStandardMsg = obsoleteAttr!.Message.Contains("Archived historic implementation");
            bool isSupersededMsg = obsoleteAttr!.Message.Contains("Superseded by");

            Assert.True(isStandardMsg || isSupersededMsg,
                $"Type {type.Name} has an unexpected obsolete message: {obsoleteAttr.Message}");
        }
    }
}
