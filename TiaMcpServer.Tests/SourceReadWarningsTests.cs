using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class SourceReadWarningsTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void A_dependency_read_that_returned_several_objects_is_warned_about()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: true, SourceFormatNames.Source, Fixture("DamperAnalog.scl"));

        var warning = Assert.Single(warnings);
        Assert.Contains("4 objects", warning);
        Assert.Contains("HMI_Settings_DB", warning);
        Assert.Contains("context only", warning);
        Assert.Contains("withDependencies", warning);
    }

    [Fact]
    public void A_dependency_read_that_returned_one_object_is_not_warned_about()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: true, SourceFormatNames.Source, Fixture("DamperDigital.scl"));

        Assert.Empty(warnings);
    }

    [Fact]
    public void A_default_read_is_never_warned_about()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: false, SourceFormatNames.Source, Fixture("DamperAnalog.scl"));

        Assert.Empty(warnings);
    }

    [Fact]
    public void An_xml_read_is_never_warned_about()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: true, SourceFormatNames.Xml, Fixture("AnalogInputSettings.xml"));

        Assert.Empty(warnings);
    }

    [Fact]
    public void Empty_content_produces_no_warning()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: true, SourceFormatNames.Source, string.Empty);

        Assert.Empty(warnings);
    }
}
