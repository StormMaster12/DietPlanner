using DietPlanner;
using DietPlanner.Authentication;
using DietPlanner.Components;
using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
var services = builder.Services;
services.AddEndpointsApiExplorer();
services.AddSwaggerGen();

#if DEBUG
services.AddSassCompiler();
#endif

// Razor Components power the interactive Diet Planner / Meals / Settings web pages, rendered
// server-side (Blazor Server) so the UI can call straight into the same EF Core services that
// back the minimal API below, without needing a separate JavaScript HTTP client layer.
services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

services.AddScoped<ISettingsService, SettingsService>()
    .AddScoped<IDayPlanService, DayPlanService>()
    .AddScoped<IWeekPlanService, WeekPlanService>()
    .AddScoped<IWeekPlanGeneratorService, WeekPlanGeneratorService>()
    .AddScoped<IMealsService, MealsService>()
    .AddScoped<IMealImportService, MealImportService>()
    .AddScoped<ISlotsService, SlotsService>();

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
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// Configure the HTTP request pipeline.
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }