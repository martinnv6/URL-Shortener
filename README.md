# High-Performance URL Shortener

An enterprise-grade, high-performance URL shortener built with .NET 8 Minimal APIs, following Strict Clean Architecture and CQRS principles. It is designed for maximum throughput, zero-allocation hot paths, and absolute resilience against security threats (OWASP API Security Top 10 2023).

## 🚀 Key Features

*   **Strict Clean Architecture & CQRS:** Uncompromising separation of concerns, decoupling the domain from infrastructure and API delivery layers.
*   **High Performance Analytics:** Implements a decoupled producer-consumer pattern using `System.Threading.Channels` (`Capacity = 10,000`, `FullMode = DropOldest`). This guarantees a zero-allocation hot path for redirection events.
*   **Collision-Free Encoding:** Uses deterministic Base62 encoding on auto-incrementing integers, mathematically guaranteeing the shortest possible string without collision risks.
*   **Lightweight Persistence:** Uses SQLite with Entity Framework Core, optimized with concurrency controls (catching `try-catch(DbUpdateException)` for TOCTOU race conditions). Provider-agnostic design allows seamless scaling to PostgreSQL/SQL Server.
*   **Advanced Security:**
    *   **SSRF Protection:** Built-in `UrlSafetyValidator` blocking internal IP ranges, loopbacks, and non-HTTP(S) schemes.
    *   **Rate Limiting:** Sliding-window rate limiting middleware (30 requests/minute per IP) to mitigate abuse and DDoS.
*   **Test-Driven:** Comprehensive Unit and Integration Test coverage utilizing dynamically unique test databases per test class to eliminate parallel execution race conditions.

## 🛠️ Tech Stack

*   **.NET 8 Minimal APIs**
*   **Entity Framework Core**
*   **SQLite**
*   **System.Threading.Channels** (For asynchronous analytics processing)
*   **xUnit & Moq** (For comprehensive testing)

## 📦 Getting Started

### Prerequisites

