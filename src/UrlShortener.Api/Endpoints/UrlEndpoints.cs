using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Contracts;
using UrlShortener.Core.Interfaces;
using UrlShortener.Core.Validation;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Api.Endpoints;

/// <summary>
/// Minimal API endpoint mappings for URL shortening operations.
/// Follows the architecture plan: API layer is responsible solely for
/// HTTP protocol binding, routing, and payload validation.
/// </summary>
public static class UrlEndpoints
{
    public static void MapUrlEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/urls", CreateShortUrl)
            .WithName("CreateShortUrl")
            .Produces<CreateUrlResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/{shortCode}", RedirectToOriginalUrl)
            .WithName("RedirectToOriginalUrl")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/urls/{shortCode}/analytics", GetAnalytics)
            .WithName("GetAnalytics")
            .Produces<ClickAnalyticsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// POST /api/v1/urls
    /// Validates the URL via <see cref="UrlSafetyValidator"/> (SSRF mitigation),
    /// validates the custom alias format, then delegates creation to the service layer.
    /// </summary>
    private static async Task<IResult> CreateShortUrl(
        CreateUrlRequest request,
        IUrlShortenerService service,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        // --- SSRF Validation (OWASP API7:2023) ---
        var urlValidation = UrlSafetyValidator.Validate(request.Url);
        if (!urlValidation.IsValid)
        {
            return Results.BadRequest(new
            {
                error = urlValidation.Detail,
                reasonCode = urlValidation.ReasonCode
            });
        }

        // --- Custom Alias Validation ---
        var aliasValidation = UrlSafetyValidator.ValidateCustomAlias(request.CustomAlias);
        if (!aliasValidation.IsValid)
        {
            return Results.BadRequest(new
            {
                error = aliasValidation.Detail,
                reasonCode = aliasValidation.ReasonCode
            });
        }

        try
        {
            var result = await service.CreateAsync(request.Url, request.CustomAlias, cancellationToken);
            var baseUrl = $"{httpRequest.Scheme}://{httpRequest.Host}";

            var response = new CreateUrlResponse(
                ShortCode: result.ShortCode,
                ShortUrl: $"{baseUrl}/{result.ShortCode}",
                OriginalUrl: result.OriginalUrl
            );

            return Results.Created($"/api/v1/urls/{result.ShortCode}", response);
        }
        catch (InvalidOperationException ex)
        {
            // Alias conflict — return 409 Conflict with RFC 7807 ProblemDetails.
            return Results.Problem(
                detail: ex.Message,
                title: "Alias Conflict",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <summary>
    /// GET /{shortCode}
    /// Resolves the short code and returns HTTP 302 Found.
    /// HTTP 302 is enforced (not 301) to guarantee all redirections pass through
    /// our ingress layer for deterministic analytics capture.
    ///
    /// Click metadata is dispatched to <see cref="IAnalyticsService"/> without
    /// awaiting background database writes — zero impact on redirect latency.
    /// </summary>
    private static async Task<IResult> RedirectToOriginalUrl(
        string shortCode,
        IUrlShortenerService service,
        IAnalyticsService analyticsService,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        var url = await service.GetByShortCodeAsync(shortCode, cancellationToken);

        if (url is null)
        {
            return Results.NotFound(new { error = $"Short code '{shortCode}' not found." });
        }

        // Fire-and-forget analytics dispatch — non-blocking, no await.
        analyticsService.TrackClick(
            shortCode,
            httpRequest.Headers.UserAgent.ToString(),
            httpRequest.Headers.Referer.ToString());

        // HTTP 302 Found — permanent: false ensures the browser re-requests
        // through our server on every visit, enabling click analytics.
        return Results.Redirect(url.OriginalUrl, permanent: false);
    }

    /// <summary>
    /// GET /api/v1/urls/{shortCode}/analytics
    /// Returns aggregated click metrics: total count and last 10 recorded clicks.
    /// </summary>
    private static async Task<IResult> GetAnalytics(
        string shortCode,
        IUrlShortenerService service,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Verify the short code exists.
        var url = await service.GetByShortCodeAsync(shortCode, cancellationToken);

        if (url is null)
        {
            return Results.NotFound(new { error = $"Short code '{shortCode}' not found." });
        }

        var totalClicks = await dbContext.ClickEvents
            .CountAsync(e => e.ShortCode == shortCode, cancellationToken);

        var recentClicks = await dbContext.ClickEvents
            .Where(e => e.ShortCode == shortCode)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(10)
            .Select(e => new ClickDetail(e.TimestampUtc, e.UserAgent, e.Referer))
            .ToListAsync(cancellationToken);

        var response = new ClickAnalyticsResponse(shortCode, totalClicks, recentClicks);

        return Results.Ok(response);
    }
}
