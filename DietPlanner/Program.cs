using DietPlanner;
using DietPlanner.Authentication;
using DietPlanner.Components;
using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.Testing;
using DietPlanner.Endpoints.WeekPlan;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Replace the default plain-text console formatter with structured logging: the plain formatter
// writes multi-line messages (e.g. exception stack traces) as continuation lines with no level
// marker of their own, which log viewers that read stdout line-by-line (Fly's log viewer, basic
// Docker log shippers) misattribute to whatever level/category preceded them - that's why error
// logs have been showing up tagged as "info". JSON console emits one self-contained record per
// line, and the OpenTelemetry logging provider gives every record a properly classified severity
// that can be exported to any OTEL-compatible backend by swapping the exporter below.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddOpenTelemetry(otelOptions =>
{
    otelOptions.IncludeScopes = true;
    otelOptions.IncludeFormattedMessage = true;
    otelOptions.ParseStateValues = true;
    otelOptions.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("DietPlanner"));
    otelOptions.AddConsoleExporter();
});

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
var services = builder.Services;
services.AddEndpointsApiExplorer();
services.AddSwaggerGen();

// Turns unhandled exceptions from the minimal API endpoints into a standard ProblemDetails
// response (instead of an empty 500) and logs them via IExceptionHandler's default handler.
services.AddProblemDetails();

#if DEBUG
services.AddSassCompiler();
#endif

// Razor Components power the interactive Diet Planner / Meals / Settings web pages, rendered
// server-side (Blazor Server) so the UI can call straight into the same EF Core services that
// back the minimal API below, without needing a separate JavaScript HTTP client layer.
services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

// Default SignalR ClientTimeoutInterval (30s) is too short for the large CSV/PDF meal uploads
// (MealUploadPage.razor) on slower connections: if a single file-data chunk doesn't arrive in
// time, the circuit drops with "Did not receive any data in the allotted time" mid-upload.
services.Configure<HubOptions>(options => options.ClientTimeoutInterval = TimeSpan.FromSeconds(60));

services.AddScoped<ISettingsService, SettingsService>()
    .AddScoped<IDayPlanService, DayPlanService>()
    .AddScoped<IWeekPlanService, WeekPlanService>()
    .AddScoped<IWeekPlanGeneratorService, WeekPlanGeneratorService>()
    .AddScoped<IMealsService, MealsService>()
    .AddScoped<IMealImportService, MealImportService>()
    .AddScoped<ISlotsService, SlotsService>();

services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
services.AddHttpClient<IMealPdfImportService, MealPdfImportService>();
services.AddHttpClient<IIngredientNormalizationService, IngredientNormalizationService>();
services.AddHttpClient<IIngredientGroupingService, IngredientGroupingService>();
services.AddHttpClient<IMealAdditionSuggestionService, MealAdditionSuggestionService>();

// Runs PDF imports / ingredient normalization on a detached background task so a slow multi-chunk
// Anthropic call doesn't hold the Blazor circuit's request open long enough to hit a SignalR/proxy
// timeout; the upload page polls these singletons for job status instead of awaiting inline.
services.AddSingleton<IMealPdfImportJobService, MealPdfImportJobService>();
services.AddSingleton<IIngredientNormalizationJobService, IngredientNormalizationJobService>();

// Default DataProtection key storage is ephemeral (in-memory/local temp dir). Combined with
// auto_stop_machines/min_machines_running=0 in fly.toml, every cold start spun up a brand new
// key ring, so antiforgery cookies issued before the previous stop could never be decrypted
// again ("key ... was not found in the key ring"). Persisting keys to the same mounted volume
// used for the SQLite database keeps the ring stable across restarts.
string? dataProtectionKeysPath = builder.Configuration["DataProtectionKeysPath"];
if (!string.IsNullOrEmpty(dataProtectionKeysPath))
{
    services.AddDataProtection()
        .SetApplicationName("DietPlanner")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

services.AddValidatorsFromAssemblyContaining<Program>();
services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Db")));

bool authConfigured = !string.IsNullOrEmpty(builder.Configuration["APP_USERNAME"]) &&
    !string.IsNullOrEmpty(builder.Configuration["APP_PASSWORD"]);
if (!authConfigured && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "APP_USERNAME and APP_PASSWORD must be set outside Development (set as Fly secrets).");
}

services.AddAuthentication(BasicAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(BasicAuthenticationHandler.SchemeName, null);

// Single-user deployment: every endpoint requires auth unless explicitly marked [AllowAnonymous].
// In Development without credentials configured, leave the default (anonymous-friendly) policy
// so `dotnet run` works without setting APP_USERNAME/APP_PASSWORD.
services.AddAuthorization(options =>
{
    if (authConfigured)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    }
});

WebApplication app = builder.Build();

// Volume-mounted SQLite starts empty on first boot; apply migrations so the
// schema exists without needing to ship a pre-built .db file to the volume.
using (IServiceScope scope = app.Services.CreateScope())
{
    AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();

    // Slots are reference data the UI assumes always exists (meals can't be saved against a
    // slot that isn't in this table, and there's no UI to create one), so seed the fixed set
    // here rather than relying on a pre-populated database.
    if (!await dbContext.Slots.AnyAsync())
    {
        dbContext.Slots.AddRange(
            new Slot(SlotKey.Breakfast, "Breakfast", 1),
            new Slot(SlotKey.Lunch, "Lunch", 2),
            new Slot(SlotKey.Dinner, "Dinner", 3),
            new Slot(SlotKey.BeforeBed, "Before bed", 4));
        await dbContext.SaveChangesAsync();
    }
}

// Configure the HTTP request pipeline.

// Must run before other middleware so it can catch exceptions thrown further down the pipeline;
// pairs with AddProblemDetails() above to turn unhandled exceptions into a ProblemDetails response.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Single-user deployment: reject requests from non-whitelisted IPs before they reach
// auth/Blazor/EF Core, so floods cost a header comparison rather than a SignalR connection
// or a DB hit. No Microsoft authentication package covers network-level ACLs, so this stays
// as plain middleware; actual credential checking is delegated to BasicAuthenticationHandler.
string? allowedIpsRaw = builder.Configuration["ALLOWED_IPS"];
HashSet<string>? allowedIps = string.IsNullOrWhiteSpace(allowedIpsRaw)
    ? null
    : allowedIpsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();

if (allowedIps is not null)
{
    app.Use(async (context, next) =>
    {
        // Fly's edge always overwrites this header, so clients cannot spoof it.
        string? clientIp = context.Request.Headers["Fly-Client-IP"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString();

        if (clientIp is null || !allowedIps.Contains(clientIp))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next();
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapSettingsEndpoints();
app.MapDayEndpoints();
app.MapMealsEndpoints();
app.MapSlotsEndpoints();
app.MapWeekEndpoints();

// The e2e suite reuses one app container across the whole run for speed and resets state between
// tests via this endpoint instead of recreating the container per test.
if (builder.Configuration.GetValue<bool>("E2E_TESTING"))
{
    app.MapTestResetEndpoints();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }