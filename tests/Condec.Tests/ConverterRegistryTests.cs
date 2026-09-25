using Condec.Core.Conversion;

namespace Condec.Tests;

public class ConverterRegistryTests
{
    [Fact]
    public void TargetOptions_MergeConverters_InRegistrationOrder_WithCatalogNames()
    {
        var registry = new ConverterRegistry(
            [
                new FakeConverter(".docx", ".pdf", ".odt"),
                new FakeConverter(".docx", ".odt", ".md"),
            ],
            []);

        var options = registry.GetTargetOptions(".docx");

        Assert.Equal([".pdf", ".odt", ".md"], options.Select(o => o.Extension));
        Assert.Equal(["PDF", "ODT", "Markdown"], options.Select(o => o.DisplayName));
        Assert.All(options, o => Assert.True(o.IsEnabled));
    }

    [Fact]
    public void MissingExternalTool_ListsItsFormatsDisabled_WithReason()
    {
        var registry = new ConverterRegistry(
            [new FakeToolConverter(".docx", ExternalToolStatus.Unavailable("LibreOffice belum terpasang."), ".pdf")],
            []);

        var option = Assert.Single(registry.GetTargetOptions(".docx"));

        Assert.False(option.IsEnabled);
        Assert.Equal("LibreOffice belum terpasang.", option.DisabledReason);
        Assert.Null(registry.FindConverter(".docx", ".pdf"));
    }

    [Fact]
    public void EnabledConverter_WinsOverDisabledOneForTheSameTarget()
    {
        var available = new FakeConverter(".docx", ".pdf");
        var registry = new ConverterRegistry(
            [new FakeToolConverter(".docx", ExternalToolStatus.Unavailable("x"), ".pdf"), available],
            []);

        var option = Assert.Single(registry.GetTargetOptions(".docx"));

        Assert.True(option.IsEnabled);
        Assert.Null(option.DisabledReason);
        Assert.Same(available, registry.FindConverter(".docx", ".pdf"));
    }

    [Theory]
    [InlineData("PNG")]
    [InlineData(".Png")]
    [InlineData(" png ")]
    public void Extensions_AreNormalized(string source)
    {
        var converter = new FakeConverter(".png", ".JPG");
        var registry = new ConverterRegistry([converter], [new FakeValidator(".jpg")]);

        Assert.Equal(".jpg", Assert.Single(registry.GetTargetOptions(source)).Extension);
        Assert.Same(converter, registry.FindConverter(source, "jpg"));
        Assert.NotNull(registry.FindValidator("JPG"));
    }

    [Fact]
    public void SameFormatAsSource_IsNotOffered()
    {
        var registry = new ConverterRegistry([new FakeConverter(".png", ".png", ".bmp")], []);

        Assert.Equal([".bmp"], registry.GetTargetOptions(".png").Select(o => o.Extension));
    }

    [Fact]
    public void SourceExtensions_AreThoseWithAtLeastOneTarget()
    {
        var registry = new ConverterRegistry(
            [new FakeConverter(".png", ".bmp"), new FakeConverter(".docx", ".pdf"), new FakeConverter(".gif", ".gif")],
            []);

        // .gif only "converts" to itself, which is never offered, so it isn't a usable source.
        Assert.Equal([".docx", ".png"], registry.GetSourceExtensions().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UnknownSource_HasNoTargets()
    {
        var registry = new ConverterRegistry([new FakeConverter(".png", ".bmp")], []);

        Assert.Empty(registry.GetTargetOptions(".xyz"));
        Assert.Null(registry.FindConverter(".xyz", ".bmp"));
    }
}
