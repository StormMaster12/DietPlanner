# CLAUDE.md

Guidance for Claude Code (and other AI assistants) working in this repository.

## What this is

DietPlanner is a single-user Blazor Server app (.NET 8) for planning meals: tracking
meal nutrition (kcal/protein/carbs/fibre/plants), assigning meals to daily slots
(Breakfast/Lunch/Dinner/Before bed), generating week plans, and importing meals from
CSV/PDF (using the Anthropic API to extract structured data from PDF text). It's
deployed as a single Fly.io machine with a SQLite database on a mounted volume.

## Solution layout

```
DietPlanner.sln
├── DietPlanner/              # the app (ASP.NET Core minimal API + Blazor Server)
├── DietPlanner.Tests/        # NUnit unit tests (service layer, in-memory SQLite)
└── DietPlanner.E2ETests/     # NUnit + Playwright browser tests against the real Docker image
```

### `DietPlanner/` structure

- `Program.cs` — composition root: DI registration, middleware pipeline, migrations-on-
  startup, slot seeding. Read this first to see how everything wires together. Comments
  here explain several non-obvious decisions (logging format, SignalR timeout, IP
  allowlisting, DataProtection key persistence) — read them before changing related code.
- `Endpoints/<Feature>/` — each feature (DayPlan, Meal, Settings, Slots, WeekPlan,
  Testing) is a vertical slice containing its own minimal-API endpoint class
  (`*Endpoints.cs`), service interface + implementation, EF Core entity, and DTOs. There
  is no separate "Models"/"Repositories"/"Controllers" layering — keep new features
  organized the same way, as a folder under `Endpoints/`.
- `Components/` — Blazor Server pages (`Pages/`) and layout (`Layout/`). Pages call
  straight into the same `I*Service` interfaces used by the minimal API (no separate
  JS HTTP client layer). Razor component styles are written as `.razor.scss` and
  compiled to `.razor.css` by `AspNetCore.SassCompiler` in dev builds.
- `Migrations/` — EF Core migrations for the SQLite database.
- `Authentication/BasicAuthenticationHandler.cs` — HTTP Basic Auth; the entire app
  requires authentication outside Development unless an endpoint is `[AllowAnonymous]`.
- `AppDbContext.cs` — registers all `DbSet`s; each entity's EF configuration lives next
  to the entity in its `Endpoints/<Feature>/` folder via `Configure*Entity()` extension
  methods called from `OnModelCreating`.

### Key conventions

- **Vertical slices, not layers.** A new feature gets its own `Endpoints/<Feature>/`
  folder with everything it needs (endpoints, service, DTOs, entity). Don't introduce a
  generic repository or controller layer.
- **Minimal API + `TypedResults`.** Endpoints return `Results<...>` / `TypedResults`
  unions for explicit status codes (see `MealEndpoints.cs`), not `IActionResult`.
- **Services take `CancellationToken`** on every async method and are registered
  `Scoped` (EF Core-backed) or `Singleton` (background job trackers like
  `IMealPdfImportJobService`) in `Program.cs`.
- **FluentValidation** validators are auto-registered via
  `AddValidatorsFromAssemblyContaining<Program>()`; endpoints validate explicitly and
  return `TypedResults.ValidationProblem(...)` on failure (see `UpsertMealAsync`).
- **Background jobs for slow external calls.** PDF import and ingredient normalization
  (both call the Anthropic API) run via singleton job services rather than awaited
  inline in the request, because a slow LLM call can outlast a SignalR circuit's
  timeout. The Blazor page polls a `GET /.../{jobId}` status endpoint instead.
- **Nullable + implicit usings** are enabled project-wide; write nullable-aware code.
- **Records** (e.g. `UpsertMealRequest`, DTOs) are used for immutable request/response
  shapes, often `with`-cloned in tests.
- Comments in this codebase explain *why*, not *what* — match that style if you add
  comments (see `Program.cs` for examples). Don't add comments that just restate the
  code.

## Build, test, run

