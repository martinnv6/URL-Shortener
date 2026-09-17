# ARCHITECTURE_PLAN.md

**Document Classification:** Internal / Highly Confidential
**Project:** Production-Grade URL Shortener Service
**Target Framework:** .NET 8
**Architectural Paradigm:** Strict Clean Architecture, CQRS, Zero-Allocation Hot Paths

---

## 1. Executive Summary & Design Principles

### System Objectives
The URL Shortener Service is designed as a highly concurrent, read-heavy distributed system (estimated 100:1 read-to-write ratio). 
*   **Performance Target:** Redirection (Read Path) p99 latency must remain strictly `< 20ms`.
*   **Reliability:** The system must degrade gracefully. Analytics ingestion must never block the critical redirection path.
*   **Analytics Mandate:** To guarantee deterministic telemetry capture, the system will strictly enforce **HTTP 302 (Found)** temporary redirects. HTTP 301 (Moved Permanently) is explicitly rejected as it allows client-side and CDN caching, which bypasses our ingress layer and blinds our analytics engine.

### Core Design Principles
*   **Strict Clean Architecture:** The Core Domain layer will have zero dependencies on external frameworks, I/O, or databases. All external concerns are inverted via interfaces.
*   **Zero-Allocation Hot Paths:** The Read Path and Base62 encoding engine will utilize .NET 8 `Span<T>`, `ReadOnlySpan<T>`, and `stackalloc` to ensure memory allocations are amortized to zero, eliminating Garbage Collection (GC) pauses during high-throughput spikes.
*   **SOLID & Loose Coupling:** Features are isolated using the CQRS pattern (Command Query Responsibility Segregation) to scale read and write concerns independently.

---

## 2. Component Architecture & Control Flow

### Detailed Component Breakdown
1.  **API Layer (`UrlShortener.Api`):** .NET 8 Minimal APIs. Responsible solely for HTTP protocol binding, routing, JWT/API Key validation, and rate limiting.
2.  **Core Domain Layer (`UrlShortener.Core`):** Contains pure domain entities (`ShortenedUrl`, `ClickEventAnalytics`), repository interfaces, and the zero-allocation Base62 encoding engine.
3.  **Infrastructure Layer (`UrlShortener.Infrastructure`):** Implements EF Core (SQLite for local development/brownfield transition), `System.Threading.Channels` for background processing, and external API integrations.

### End-to-End Control Flow
*   **Write Path (URL Creation):**
    1. Client POSTs to `/api/v1/urls`.
    2. API Layer validates payload (SSRF checks, URI format).
    3. Command is dispatched to the Core Layer.
    4. Core Layer generates a unique Base62 `ShortCode` (or validates a custom alias).
    5. Infrastructure Layer persists the `ShortenedUrl` entity via EF Core.
    6. API returns the shortened URL.
*   **Read Path (URL Redirection & Analytics):**
    1. Client GETs `/{shortCode}`.
    2. API Layer queries the Core Layer for the `OriginalUrl`.
    3. Infrastructure Layer retrieves the URL (via L1/L2 cache or DB).
    4. **Crucial Step:** A `ClickEventAnalytics` payload is published to an in-memory `System.Threading.Channels.Channel<T>`. This is a non-blocking, fire-and-forget operation.
    5. API immediately returns an HTTP 302 response to the client.
    6. A Background `IHostedService` consumes the Channel and persists analytics to the database asynchronously.

### Technology Selections & Technical Rationales
*   **Base62 vs. Cryptographic Hashing:** Base62 encoding of a unique sequence (e.g., a database sequence or Snowflake ID) is chosen over MD5/SHA. Hashing introduces collision risks and requires collision-resolution loops. Base62 on a sequence is deterministic, collision-free, and mathematically guarantees the shortest possible string.
*   **EF Core with SQLite:** Chosen for the initial brownfield implementation to provide a lightweight, zero-configuration persistent store that perfectly mimics relational semantics. The provider-agnostic design of EF Core allows a seamless pivot to PostgreSQL for production.
*   **`System.Threading.Channels`:** Chosen over standard `Task.Run` or external message brokers (for the initial scope) because it provides a highly optimized, lock-free, in-memory producer/consumer queue. It safely decouples the ultra-fast HTTP thread from the slower database I/O thread.

---

## 3. Data Schemas & API Contracts

### Entity Models

**Entity: `ShortenedUrl`**
*   `Id` (BIGINT / long) - Primary Key, Auto-increment/Sequence.
*   `ShortCode` (VARCHAR(15)) - Unique Index. The Base62 encoded string or custom alias.
*   `OriginalUrl` (VARCHAR(2048)) - The destination URI.
*   `CreatedAt` (DATETIME / DateTimeOffset) - UTC timestamp.
*   `ExpiresAt` (DATETIME / DateTimeOffset) - Nullable. For TTL-based expiration.

**Entity: `ClickEventAnalytics`**
*   `Id` (UUID / Guid) - Primary Key.
*   `ShortCode` (VARCHAR(15)) - Foreign Key / Indexed. Links to the shortened URL.
*   `Timestamp` (DATETIME / DateTimeOffset) - UTC timestamp of the click.
*   `IpAddress` (VARCHAR(64)) - Hashed/Anonymized IP for geographic analytics.
*   `UserAgent` (VARCHAR(512)) - Browser/Client identifier.
*   `Referer` (VARCHAR(2048)) - Nullable. The referring page.

### OpenAPI Specifications (YAML Snippets)

```yaml
paths:
  /api/v1/urls:
    post:
      summary: Create a shortened URL
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              required: [originalUrl]
              properties:
                originalUrl:
                  type: string
                  format: uri
                customAlias:
                  type: string
                  maxLength: 15
      responses:
        '201':
          description: Created
          content:
            application/json:
              schema:
                type: object
                properties:
                  shortUrl: { type: string }
                  shortCode: { type: string }
                  createdAt: { type: string, format: date-time }

  /{shortCode}:
    get:
      summary: Redirect to original URL
      parameters:
        - in: path
          name: shortCode
          required: true
          schema:
            type: string
      responses:
        '302':
          description: Found (Temporary Redirect enforced for analytics)
          headers:
            Location:
              schema:
                type: string
                format: uri

  /api/v1/urls/{shortCode}/analytics:
    get:
      summary: Get aggregated click metrics
      parameters:
        - in: path
          name: shortCode
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Analytics data
          content:
            application/json:
              schema:
                type: object
                properties:
                  totalClicks: { type: integer }
                  recentClicks: 
                    type: array
                    items:
                      type: object # ClickEventAnalytics projection