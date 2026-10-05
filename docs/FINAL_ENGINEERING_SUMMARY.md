# Final Engineering Summary

**Author:** Martin Navarrete  
**Project:** Production-Grade URL Shortener Service  
**Framework:** .NET 8 Minimal APIs  
**Architecture:** Strict Clean Architecture · CQRS · Zero-Allocation Hot Paths  
**Document Purpose:** Publication-ready synthesis of all engineering work, architectural decisions, security hardening, and test verification.

---

## 1. Executive Summary

This document synthesizes the complete engineering lifecycle of a production-grade URL Shortener service. The system was designed from first principles to handle high-concurrency, read-heavy workloads (estimated 100:1 read-to-write ratio) while enforcing strict security guardrails aligned with the OWASP API Security Top 10 (2023).

### Final Metrics

| Metric | Value |
|---|---|
| **Total Test Count** | 129 |
| **Test Pass Rate** | 100% |
| **Line Coverage** | 88.0% (607 / 689) |
| **Branch Coverage** | 75.9% (164 / 216) |
| **Method Coverage** | 87.3% (76 / 87) |
| **Test Projects** | 3 (Unit, Functional, Integration) |
| **Source Assemblies** | 3 (Api, Core, Infrastructure) |
| **Total Source Lines** | 1,321 |
| **AI Collaboration Entries** | 12 audit-logged interactions |

---

## 2. Core Requirements Traceability

The following table maps each core requirement to its implementation and verification.

### Requirement 1: URL Shortening via Base62 Encoding

**Implementation:** [`Base62Encoder.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Core/Encoding/Base62Encoder.cs)

Zero-allocation Base62 encoding engine using `stackalloc char[11]` and `Span<char>` slicing. The `ReadOnlySpan<char>` alphabet literal is JIT-optimized to point directly at the static data segment — no heap copy. The modulo-division loop performs **zero heap allocations**; only the final `new string(buffer[position..])` allocates, which is unavoidable since a `string` must be returned.

**Design Decision:** Base62 encoding of auto-incrementing database IDs was chosen over cryptographic hashing (MD5/SHA) because:
- Hashing introduces collision risks requiring collision-resolution loops
- Base62 on a sequence is **deterministic** and **collision-free**
- It mathematically guarantees the shortest possible string for any given value

**Verification:** 11 unit tests in [`Base62EncoderTests.cs`](file:///c:/Repos/URL-Shortener/tests/UrlShortener.UnitTests/Encoding/Base62EncoderTests.cs) covering boundary values (`0`, `1`, `61`, `62`, `ulong.MaxValue`), reversibility, and character-set validation.

---

### Requirement 2: Persistent Storage with SQLite & EF Core

**Implementation:** [`SqliteUrlRepository.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Infrastructure/Persistence/SqliteUrlRepository.cs), [`AppDbContext.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Infrastructure/Persistence/AppDbContext.cs)

EF Core with SQLite provides a lightweight, zero-configuration persistent store that perfectly mimics relational semantics. The provider-agnostic design of EF Core allows a seamless pivot to PostgreSQL or SQL Server for production without any repository interface changes.

**Schema Design:**
- `ShortenedUrl`: Primary key `Id` (auto-increment), unique index on `ShortCode` (VARCHAR(15)), `OriginalUrl` (VARCHAR(2048)), `CreatedAt` (DateTimeOffset)
- `ClickEvent`: Primary key `Id` (Guid), indexed `ShortCode` FK, `Timestamp`, `UserAgent`, `Referer`, `IpAddress`

**Concurrency Safety:** The unique index on `ShortCode` is the authoritative concurrency control. A `try-catch(DbUpdateException)` wraps `SaveChangesAsync` to catch SQLite UNIQUE constraint violations (error codes 19/2067) caused by TOCTOU race conditions. The `AnyAsync` pre-check is retained as a fast-path optimization, not a correctness guarantee.

**Verification:** 18 tests in [`SqliteUrlRepositoryTests.cs`](file:///c:/Repos/URL-Shortener/tests/UrlShortener.UnitTests/Infrastructure/SqliteUrlRepositoryTests.cs) including `CreateAsync_ConcurrentDuplicateAlias_ThrowsInvalidOperationException`.

---

### Requirement 3: RESTful API with .NET 8 Minimal APIs

**Implementation:** [`UrlEndpoints.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Api/Endpoints/UrlEndpoints.cs), [`Program.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Api/Program.cs)

Three endpoints:
| Method | Route | Response | Purpose |
|---|---|---|---|
| `POST` | `/api/v1/urls` | `201 Created` | Create shortened URL (auto or custom alias) |
| `GET` | `/{shortCode}` | `302 Found` | Redirect + analytics capture |
| `GET` | `/api/v1/urls/{shortCode}/analytics` | `200 OK` | Aggregated click metrics |

**API Contract Design:**
- Request: `CreateUrlRequest(string originalUrl, string? customAlias)`
- Response: `CreateUrlResponse(string shortUrl, string shortCode, string originalUrl, DateTimeOffset createdAt)`
- Error responses use RFC 7807 `ProblemDetails` for 409 Conflict, and structured `{ error, reasonCode }` for 400 Bad Request

**Verification:** Full endpoint coverage across [`CreateUrlEndpointTests.cs`](file:///c:/Repos/URL-Shortener/tests/UrlShortener.FunctionalTests/Endpoints/CreateUrlEndpointTests.cs), [`RedirectEndpointTests.cs`](file:///c:/Repos/URL-Shortener/tests/UrlShortener.FunctionalTests/Endpoints/RedirectEndpointTests.cs), and the integration test suite.

---

### Requirement 4: HTTP 302 Redirect with Analytics Capture

**Design Decision:** HTTP 302 (Found) — **not** 301 (Moved Permanently) — is mandatory. HTTP 301 allows client-side and CDN caching, which bypasses our ingress layer and blinds the analytics engine. 302 forces every click to transit through our server, ensuring deterministic telemetry capture.

**Analytics Pipeline:** Click events are published to an in-memory `System.Threading.Channels.Channel<T>` — a non-blocking, fire-and-forget operation that **never blocks the redirect response**. A background `IHostedService` ([`AnalyticsProcessingWorker.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Infrastructure/Analytics/AnalyticsProcessingWorker.cs)) consumes the channel and persists analytics in batches (up to 100 events or 1-second timeout, whichever comes first).

