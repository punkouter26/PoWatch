using PoWatch.Domain.Services;

namespace PoWatch.Unit;

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

    [Fact]
    public void TryExtract_ReturnsTrue_WhenPayloadHasAIPreamble()
    {
        // AI models often prefix responses with preamble text before the structured tags.
        var ok = ClinicalTagParser.TryExtract("Sure! Here is the activity: <S>Patient standing at window<E>", out var extracted);

        Assert.True(ok);
        Assert.Equal("Patient standing at window", extracted);
    }

    [Fact]
    public void TryExtract_ReturnsTrue_WhenPayloadHasAIPostamble()
    {
        var ok = ClinicalTagParser.TryExtract("<S>Sitting at desk<E> Hope that helps!", out var extracted);

        Assert.True(ok);
        Assert.Equal("Sitting at desk", extracted);
    }
}