```bash
dotnet restore DietPlanner.sln
dotnet build DietPlanner.sln --configuration Release

# Unit tests (fast, in-memory SQLite, no Docker required)
dotnet test DietPlanner.Tests/DietPlanner.Tests.csproj

# E2E tests (builds and runs the real Docker image + Playwright + a WireMock stand-in
# for the Anthropic API via Testcontainers — requires Docker)
dotnet build DietPlanner.E2ETests/DietPlanner.E2ETests.csproj --configuration Release
pwsh DietPlanner.E2ETests/bin/Release/net8.0/playwright.ps1 install --with-deps chromium
dotnet test DietPlanner.E2ETests/DietPlanner.E2ETests.csproj --no-build --configuration Release

# Run the app locally (Development env skips the auth requirement)
dotnet run --project DietPlanner/DietPlanner.csproj
```

This mirrors `.github/workflows/ci.yml` exactly, which runs on every PR and on push to
`master`: restore → build → unit tests → build E2E project → install Playwright
Chromium → run E2E tests. On push to `master` (after tests pass) it also builds and
pushes the Docker image to `ghcr.io/<repo>:latest` / `:<sha>`.

### Unit tests (`DietPlanner.Tests`)

- NUnit, `[TestFixture]` / `[Test]` / `[SetUp]` / `[TearDown]`.
- Each test gets a fresh `TestDatabase` (SQLite `:memory:`, kept alive via an open
  connection — see `TestDbContextFactory.cs`) and constructs the service under test
  directly with `new MealsService(db)` rather than going through DI.
- Assertions use the NUnit constraint model: `Assert.That(x, Is.EqualTo(y))`.

### E2E tests (`DietPlanner.E2ETests`)

- NUnit + `Microsoft.Playwright.NUnit`. All page tests inherit `PageTestBase`.
- A single app instance (`DietPlannerAppFactory`) is built via Testcontainers from the
  repo's own `Dockerfile` and shared across the whole run — tests must run sequentially
  (NUnit's default), not in parallel, since they share one container and database.
- `[SetUp]` calls `ResetStateAsync()`, which hits a `/__test__/reset` endpoint
  (`Endpoints/Testing/TestResetEndpoints.cs`, only mapped when `E2E_TESTING=true`) to
  wipe per-test data instead of recreating the container.
- A WireMock container stands in for the real Anthropic API; use
  `StubAnthropicResponseAsync(...)` in a test to stub the next `/v1/messages` response
  instead of calling the real API.
- Use `GoToAsync(relativePath)` from `PageTestBase` to navigate — it waits for
  `NetworkIdle` so the Blazor SignalR circuit is connected before interacting with the
  page (interacting earlier silently no-ops).

## Configuration

- Connection string: `ConnectionStrings:Db` (SQLite file path; `Data Source=:memory:`
  pattern used in tests).
- `Anthropic:ApiKey` / `Anthropic:Model` / `Anthropic:BaseUrl` — used for PDF import and
  ingredient normalization. **Never commit a real API key** — use user secrets or the
  `Anthropic__ApiKey` environment variable locally; it's a Fly secret in production.
- `APP_USERNAME` / `APP_PASSWORD` — Basic Auth credentials, required outside
  Development (the app throws on startup in Production if unset).
- `ALLOWED_IPS` — optional comma-separated IP allowlist checked against Fly's
  `Fly-Client-IP` header before auth.
- `E2E_TESTING=true` — enables the `/__test__/reset` endpoint; only ever set in the E2E
  container.

## Deployment

Single Fly.io app (`fly.toml`: app `dieting-app`, region `iad`, 1 always-on
shared-cpu-1x machine, SQLite + DataProtection keys on a mounted volume so both survive
restarts). CI builds and pushes `ghcr.io/<repo>:latest` on every merge to `master`; Fly
deploys are not done by CI in this repo — check with the user before assuming a deploy
should happen automatically.

## Working in this repo

- Follow `.editorconfig` (4-space indent, CRLF line endings, `this.`/`Me.` qualification
  off, predefined types over BCL names, namespace-matches-folder).
- When adding a feature, look at an existing `Endpoints/<Feature>/` folder (e.g. `Meal`
  or `Slots`) as the template for how endpoints/service/DTOs/entity are split.
- When changing EF Core entities, add a migration (`dotnet ef migrations add ...` from
  `DietPlanner/`) rather than hand-editing `AppDbContextModelSnapshot.cs`.
- Don't add a deploy step or push to Fly without explicit instruction — this file
  documents the existing pipeline, it doesn't authorize new automated deploys.
