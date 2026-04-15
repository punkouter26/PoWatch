using PoWatch.Domain.Services;

namespace PoWatch.UnitTests;

public sealed class ClinicalTagParserTests
{
    [Fact]
    public void TryExtract_ReturnsTrue_WhenPayloadIsWellFormed()
    {
        var ok = ClinicalTagParser.TryExtract("<S>Subject typing at desk<E>", out var extracted);

        Assert.True(ok);
        Assert.Equal("Subject typing at desk", extracted);
    }

    [Fact]
    public void TryExtract_ReturnsFalse_WhenPayloadIsMalformed()
    {
        var ok = ClinicalTagParser.TryExtract("Subject typing at desk", out var extracted);

        Assert.False(ok);
        Assert.Equal(string.Empty, extracted);
    }

    [Fact]
    public void TryExtract_ReturnsFalse_WhenPayloadContainsNestedStartTag()
    {
        var ok = ClinicalTagParser.TryExtract("<S>text<S>more<E>", out var extracted);

        Assert.False(ok);
        Assert.Equal(string.Empty, extracted);
    }
}

