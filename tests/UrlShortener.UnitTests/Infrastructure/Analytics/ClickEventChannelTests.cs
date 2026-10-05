using UrlShortener.Core.Entities;
using UrlShortener.Infrastructure.Analytics;

namespace UrlShortener.UnitTests.Infrastructure.Analytics;

public class ClickEventChannelTests
{
    [Fact]
    public void Channel_RespectsCapacity_DropsOldestOnFull()
    {
        // Arrange
        var channel = new ClickEventChannel();
        // The channel capacity is 10,000. 
        // We will insert 10,005 items. 
        // The first 5 should be dropped.
        
        var totalToWrite = 10005;

        // Act
        for (int i = 0; i < totalToWrite; i++)
        {
            var success = channel.Writer.TryWrite(new ClickEvent
            {
                Id = Guid.NewGuid(),
                ShortCode = $"code{i}",
                TimestampUtc = DateTime.UtcNow
            });
            
            // TryWrite should always return true because DropOldest makes room
            Assert.True(success);
        }

        // Assert
        // Now read all items to count them. It should be exactly 10,000.
        int readCount = 0;
        string firstReadShortCode = "";
        
        while (channel.Reader.TryRead(out var ev))
        {
            if (readCount == 0)
            {
                firstReadShortCode = ev.ShortCode;
            }
            readCount++;
        }

        Assert.Equal(10000, readCount);
        
        // Since we wrote 10005 items (indexes 0 to 10004) and the first 5 (indexes 0 to 4) were dropped,
        // the first item we read should have index 5.
        Assert.Equal("code5", firstReadShortCode);
    }
}
