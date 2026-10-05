using Moq;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Interfaces;
using UrlShortener.Core.Services;

namespace UrlShortener.UnitTests.Services;

public class UrlShortenerServiceTests
{
    private readonly Mock<IUrlRepository> _mockRepository;
    private readonly UrlShortenerService _service;

    public UrlShortenerServiceTests()
    {
        _mockRepository = new Mock<IUrlRepository>();
        _service = new UrlShortenerService(_mockRepository.Object);
    }

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new UrlShortenerService(null!));
    }

    [Fact]
    public async Task CreateAsync_ValidUrl_ReturnsShortenedUrl()
    {
        // Arrange
        var originalUrl = "https://example.com";
        var expectedEntity = new ShortenedUrl
        {
            Id = 1,
            OriginalUrl = originalUrl,
            ShortCode = "1",
            CreatedAt = DateTimeOffset.UtcNow
        };
        
        _mockRepository
            .Setup(r => r.CreateAsync(originalUrl, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEntity);

        // Act
        var result = await _service.CreateAsync(originalUrl);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedEntity.ShortCode, result.ShortCode);
        Assert.Equal(expectedEntity.OriginalUrl, result.OriginalUrl);
        _mockRepository.Verify(r => r.CreateAsync(originalUrl, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByShortCodeAsync_ExistingCode_ReturnsShortenedUrl()
    {
        // Arrange
        var shortCode = "abc";
        var expectedEntity = new ShortenedUrl
        {
            Id = 1,
            OriginalUrl = "https://example.com",
            ShortCode = shortCode,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockRepository
            .Setup(r => r.GetByShortCodeAsync(shortCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEntity);

        // Act
        var result = await _service.GetByShortCodeAsync(shortCode);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedEntity.ShortCode, result.ShortCode);
        Assert.Equal(expectedEntity.OriginalUrl, result.OriginalUrl);
    }

    [Fact]
    public async Task GetByShortCodeAsync_NonExistingCode_ReturnsNull()
    {
        // Arrange
        var shortCode = "nonexistent";

        _mockRepository
            .Setup(r => r.GetByShortCodeAsync(shortCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShortenedUrl?)null);

        // Act
        var result = await _service.GetByShortCodeAsync(shortCode);

        // Assert
        Assert.Null(result);
    }
}
