using Microsoft.Extensions.Logging;
using Moq;
using UrlShortener.Infrastructure.Analytics;

namespace UrlShortener.UnitTests.Infrastructure.Analytics;

public class AnalyticsServiceTests
{
    [Fact]
    public void TrackClick_ValidInputs_WritesToChannel()
    {
        // Arrange
        var channel = new ClickEventChannel();
        var mockLogger = new Mock<ILogger<AnalyticsService>>();
        var service = new AnalyticsService(channel, mockLogger.Object);
        
        var shortCode = "testCode";
        var userAgent = "testAgent";
        var referer = "testReferer";

        // Act
        service.TrackClick(shortCode, userAgent, referer);

        // Assert
        Assert.True(channel.Reader.TryRead(out var clickEvent));
        Assert.NotNull(clickEvent);
        Assert.Equal(shortCode, clickEvent.ShortCode);
        Assert.Equal(userAgent, clickEvent.UserAgent);
        Assert.Equal(referer, clickEvent.Referer);
        Assert.NotEqual(default, clickEvent.Id);
        Assert.Equal(DateTimeKind.Utc, clickEvent.TimestampUtc.Kind);
    }
}