**Verification:** [`AnalyticsEndpointTests.cs`](file:///c:/Repos/URL-Shortener/tests/UrlShortener.FunctionalTests/Endpoints/AnalyticsEndpointTests.cs) verifies accurate count after 4 clicks with background worker flush. Integration tests verify 3-click accuracy.

---

### Requirement 5: Bounded Channel Analytics (Async Decoupling)

**Implementation:** [`ClickEventChannel.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Infrastructure/Analytics/ClickEventChannel.cs), [`AnalyticsService.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Infrastructure/Analytics/AnalyticsService.cs), [`AnalyticsProcessingWorker.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Infrastructure/Analytics/AnalyticsProcessingWorker.cs)

**Design Decision:** `System.Threading.Channels` was chosen over `Task.Run` or external message brokers because:
- It provides a highly optimized, lock-free, in-memory producer/consumer queue
- It safely decouples the ultra-fast HTTP thread from the slower database I/O thread
- Bounded capacity (1,000 items) provides built-in backpressure — `BoundedChannelFullMode.Wait` blocks producers only when the buffer is exhausted, preventing out-of-memory conditions under extreme load
- Zero external dependencies — no Redis, RabbitMQ, or Kafka required for the initial deployment

**Batching Strategy:** The background worker accumulates up to 100 events or waits a maximum of 1 second before flushing — whichever threshold is reached first. This amortizes the cost of database round-trips under high load. During graceful shutdown, the worker drains all remaining items from the channel before stopping.

---

### Requirement 6: Custom Alias Support with Collision Handling

**Implementation:** [`UrlSafetyValidator.ValidateCustomAlias()`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Core/Validation/UrlSafetyValidator.cs), [`SqliteUrlRepository.CreateAsync()`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Infrastructure/Persistence/SqliteUrlRepository.cs)

**Alias Validation:** Custom aliases are restricted to `[a-zA-Z0-9\-_]{1,15}` via `[GeneratedRegex]` source generator. This provides:
- Compile-time regex compilation (zero JIT overhead, zero allocation at runtime)
- RFC 3986 unreserved characters subset — industry standard for URL-safe identifiers
- Maximum length of 15 characters — consistent with the `ShortCode` column size

**Collision Handling:** Two-tier defense:
1. **Fast path:** `AnyAsync` pre-check queries the database before insertion (O(1) index lookup)
2. **Authority:** `DbUpdateException` catch on unique index violation — mathematically guaranteed to prevent duplicates regardless of TOCTOU timing

On collision, the API returns HTTP 409 Conflict with RFC 7807 `ProblemDetails`:
```json
{
  "title": "Alias Conflict",
  "status": 409,
  "detail": "The alias 'my-alias' is already taken."
}
```

**Verification:** Sequential and concurrent collision tests in both unit and integration suites.

---

### Requirement 7: OWASP API Security Hardening

#### API7:2023 — Server-Side Request Forgery (SSRF)

**Implementation:** [`UrlSafetyValidator.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Core/Validation/UrlSafetyValidator.cs)

Pure static class in the Core layer — zero infrastructure dependencies. Any future ingestion path (CLI, message queue, scheduled import) inherits the same SSRF protection without code duplication.

**Blocked Vectors:**
| Vector | Examples | Reason Code |
|---|---|---|
| Non-HTTP/S schemes | `ftp://`, `file://`, `gopher://` | `SCHEME_NOT_ALLOWED` |
| IPv4 loopback | `127.0.0.0/8`, `localhost` | `LOOPBACK_ADDRESS` |
| IPv6 loopback | `::1` | `LOOPBACK_ADDRESS` |
| RFC 1918 private | `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` | `PRIVATE_NETWORK` |
| Link-local / cloud metadata | `169.254.0.0/16` (AWS/GCP `169.254.169.254`) | `LINK_LOCAL_ADDRESS` |
| IPv6 link-local | `fe80::/10` | `LINK_LOCAL_ADDRESS` |
| IPv6-mapped IPv4 | `::ffff:127.0.0.1`, `::ffff:10.0.0.1` | Maps to underlying type |

**Verification:** 28 unit tests + 13 integration SSRF tests covering all attack vectors.

#### Rate Limiting (Sliding Window)

**Implementation:** [`SlidingWindowRateLimiterMiddleware.cs`](file:///c:/Repos/URL-Shortener/src/UrlShortener.Api/Middleware/SlidingWindowRateLimiterMiddleware.cs)

In-memory, zero external dependencies. `ConcurrentDictionary<string, ConcurrentQueue<DateTime>>` keyed by client IP. Returns HTTP 429 with `Retry-After` header.

**Known Limitation:** Per-process, non-durable. Does not survive app restarts. For horizontal scaling, replace with Redis-backed rate limiting.

#### API10:2023 — Unsafe Consumption of APIs

**Status:** Policy documented in [`ARCHITECTURE_PLAN.md`](file:///c:/Repos/URL-Shortener/ARCHITECTURE_PLAN.md), Section 4. No current outbound HTTP consumers exist — the 302 redirect delegates fetching to the client browser. Implementing speculative infrastructure violates YAGNI.

---

### Requirement 8: Comprehensive Test Suite

| Project | Tests | Coverage | Purpose |
|---|---|---|---|
| [`UrlShortener.UnitTests`](file:///c:/Repos/URL-Shortener/tests/UrlShortener.UnitTests) | 75 | — | Isolated unit tests (Base62, Service, Repository, Validator, Middleware) |
| [`UrlShortener.FunctionalTests`](file:///c:/Repos/URL-Shortener/tests/UrlShortener.FunctionalTests) | 21 | — | WebApplicationFactory endpoint tests |
| [`UrlShortener.IntegrationTests`](file:///c:/Repos/URL-Shortener/tests/UrlShortener.IntegrationTests) | 33 | — | Full pipeline integration (SSRF, race conditions, rate limiting) |
| **Total** | **129** | **88% line** | **100% pass rate** |

**Test Infrastructure Design:**
- `WebApplicationFactory<Program>` boots the full ASP.NET Core pipeline per test class
- Each `IClassFixture<T>` provisions a unique GUID-based SQLite database file — zero cross-talk during parallel xUnit execution
- Analytics background worker flush is awaited via polling with configurable timeout
- Rate limiter tests are isolated in dedicated fixtures to avoid request budget contention

---

## 3. Architectural Trade-offs

| Decision | Alternative Considered | Rationale for Choice |
|---|---|---|
| Base62 on auto-increment ID | SHA-256 truncation | Deterministic, collision-free, shortest possible output |
| `System.Threading.Channels` | `Task.Run`, RabbitMQ | Lock-free, zero external dependencies, bounded backpressure |
| HTTP 302 (not 301) | HTTP 301 Moved Permanently | 301 allows client/CDN caching, blinds analytics |
| SQLite (EF Core) | PostgreSQL, DynamoDB | Zero-config local dev; EF Core provider swap for production |
| In-memory rate limiter | Redis rate limiter | Single-instance scope; documented upgrade path |
| `[GeneratedRegex]` | `Regex.IsMatch()` | Compile-time codegen, zero JIT overhead, zero allocation |
| `stackalloc` + `Span<T>` | `StringBuilder` | Zero GC pressure on hot path |
| Feature 4 deferred | Implement `HttpClientFactory` | No current consumers — YAGNI principle |

---

## 4. AI Governance Ledger

All human-AI interactions are audit-logged in [`AI_Collaboration.md`](file:///c:/Repos/URL-Shortener/AI_Collaboration.md). The ledger enforces a structured schema:

```
- Timestamp
- Prompt Intent (what was asked)
- AI Output (what was generated)
- Your Audit Decision (Accepted / Modified / Rejected)
- Engineering Rationale (why the decision was made)
```

This document provides complete traceability of every architectural decision, code generation, debugging session, and test implementation throughout the project lifecycle. 12 entries document the full progression from greenfield scaffolding to OWASP-hardened production readiness.

---

## 5. Repository Structure

```
URL-Shortener/
├── src/
│   ├── UrlShortener.Api/                # Minimal API layer
│   │   ├── Contracts/                   # Request/Response DTOs
│   │   ├── Endpoints/                   # Route handlers
│   │   └── Middleware/                  # Rate limiter
│   ├── UrlShortener.Core/              # Pure domain layer (zero dependencies)
│   │   ├── Encoding/                    # Base62 encoder
│   │   ├── Entities/                    # Domain entities
│   │   ├── Interfaces/                  # Repository & service contracts
│   │   ├── Services/                    # Business logic
│   │   └── Validation/                  # SSRF & alias validation
│   └── UrlShortener.Infrastructure/    # EF Core, Channels, Analytics
│       ├── Analytics/                   # Background worker, channel, service
│       ├── Persistence/                 # DbContext, SQLite repository
│       └── Repositories/               # Legacy in-memory repository
├── tests/
│   ├── UrlShortener.UnitTests/         # 75 tests
│   ├── UrlShortener.FunctionalTests/   # 21 tests
│   └── UrlShortener.IntegrationTests/  # 33 tests
├── ARCHITECTURE_PLAN.md                # Technical architecture & API contracts
├── AI_Collaboration.md                 # Human-AI governance audit trail
├── .gitignore                          # Build artifacts, SQLite DBs, coverage
└── UrlShortener.slnx                   # Solution file
```

---

## 6. Coverage by Assembly

| Assembly | Line Coverage | Key Classes at 100% |
|---|---|---|
| **UrlShortener.Api** | 91.1% | `UrlEndpoints`, `CreateUrlRequest/Response`, `ClickAnalyticsResponse` |
| **UrlShortener.Core** | 87.3% | `Base62Encoder`, `UrlShortenerService`, `UrlValidationResult` |
| **UrlShortener.Infrastructure** | 86.3% | `SqliteUrlRepository` (100%), `AppDbContext`, `ClickEventChannel` |

**Uncovered areas:** `InMemoryUrlRepository` (0% — legacy class superseded by SQLite), `ClickEventAnalytics` entity (0% — projection DTO not directly instantiated in tests), rate limiter cleanup timer (internal periodic maintenance).