*   [.NET 8 SDK or newer (e.g., .NET 10)](https://dotnet.microsoft.com/en-us/download/dotnet) installed on your machine.

### Running the Application Locally

1.  **Clone the repository** (if you haven't already):
    ```bash
    git clone https://github.com/martinnv6/URL-Shortener.git
    cd URL-Shortener
    ```

2.  **Build and Run the API**:
    Navigate to the root directory and start the project:
    ```bash
    dotnet run --project src/UrlShortener.Api/UrlShortener.Api.csproj --roll-forward Major
    ```
    *(The `--roll-forward Major` flag allows the .NET 8 app to run seamlessly on newer runtimes like .NET 10).*

3.  **Explore the API via Swagger**:
    Once the application is running, open your browser and navigate to:
    ```
    http://localhost:5046/swagger
    ```

4.  **Quick API Test**:
    *   **Shorten a URL**:
        ```bash
        curl -X POST http://localhost:5046/api/v1/urls \
          -H "Content-Type: application/json" \
          -d '{"url": "https://google.com"}'
        ```
    *   **Redirect**:
        ```bash
        curl -i http://localhost:5046/1
        ```
    *   **Inspect Analytics**:
        ```bash
        curl http://localhost:5046/api/v1/urls/1/analytics
        ```

### Running the Tests

To execute the comprehensive test suite across all projects:
```bash
dotnet test
```

*(Or explicitly with the local .NET 8 SDK: `~/.dotnet/dotnet test tests/UrlShortener.UnitTests/UrlShortener.UnitTests.csproj && ~/.dotnet/dotnet test tests/UrlShortener.IntegrationTests/UrlShortener.IntegrationTests.csproj && ~/.dotnet/dotnet test tests/UrlShortener.FunctionalTests/UrlShortener.FunctionalTests.csproj`)*

## 📖 Architecture Overview

The project is divided into four main layers:

1.  **UrlShortener.Core:** Contains the enterprise business logic, entities, service interfaces, and Base62 encoding algorithms. Completely dependency-free.
2.  **UrlShortener.Infrastructure:** Implements the core interfaces (e.g., `IUrlRepository`). Houses EF Core configurations, the SQLite `AppDbContext`, and the background worker (`AnalyticsProcessingWorker`) for batching click events.
3.  **UrlShortener.Api:** The presentation layer exposing Minimal APIs. It wires up Dependency Injection, Swagger, Rate Limiting Middleware, and OWASP safety validators.
4.  **UrlShortener.UnitTests:** A robust testing boundary validating every layer of the application.

## 🛡️ Security Posture

This project strictly adheres to the OWASP API Security Top 10 (2023). Key implementations include:
*   **API4:2023 Unrestricted Resource Consumption:** Mitigated via `SlidingWindowRateLimiterMiddleware`.
*   **API10:2023 Unsafe Consumption of APIs (SSRF):** Addressed via strict URI parsing and local network blocking.

## 🏷️ Assessment Milestones (Branches & Tags)

The repository's development lifecycle is organized into specific Git tags and branches corresponding to each scenario requested in the assessment brief:

| Scenario / Milestone | Git Tag | Git Branch | Commit Hash | Scope & Key Deliverables |
| :--- | :--- | :--- | :--- | :--- |
| **Part 1: Greenfield** | [`greenfield`](https://github.com/martinnv6/URL-Shortener/releases/tag/greenfield) | [`scenario/greenfield`](https://github.com/martinnv6/URL-Shortener/tree/scenario/greenfield) | `a826224` | Initial system design, Core domain abstractions, Base62 encoding algorithm, decoupled in-memory click analytics service, contract models, and unit tests. |
| **Part 2: Brownfield** | [`brownfield`](https://github.com/martinnv6/URL-Shortener/releases/tag/brownfield) | [`scenario/brownfield`](https://github.com/martinnv6/URL-Shortener/tree/scenario/brownfield) | `7b0a8f0` | SQLite persistence with EF Core, asynchronous analytics background channel worker (`System.Threading.Channels`), sliding-window rate limiting middleware, SSRF URL safety validation, and full test suite. |
| **Part 3: Ambiguous** | [`ambiguous`](https://github.com/martinnv6/URL-Shortener/releases/tag/ambiguous) | [`scenario/ambiguous`](https://github.com/martinnv6/URL-Shortener/tree/scenario/ambiguous) | `8182876` | Architectural AI peer review, fixing ambiguous specifications (e.g., UTC timestamp normalization to `DateTimeOffset`), race-condition handling, and expanded analytics verification. |
| **Current Mainline** | — | [`main`](https://github.com/martinnv6/URL-Shortener/tree/main) | `HEAD` | Production Visual Studio solution file (`UrlShortener.sln`), GitHub Actions CI pipeline ([`.github/workflows/ci.yml`](./.github/workflows/ci.yml)), and full documentation. |

### Why Both Branches and Tags Exist
Both are valid and serve complementary purposes:
*   **Git Tags:** Serve as **immutable milestone releases**. Reviewers can immediately check out a tag or view it under GitHub Releases to review the exact state of each deliverable without branch divergence.
*   **Git Branches (`scenario/*`):** Demonstrate a **real-world SDLC branch-based delivery workflow**, showcasing feature isolation and PR readiness.

## 📚 Project Documentation

Detailed design decisions, implementation plans, and architectural reviews are documented in the [`docs/`](./docs) directory:

### Core Summaries & Architecture
*   [`FINAL_ENGINEERING_SUMMARY.md`](./docs/FINAL_ENGINEERING_SUMMARY.md) — Comprehensive technical wrap-up covering requirement fulfillment, architectural decisions, security audits, and test metrics.
*   [`ARCHITECTURE_PLAN.md`](./docs/ARCHITECTURE_PLAN.md) — Architectural blueprint detailing Clean Architecture layers, CQRS data flow, and technology choices.
*   [`AI_Collaboration.md`](./docs/AI_Collaboration.md) — Detailed chronological log of AI-assisted engineering prompts, rationale, and iterative reviews.

### Review & Issue Resolutions
*   [`docs/Issues/Existent/peer_review.md`](./docs/Issues/Existent/peer_review.md) — Exhaustive code and architecture peer review identifying edge cases, concurrency hazards, and potential ambiguities.
*   [`docs/Issues/Existent/implementation_plan.md`](./docs/Issues/Existent/implementation_plan.md) — Actionable implementation plan resolving findings from the peer review.
*   [`implementation_plan_Fix_DateTimeOffset`](./docs/implementation_plan_Fix_DateTimeOffset) — Targeted fix plan addressing timezone ambiguity by migrating temporal fields to `DateTimeOffset`.

### Implementation Walkthroughs & Testing Plans
*   [`Walkthrough -- Brownfield Refactor - EF Core Persistence & Analytics Pipeline`](./docs/Walkthrough%20--%20Brownfield%20Refactor%20-%20EF%20Core%20Persistence%20%26%20Analytics%20Pipeline) — Step-by-step breakdown of introducing SQLite EF Core persistence and asynchronous analytics queueing.
*   [`Walkthrough — OWASP API Security Hardening`](./docs/Walkthrough%20—%20OWASP%20API%20Security%20Hardening) — Walkthrough of security mitigations (SSRF prevention, sliding-window rate limiter).
*   [`Walkthrough -- URL Shortener Test Coverage Improvements Implementation`](./docs/Walkthrough%20--%20URL%20Shortener%20Test%20Coverage%20Improvements%20Implementation) — Summary of test suite expansion across domain and infrastructure boundaries.
*   [`Functional Tests Implementation Plan (Revision 2 - Comprehensive)`](./docs/Functional%20Tests%20Implementation%20Plan%20%28Revision%202%20-%20Comprehensive%29) — Detailed strategy for integration and end-to-end HTTP pipeline testing.
*   [`Test Coverage Implementation Plan`](./docs/Test%20Coverage%20Implementation%20Plan) — Initial coverage baseline analysis and target metrics roadmap.

