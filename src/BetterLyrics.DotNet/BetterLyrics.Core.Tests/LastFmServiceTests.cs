using BetterLyrics.Core.Implementations.Services;
using Xunit;

namespace BetterLyrics.Core.Tests;

public class LastFmServiceTests
{
    [Theory]
    [InlineData(null, "configured-secret", false)]
    [InlineData("configured-key", null, false)]
    [InlineData("", "", false)]
    [InlineData("   ", "configured-secret", false)]
    [InlineData("configured-key", "   ", false)]
    [InlineData("Your api key here", "configured-secret", false)]
    [InlineData("configured-key", "Your shared secret here", false)]
    [InlineData(" Your api key here ", " Your shared secret here ", false)]
    [InlineData("configured-key", "configured-secret", true)]
    public void LastFmRequiresBothConfiguredCredentials(string? apiKey, string? sharedSecret, bool expected)
    {
        Assert.Equal(expected, LastFmService.HasConfiguredCredentials(apiKey, sharedSecret));
    }
}
