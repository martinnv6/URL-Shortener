namespace UrlShortener.Api.Contracts;

/// <summary>
/// Response payload returned after successfully creating a shortened URL.
/// </summary>
public sealed record CreateUrlResponse(string ShortCode, string ShortUrl, string OriginalUrl);
