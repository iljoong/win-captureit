using CaptureIt.App.Capture;
using Xunit;

namespace CaptureIt.Tests;

public class ImageSaveServiceFileNameTests
{
    [Fact]
    public void BuildFileName_ExpandsDatetimeToken()
    {
        var timestamp = new DateTime(2026, 7, 1, 15, 4, 5);
        var result = ImageSaveService.BuildFileName("Screenshot_{datetime}", timestamp);
        Assert.Equal("Screenshot_20260701_150405", result);
    }

    [Fact]
    public void BuildFileName_ExpandsDateAndTimeTokensSeparately()
    {
        var timestamp = new DateTime(2026, 7, 1, 15, 4, 5);
        var result = ImageSaveService.BuildFileName("{date}-{time}", timestamp);
        Assert.Equal("20260701-150405", result);
    }

    [Fact]
    public void BuildFileName_TimestampTokenMatchesDatetimeToken()
    {
        var timestamp = new DateTime(2026, 7, 1, 15, 4, 5);
        var result = ImageSaveService.BuildFileName("{timestamp}", timestamp);
        Assert.Equal("20260701_150405", result);
    }

    [Theory]
    [InlineData("My:Screenshot?", "My_Screenshot_")]
    [InlineData("a/b\\c", "a_b_c")]
    public void Sanitize_ReplacesInvalidFileNameCharsWithUnderscore(string input, string expected)
    {
        Assert.Equal(expected, ImageSaveService.Sanitize(input));
    }

    [Fact]
    public void Sanitize_EmptyOrWhitespace_FallsBackToDefaultName()
    {
        Assert.Equal("Screenshot", ImageSaveService.Sanitize(""));
        Assert.Equal("Screenshot", ImageSaveService.Sanitize("   "));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("LPT1")]
    public void Sanitize_ReservedWindowsDeviceNames_ArePrefixed(string reservedName)
    {
        var result = ImageSaveService.Sanitize(reservedName);
        Assert.NotEqual(reservedName, result);
        Assert.EndsWith(reservedName, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_VeryLongName_IsTruncated()
    {
        var longName = new string('a', 500);
        var result = ImageSaveService.Sanitize(longName);
        Assert.True(result.Length <= 150);
    }

    [Fact]
    public void Sanitize_TrimsTrailingDotsAndWhitespace()
    {
        var result = ImageSaveService.Sanitize("Screenshot...  ");
        Assert.False(result.EndsWith('.'));
    }
}
