namespace UrlShortener.Api.Contracts;

/// <summary>
/// Response payload for the analytics endpoint.
/// </summary>
public sealed record ClickAnalyticsResponse(
    string ShortCode,
    int TotalClicks,
    IReadOnlyList<ClickDetail> RecentClicks);

/// <summary>
/// Individual click event detail within the analytics response.
/// </summary>
public sealed record ClickDetail(
    DateTimeOffset TimestampUtc,
    string? UserAgent,
    string? Referer);
