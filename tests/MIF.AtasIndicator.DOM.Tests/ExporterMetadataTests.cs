using System;
using System.Reflection;
using MIF.AtasIndicator.DOM.Exporter;
using MIF.AtasIndicator;
using Xunit;

namespace MIF.AtasIndicator.DOM.Tests;

public class ExporterMetadataTests
{
    [Fact]
    public void DomExporterV14_ShouldNotBeMarkedObsolete()
    {
        var obsoleteAttr = typeof(MifExporterV14F).GetCustomAttribute<ObsoleteAttribute>();
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
            typeof(MifExporterV21)
        };

        foreach (var type in historicTypes)
        {
            var obsoleteAttr = type.GetCustomAttribute<ObsoleteAttribute>();
            Assert.NotNull(obsoleteAttr);
            Assert.Contains("Archived historic implementation", obsoleteAttr!.Message);
        }
    }
}
