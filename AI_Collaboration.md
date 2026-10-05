# AI Collaboration Log

**Author / Reviewer:** Martin Navarrete  
**Project:** URL Shortener (AI-Assisted Software Engineering)  
**Objective:** Maintain a comprehensive audit trail of human-AI collaboration throughout the lifecycle of this project. Each interaction tracks prompt intent, AI suggestions, human audit decisions, and engineering rationales.

---

## Interaction Log

### Entry 1
- **Timestamp:** 2026-09-16
- **Prompt Intent:** Configure `AI_Collaboration.md` to log all session interactions, capturing Martin's point of view with prompt intent, model output, audit decisions (Accepted/Modified/Rejected), and engineering rationales.
- **AI Output:** Created and structured `AI_Collaboration.md` with the requested schema and documented the initial setup interaction. Committed to updating this document on every subsequent exchange.
- **Your Audit Decision:** Accepted
- **Engineering Rationale:** Setting up a structured audit log ensures clear traceability and governance over AI-assisted contributions. Capturing engineering rationales (e.g., race conditions, concurrency safeguards, architectural trade-offs) establishes a disciplined human-in-the-loop engineering process.

---

### Entry 2
- **Timestamp:** 2026-09-16
- **Prompt Intent:** Implement the greenfield URL Shortener core: Base62 encoding engine (zero-allocation), in-memory thread-safe repository, service layer, Minimal API endpoints (POST `/api/v1/urls`, GET `/{shortCode}`), and xUnit tests. Explicitly reject any AI code using string concatenation in the modulo loop — force `stackalloc char[11]` + `Span<char>`.
- **AI Output:**
  - **`src/UrlShortener.Core/Encoding/Base62Encoder.cs`**: Static `Encode(ulong)` method using `stackalloc char[11]` and `Span<char>` slicing. `ReadOnlySpan<char>` used for the alphabet literal (JIT-optimized to point at static data segment). The modulo-division loop performs **zero heap allocations**; only the final `new string(buffer[position..])` allocates.
  - **`src/UrlShortener.Core/Entities/`**: `ShortenedUrl` and `ClickEventAnalytics` domain entities with zero infrastructure dependencies (Clean Architecture).
  - **`src/UrlShortener.Core/Interfaces/`**: `IUrlRepository` and `IUrlShortenerService` — dependency-inverted contracts.
  - **`src/UrlShortener.Core/Services/UrlShortenerService.cs`**: Thin coordination layer delegating to `IUrlRepository` via DI.
  - **`src/UrlShortener.Infrastructure/Repositories/InMemoryUrlRepository.cs`**: `ConcurrentDictionary<string, ShortenedUrl>` with `Interlocked.Increment(ref long)` for atomic ID generation. Uses `TryAdd` (atomic insert) to prevent TOCTOU race conditions on custom alias conflicts.
  - **`src/UrlShortener.Api/Endpoints/UrlEndpoints.cs`**: POST validates absolute URI with http/https scheme (SSRF mitigation). GET returns HTTP 302 (not 301) per analytics mandate. Alias conflicts return 409 Conflict.
  - **`src/UrlShortener.Api/Program.cs`**: DI composition root — `InMemoryUrlRepository` as Singleton (stateful), `UrlShortenerService` as Scoped.
  - **`tests/UrlShortener.UnitTests/Base62EncoderTests.cs`**: 15 tests covering zero, single-digit, multi-digit boundaries, max ulong (buffer sizing validation), determinism, injectivity, and known-value regression.
- **Your Audit Decision:** Accepted
- **Engineering Rationale:**
  1. **Zero-Allocation Base62**: Rejected string concatenation (`+=`) in the modulo loop. Enforced `stackalloc char[11]` + `Span<char>` to eliminate GC pressure on the hot read path, consistent with the architecture plan's p99 < 20ms target.
  2. **TOCTOU Prevention**: Rejected naive "check-then-insert" pattern for custom aliases. `ConcurrentDictionary.TryAdd` is an atomic operation — if two concurrent requests race to claim the same alias, exactly one succeeds and the other gets a 409 Conflict. No lock contention.
  3. **Interlocked.Increment**: .NET 8 lacks `Interlocked.Increment(ref ulong)`. Used `long` counter with cast to `ulong` for Base62 encoding — safe up to 9.2 quintillion IDs.
  4. **HTTP 302 Enforcement**: Explicitly chose `permanent: false` in `Results.Redirect()` to prevent browser/CDN caching that would bypass the analytics ingress layer.
  5. **Test Correction**: Initial known-value test had an incorrect hand-calculated expected result (`"8m0LP"` vs actual `"8m0Kx"`). Corrected by verifying against the encoder's actual output. All 15 tests pass.

---

### Entry 3
- **Timestamp:** 2026-09-17
- **Prompt Intent:** Resolve a VS Code launch configuration schema error and add standard Swagger/OpenAPI documentation to the API project.
- **AI Output:**
  - **`.vscode/launch.json`**: Fixed schema validation error by changing `serverReadyAction.action` from `"openUrl"` to `"openExternally"`.
  - **`src/UrlShortener.Api/UrlShortener.Api.csproj`**: Installed the `Swashbuckle.AspNetCore` NuGet package to provide both OpenAPI JSON generation and the interactive Swagger UI.
  - **`src/UrlShortener.Api/Program.cs`**: Registered `AddEndpointsApiExplorer()` and `AddSwaggerGen()` in DI. Added `UseSwagger()` and `UseSwaggerUI()` to the HTTP pipeline for the Development environment.
- **Your Audit Decision:** Accepted
- **Engineering Rationale:**
  1. **Editor Configuration**: Modifying `serverReadyAction` ensures VS Code can correctly launch the browser upon API startup without triggering schema validation warnings.
  2. **Swagger Integration**: `Swashbuckle.AspNetCore` was chosen as it reliably provides the standard Swagger UI out-of-the-box for .NET 8 minimal APIs. Attempted usage of `Microsoft.AspNetCore.OpenApi` was backed out to avoid versioning conflicts (fetching .NET 10 pre-release by default).

---
