using System.Globalization;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class TextEllipsisTests
{
    [Fact]
    public void LeavesTextThatAlreadyFitsUnchanged()
    {
        Assert.Equal("Track", TextEllipsis.Fit("Track", 5f, static value => value.Length));
    }

    [Fact]
    public void ReplacesOverflowWithThreePeriods()
    {
        Assert.Equal("Long...", TextEllipsis.Fit("Long title", 7f, static value => value.Length));
    }

    [Fact]
    public void DoesNotSplitAUnicodeTextElement()
    {
        const string value = "A👩‍🚀BCDE";

        var fitted = TextEllipsis.Fit(
            value,
            5f,
            static candidate => StringInfo.ParseCombiningCharacters(candidate).Length);

        Assert.Equal("A👩‍🚀...", fitted);
    }
}
