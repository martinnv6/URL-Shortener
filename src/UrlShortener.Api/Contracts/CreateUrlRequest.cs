namespace UrlShortener.Api.Contracts;

/// <summary>
/// Request payload for creating a shortened URL.
/// Property names use camelCase to match the JSON contract.
/// </summary>
public sealed record CreateUrlRequest(string Url, string? CustomAlias = null);
