using OfficeAssistant.Core;

namespace AcsFormat.Tests;

public class ScaleTests
{
    [Theory]
    [InlineData(96u, 1)]
    [InlineData(120u, 1)]
    [InlineData(144u, 2)]
    [InlineData(192u, 2)]
    [InlineData(240u, 3)]
    [InlineData(288u, 3)]
    [InlineData(384u, 4)]
    [InlineData(480u, 4)]
    [InlineData(48u, 1)]
    public void DefaultPixelSizeFollowsTheDisplayScaling(uint dpi, int expected) =>
        Assert.Equal(expected, AssistantController.ScaleForDpi(dpi));
}
