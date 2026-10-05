using System.Net;
using System.Text.RegularExpressions;

namespace UrlShortener.Core.Validation;

/// <summary>
/// Result of URL safety validation.
/// </summary>
public sealed record UrlValidationResult(bool IsValid, string? ReasonCode = null, string? Detail = null)
{
    public static UrlValidationResult Valid() => new(true);
    public static UrlValidationResult Invalid(string reasonCode, string detail) => new(false, reasonCode, detail);
}

/// <summary>
/// Validates URLs against SSRF and injection attack vectors.
/// Pure static class with zero infrastructure dependencies — safe for the Core layer.
///
/// OWASP API7:2023 — Server-Side Request Forgery mitigation.
/// Uses the native .NET <see cref="Uri"/> class for parsing (no third-party libs).
/// </summary>
public static partial class UrlSafetyValidator
{
    /// <summary>
    /// Regex for valid custom aliases: alphanumeric, hyphens, and underscores.
    /// Industry standard per RFC 3986 unreserved characters (subset).
    /// Min 1 char, max 15 chars.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9\-_]{1,15}$")]
    private static partial Regex CustomAliasPattern();

    /// <summary>
    /// Validates a URL for safety, rejecting SSRF attack vectors.
    /// </summary>
    public static UrlValidationResult Validate(string? url)
    {
        // ── Step 1: Null/empty check ──
        if (string.IsNullOrWhiteSpace(url))
        {
            return UrlValidationResult.Invalid("INVALID_URL", "URL is required.");
        }

        // ── Step 2: Parse with native Uri class ──
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return UrlValidationResult.Invalid("INVALID_URL", "URL must be a valid absolute URI.");
        }

        // ── Step 3: Scheme allowlist (kills file://, gopher://, javascript:, data:, ftp://) ──
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return UrlValidationResult.Invalid(
                "SCHEME_NOT_ALLOWED",
                $"URL scheme '{uri.Scheme}' is not allowed. Only http and https are permitted.");
        }

        // ── Step 4: Host type guard ──
        if (uri.HostNameType == UriHostNameType.Unknown || uri.HostNameType == UriHostNameType.Basic)
        {
            return UrlValidationResult.Invalid("INVALID_HOST", "URL host is not valid.");
        }

        // ── Step 5: Hostname-based loopback detection ──
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return UrlValidationResult.Invalid(
                "LOOPBACK_ADDRESS",
                "URLs targeting localhost are not allowed.");
        }

        // ── Step 6: IP-based validation ──
        if (IPAddress.TryParse(uri.Host, out var ipAddress))
        {
            return ValidateIpAddress(ipAddress);
        }

        return UrlValidationResult.Valid();
    }

    /// <summary>
    /// Validates a custom alias against the allowed character set.
    /// </summary>
    public static UrlValidationResult ValidateCustomAlias(string? alias)
    {
        if (alias is null)
        {
            return UrlValidationResult.Valid(); // null alias means auto-generate
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            return UrlValidationResult.Invalid(
                "INVALID_ALIAS",
                "Custom alias cannot be empty or whitespace.");
        }

        if (alias.Length > 15)
        {
            return UrlValidationResult.Invalid(
                "ALIAS_TOO_LONG",
                "Custom alias must be 15 characters or fewer.");
        }

        if (!CustomAliasPattern().IsMatch(alias))
        {
            return UrlValidationResult.Invalid(
                "INVALID_ALIAS_CHARACTERS",
                "Custom alias may only contain letters (a-z, A-Z), digits (0-9), hyphens (-), and underscores (_).");
        }

        return UrlValidationResult.Valid();
    }

    private static UrlValidationResult ValidateIpAddress(IPAddress ip)
    {
        // Handle IPv6-mapped IPv4 addresses (e.g., ::ffff:127.0.0.1)
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        // Loopback: 127.0.0.0/8 or ::1
        if (IPAddress.IsLoopback(ip))
        {
            return UrlValidationResult.Invalid(
                "LOOPBACK_ADDRESS",
                "URLs targeting loopback addresses (127.0.0.0/8, ::1) are not allowed.");
        }

        // Only check private/link-local ranges for IPv4
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // RFC 1918: 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return UrlValidationResult.Invalid(
                    "PRIVATE_NETWORK",
                    "URLs targeting private network addresses (10.0.0.0/8) are not allowed.");
            }

            // RFC 1918: 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return UrlValidationResult.Invalid(
                    "PRIVATE_NETWORK",
                    "URLs targeting private network addresses (172.16.0.0/12) are not allowed.");
            }

            // RFC 1918: 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return UrlValidationResult.Invalid(
                    "PRIVATE_NETWORK",
                    "URLs targeting private network addresses (192.168.0.0/16) are not allowed.");
            }

            // Link-local: 169.254.0.0/16 (cloud metadata endpoint: 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return UrlValidationResult.Invalid(
                    "LINK_LOCAL_ADDRESS",
                    "URLs targeting link-local addresses (169.254.0.0/16) are not allowed. This includes cloud metadata endpoints.");
            }
        }

        // IPv6 link-local: fe80::/10
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
            {
                return UrlValidationResult.Invalid(
                    "LINK_LOCAL_ADDRESS",
                    "URLs targeting IPv6 link-local addresses (fe80::/10) are not allowed.");
            }
        }

        return UrlValidationResult.Valid();
    }
}
